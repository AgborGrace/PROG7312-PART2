using SmartX.Api.Collections;
using SmartX.Api.Models;

namespace SmartX.Api.Services;

public enum CommandOutcome
{
    Executed,
    UnknownDevice,
    NotAnActuator,
    NothingToDo
}

/// <summary>
/// THE dedicated class that holds the Part 2 data structures. Each structure is named after its purpose
/// (as the lecturer recommended) so every requirement of the brief is easy to find and mark:
///
///   TelemetryQueue     Queue&lt;T&gt;              FIFO processing of normal incoming telemetry packets
///   CriticalAlertQueue PriorityQueue&lt;,&gt;      critical alerts that BYPASS the normal queue
///   CommandUndoStack   Stack&lt;T&gt;              manual override history -> "Undo" (never throws on empty)
///   DeviceRegistry     Dictionary&lt;,&gt;         MAC address -> device, O(1) lookup for every packet
///   SensorTimeline     SortedDictionary&lt;,&gt;   per-device readings kept in timestamp order for timeline graphs
///   DisconnectedNodes  HashSet&lt;T&gt;            nodes currently down (a node can only be in the set once)
///   ActiveAlertKeys    HashSet&lt;T&gt;            active alerts ("MAC:Type") -> duplicate alerts are suppressed
///
/// Everything is guarded by one lock because ASP.NET handles requests on many threads while a background
/// service is draining the queue at the same time.
/// </summary>
public class SmartXDataStore
{
    private readonly object _sync = new();
    private readonly PredictiveEngine _engine;

    private sealed record QueuedTelemetry(SensorReading Reading, DateTime EnqueuedUtc);
    private sealed record CriticalItem(SensorReading Reading, ReadingEvaluation Evaluation, DateTime EnqueuedUtc);

    // ============================ the named data structures ============================

    /// <summary>FIFO queue of normal telemetry waiting to be processed (Queue&lt;T&gt;).</summary>
    private readonly Queue<QueuedTelemetry> TelemetryQueue = new();

    /// <summary>Critical alerts; lower (Priority, Seq) = processed first (PriorityQueue).</summary>
    private readonly PriorityQueue<CriticalItem, (int Priority, long Seq)> CriticalAlertQueue = new();

    /// <summary>Manual override history; top of the stack = the command "Undo" reverts (Stack&lt;T&gt;).</summary>
    private readonly Stack<DeviceCommand> CommandUndoStack = new();

    /// <summary>Registered devices keyed on MAC address -> instant look-ups (Dictionary).</summary>
    private readonly Dictionary<string, DeviceRuntime> DeviceRegistry = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-device history keyed on timestamp, always sorted (SortedDictionary).</summary>
    private readonly Dictionary<string, SortedDictionary<DateTime, SensorReading>> SensorTimeline =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>MACs of nodes that stopped reporting (HashSet).</summary>
    private readonly HashSet<string> DisconnectedNodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keys of alerts that are active right now; Add() returning false == duplicate (HashSet).</summary>
    private readonly HashSet<string> ActiveAlertKeys = new(StringComparer.OrdinalIgnoreCase);

    // ===================================================================================

    private readonly BoundedLog<AlertRecord> _recentAlerts = new(200);
    private readonly BoundedLog<ProcessedEvent> _processingLog = new(100);
    private long _alertSeq, _processedSeq, _criticalSeq, _commandSeq;
    private long _totalProcessed, _criticalProcessed, _duplicatesSuppressed;

    private const int MaxTimelinePerDevice = 500;
    private const int MaxQueueDepth = 5000;

    public SmartXDataStore(PredictiveEngine engine) => _engine = engine;

    private static string Key(string mac, AlertType type) => $"{mac}:{type}";

    // ---------------------------------------------------------------- devices

    public bool TryRegisterDevice(SensorRegistration reg, out string error)
    {
        lock (_sync)
        {
            var mac = DeviceImportParser.NormalizeMac(reg.MacAddress);
            reg.MacAddress = mac;
            if (string.IsNullOrWhiteSpace(reg.Name)) reg.Name = mac;
            if (string.IsNullOrWhiteSpace(reg.Unit)) reg.Unit = DeviceImportParser.DefaultUnit(reg.Category);
            reg.RegisteredUtc = DateTime.UtcNow;

            // Dictionary.TryAdd is O(1) and rejects a duplicate MAC without scanning anything.
            if (!DeviceRegistry.TryAdd(mac, new DeviceRuntime(reg)))
            {
                error = $"A device with MAC address {mac} is already registered.";
                return false;
            }

            SensorTimeline[mac] = new SortedDictionary<DateTime, SensorReading>();
            error = string.Empty;
            return true;
        }
    }

    /// <summary>Loads historical readings (from the Part 1 jagged-array batch) into the sorted timeline.</summary>
    public void SeedTimeline(string mac, IEnumerable<SensorReading> readings)
    {
        lock (_sync)
        {
            if (!SensorTimeline.TryGetValue(mac, out var timeline)) return;
            SensorReading? last = null;
            foreach (var r in readings)
            {
                var ts = r.TimestampUtc;
                while (!timeline.TryAdd(ts, r)) ts = ts.AddTicks(1);
                last = r;
            }

            // The seed gives spike detection a realistic baseline, but it must NOT make the device look
            // "online": LastSeenUtc/Status stay untouched until real telemetry arrives.
            if (last is not null && DeviceRegistry.TryGetValue(mac, out var device)) device.LastReading = last;
        }
    }

    public bool DeviceExists(string mac)
    {
        lock (_sync) return DeviceRegistry.ContainsKey(mac);
    }

    public bool AttachFile(string mac, string fileName)
    {
        lock (_sync)
        {
            if (!DeviceRegistry.TryGetValue(mac, out var device)) return false;
            device.Registration.AttachedFileName = fileName;
            return true;
        }
    }

    public DeviceView? GetDevice(string mac)
    {
        lock (_sync) return DeviceRegistry.TryGetValue(mac, out var d) ? ToView(d) : null;
    }

    public List<DeviceView> GetDevices(string? search, SensorCategory? category, DeviceStatus? status)
    {
        lock (_sync)
        {
            IEnumerable<DeviceRuntime> query = DeviceRegistry.Values;

            if (category.HasValue) query = query.Where(d => d.Registration.Category == category.Value);
            if (status.HasValue) query = query.Where(d => d.Status == status.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(d =>
                    d.Registration.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    d.Registration.MacAddress.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    d.Registration.Zone.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    d.Registration.Facility.Contains(s, StringComparison.OrdinalIgnoreCase));
            }

            return query
                .OrderBy(d => d.Registration.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToView)
                .ToList();
        }
    }

    private static DeviceView ToView(DeviceRuntime d) => new()
    {
        Mac = d.Registration.MacAddress,
        Name = d.Registration.Name,
        Category = d.Registration.Category,
        Unit = d.Registration.Unit,
        Facility = d.Registration.Facility,
        Zone = d.Registration.Zone,
        SubZone = d.Registration.SubZone,
        Status = d.Status,
        LastValue = d.LastReading is not null && d.LastSeenUtc.HasValue ? d.LastReading.Value : null,
        LastSeenUtc = d.LastSeenUtc,
        IsOn = d.IsOn,
        AttachedFileName = d.Registration.AttachedFileName,
        RegisteredUtc = d.Registration.RegisteredUtc
    };

    // ---------------------------------------------------------------- telemetry ingestion

    public int TelemetryQueueDepth
    {
        get { lock (_sync) return TelemetryQueue.Count; }
    }

    /// <summary>
    /// Entry point for every packet that hits the API.
    ///  - Unknown MAC            -> rejected (Dictionary look-up, O(1)).
    ///  - CRITICAL reading       -> pushed on the CriticalAlertQueue and processed IMMEDIATELY (bypasses the FIFO queue).
    ///  - Everything else        -> enqueued on the FIFO TelemetryQueue; the background service processes it.
    /// </summary>
    public IngestResult Ingest(TelemetryPacket<double> packet)
    {
        lock (_sync)
        {
            if (!DeviceRegistry.TryGetValue(packet.DeviceMac, out var device))
            {
                return new IngestResult
                {
                    Outcome = IngestOutcome.UnknownDevice,
                    QueueDepth = TelemetryQueue.Count,
                    Message = $"Unknown device {packet.DeviceMac}. Register it first."
                };
            }

            var reg = device.Registration;
            var reading = new SensorReading(reg.MacAddress, packet.Value,
                string.IsNullOrWhiteSpace(packet.Unit) ? reg.Unit : packet.Unit)
            {
                TimestampUtc = packet.TimestampUtc
            };

            var evaluation = ThresholdPolicy.Evaluate(reg, reading, device.LastReading);

            if (evaluation.Class == ReadingClass.Critical)
            {
                int priority = reg.Category switch
                {
                    SensorCategory.PowerConsumption => 0,   // electrical faults first
                    SensorCategory.Environmental => 1,
                    _ => 2
                };

                int waiting = TelemetryQueue.Count;
                CriticalAlertQueue.Enqueue(new CriticalItem(reading, evaluation, DateTime.UtcNow), (priority, ++_criticalSeq));
                DrainCriticalLocked();   // processed right now, without waiting behind the FIFO queue

                return new IngestResult
                {
                    Outcome = IngestOutcome.CriticalProcessed,
                    QueueDepth = TelemetryQueue.Count,
                    Message = $"CRITICAL: {evaluation.Reason}. Processed immediately, ahead of {waiting} queued packet(s)."
                };
            }

            if (TelemetryQueue.Count >= MaxQueueDepth)
            {
                return new IngestResult
                {
                    Outcome = IngestOutcome.QueueFull,
                    QueueDepth = TelemetryQueue.Count,
                    Message = "Telemetry queue is full — slow down and retry."
                };
            }

            TelemetryQueue.Enqueue(new QueuedTelemetry(reading, DateTime.UtcNow));
            return new IngestResult
            {
                Outcome = IngestOutcome.Queued,
                QueueDepth = TelemetryQueue.Count,
                Message = "Queued for processing."
            };
        }
    }

    /// <summary>Called by the background service: critical alerts first, then up to <paramref name="max"/> FIFO packets.</summary>
    public int ProcessQueuedBatch(int max)
    {
        lock (_sync)
        {
            DrainCriticalLocked();

            int processed = 0;
            while (processed < max && TelemetryQueue.TryDequeue(out var item))
            {
                ApplyReadingLocked(item.Reading, "Normal", null, item.EnqueuedUtc);
                processed++;
            }

            return processed;
        }
    }

    // Caller must hold _sync.
    private void DrainCriticalLocked()
    {
        while (CriticalAlertQueue.TryDequeue(out var item, out _))
            ApplyReadingLocked(item.Reading, "Critical", item.Evaluation, item.EnqueuedUtc);
    }

    // Caller must hold _sync. Applies one reading to the device, the timeline and the alert logic.
    private void ApplyReadingLocked(SensorReading reading, string kind, ReadingEvaluation? forced, DateTime enqueuedUtc)
    {
        if (!DeviceRegistry.TryGetValue(reading.DeviceMac, out var device)) return;

        var reg = device.Registration;
        var now = DateTime.UtcNow;

        // 1. SensorTimeline: SortedDictionary keyed on timestamp (bumped by a tick if two readings collide).
        var timeline = SensorTimeline[reg.MacAddress];
        var ts = reading.TimestampUtc;
        while (!timeline.TryAdd(ts, reading)) ts = ts.AddTicks(1);
        while (timeline.Count > MaxTimelinePerDevice) timeline.Remove(timeline.Keys.First());

        // 2. Evaluate against the thresholds (critical readings arrive already evaluated).
        var evaluation = forced ?? ThresholdPolicy.Evaluate(reg, reading, device.LastReading);

        // 3. A node that talks again is no longer disconnected.
        DisconnectedNodes.Remove(reg.MacAddress);
        ActiveAlertKeys.Remove(Key(reg.MacAddress, AlertType.Disconnected));

        device.LastReading = reading;
        device.LastSeenUtc = now;

        // 4. Status + alerts. RaiseAlertLocked uses the ActiveAlertKeys HashSet to drop duplicates.
        switch (evaluation.Class)
        {
            case ReadingClass.Critical:
                device.Status = DeviceStatus.Critical;
                RaiseAlertLocked(device, AlertType.CriticalReading, AlertSeverity.Critical, evaluation.Reason);
                break;

            case ReadingClass.OutOfRange:
                device.Status = DeviceStatus.Warning;
                ActiveAlertKeys.Remove(Key(reg.MacAddress, AlertType.CriticalReading));   // improved from critical
                RaiseAlertLocked(device, AlertType.OutOfRange, AlertSeverity.Warning, evaluation.Reason);
                break;

            default:
                device.Status = DeviceStatus.Normal;
                // Recovered: clear the keys so the NEXT problem raises a fresh alert.
                ActiveAlertKeys.Remove(Key(reg.MacAddress, AlertType.CriticalReading));
                ActiveAlertKeys.Remove(Key(reg.MacAddress, AlertType.OutOfRange));
                break;
        }

        // 5. Processing log — proves the processing order (critical vs FIFO) and how long each packet waited.
        _processingLog.Add(new ProcessedEvent
        {
            Seq = ++_processedSeq,
            Kind = kind,
            Mac = reg.MacAddress,
            DeviceName = reg.Name,
            Value = reading.Value,
            Unit = reading.Unit,
            ProcessedUtc = now,
            WaitMs = Math.Round((now - enqueuedUtc).TotalMilliseconds, 0)
        });
        _totalProcessed++;
        if (kind == "Critical") _criticalProcessed++;
    }

    // Caller must hold _sync.
    private bool RaiseAlertLocked(DeviceRuntime device, AlertType type, AlertSeverity severity, string message)
    {
        var mac = device.Registration.MacAddress;
        var key = Key(mac, type);

        // HashSet.Add returns false when the key is already present -> the same alert is NOT raised twice.
        if (!ActiveAlertKeys.Add(key))
        {
            _duplicatesSuppressed++;
            return false;
        }

        var now = DateTime.UtcNow;
        _recentAlerts.Add(new AlertRecord
        {
            Id = ++_alertSeq,
            Mac = mac,
            DeviceName = device.Registration.Name,
            Type = type,
            Severity = severity,
            Message = message,
            RaisedUtc = now
        });

        // Tell the predictive engine an alert happened, so it can learn what the operator does next.
        _engine.RecordTrigger(key, device.Registration.Name, message, now);
        return true;
    }

    // ---------------------------------------------------------------- disconnect handling

    /// <summary>Marks every device that has been silent for longer than <paramref name="timeout"/> as disconnected.</summary>
    public int CheckForDisconnects(TimeSpan timeout)
    {
        lock (_sync)
        {
            int newlyDown = 0;
            var now = DateTime.UtcNow;

            foreach (var device in DeviceRegistry.Values)
            {
                if (device.LastSeenUtc is null) continue;                 // never reported: nothing to lose
                if (device.Status == DeviceStatus.Disconnected) continue; // already known
                if (now - device.LastSeenUtc.Value <= timeout) continue;

                if (MarkDisconnectedLocked(device, $"No telemetry for more than {timeout.TotalSeconds:F0} seconds.")) newlyDown++;
            }

            return newlyDown;
        }
    }

    /// <summary>Manual "report disconnect". Calling it twice shows the HashSet suppressing the duplicate.</summary>
    public (bool Found, DisconnectReportResult Result) ReportDisconnect(string mac)
    {
        lock (_sync)
        {
            if (!DeviceRegistry.TryGetValue(mac, out var device))
                return (false, new DisconnectReportResult { Added = false, Message = "Unknown device." });

            bool added = MarkDisconnectedLocked(device, "Reported as disconnected by an operator.");
            return (true, new DisconnectReportResult
            {
                Added = added,
                Message = added
                    ? $"{device.Registration.Name} marked as disconnected and an alert was raised."
                    : $"{device.Registration.Name} is already in the disconnected set — duplicate alert suppressed."
            });
        }
    }

    // Caller must hold _sync.
    private bool MarkDisconnectedLocked(DeviceRuntime device, string message)
    {
        bool added = DisconnectedNodes.Add(device.Registration.MacAddress);   // HashSet: false if already down
        device.Status = DeviceStatus.Disconnected;

        if (added) RaiseAlertLocked(device, AlertType.Disconnected, AlertSeverity.Warning, message);
        else _duplicatesSuppressed++;

        return added;
    }

    // ---------------------------------------------------------------- manual override commands (stack)

    public (CommandOutcome Outcome, string Message, DeviceCommand? Command) ExecuteCommand(CommandRequest request, string issuedBy)
    {
        lock (_sync)
        {
            DeviceCommand command;

            if (request.Action == CommandAction.ShutDownAll)
            {
                var previous = new Dictionary<string, bool>();
                foreach (var d in DeviceRegistry.Values)
                {
                    if (d.Registration.Category != SensorCategory.Actuator) continue;
                    previous[d.Registration.MacAddress] = d.IsOn;
                    d.IsOn = false;
                }

                if (previous.Count == 0)
                    return (CommandOutcome.NothingToDo, "There are no actuators registered.", null);

                command = new DeviceCommand
                {
                    Id = ++_commandSeq,
                    Action = CommandAction.ShutDownAll,
                    Mac = "*",
                    TargetName = "all actuators",
                    Description = $"Shut down all actuators ({previous.Count})",
                    PreviousStates = previous,
                    IssuedBy = issuedBy,
                    IssuedUtc = DateTime.UtcNow
                };
            }
            else
            {
                var mac = DeviceImportParser.NormalizeMac(request.Mac);
                if (!DeviceRegistry.TryGetValue(mac, out var device))
                    return (CommandOutcome.UnknownDevice, $"Unknown device {mac}.", null);

                if (device.Registration.Category != SensorCategory.Actuator)
                    return (CommandOutcome.NotAnActuator,
                        $"{device.Registration.Name} is a sensor, not an actuator — it cannot be switched.", null);

                bool newState = request.Action switch
                {
                    CommandAction.TurnOn => true,
                    CommandAction.TurnOff => false,
                    _ => !device.IsOn
                };

                var previous = new Dictionary<string, bool> { [mac] = device.IsOn };
                device.IsOn = newState;

                command = new DeviceCommand
                {
                    Id = ++_commandSeq,
                    Action = request.Action,
                    Mac = mac,
                    TargetName = device.Registration.Name,
                    Description = $"{PredictiveEngine.Describe(request.Action)} {device.Registration.Name} (now {(newState ? "ON" : "OFF")})",
                    PreviousStates = previous,
                    IssuedBy = issuedBy,
                    IssuedUtc = DateTime.UtcNow
                };
            }

            CommandUndoStack.Push(command);
            return (CommandOutcome.Executed, $"Command executed: {command.Description}", command);
        }
    }

    /// <summary>Pops the most recent command and restores the saved states. An empty stack is a normal, safe case.</summary>
    public UndoResult UndoLastCommand()
    {
        lock (_sync)
        {
            if (!CommandUndoStack.TryPop(out var command))
                return new UndoResult { Undone = false, Message = "Nothing to undo — the command history is empty." };

            foreach (var (mac, wasOn) in command.PreviousStates)
            {
                if (DeviceRegistry.TryGetValue(mac, out var device)) device.IsOn = wasOn;
            }

            return new UndoResult { Undone = true, Message = $"Undone: {command.Description}", Command = command };
        }
    }

    public List<DeviceCommand> GetCommandHistory(int take)
    {
        lock (_sync) return CommandUndoStack.Take(take).ToList();   // a Stack enumerates top-first
    }

    // ---------------------------------------------------------------- read models

    public List<AlertRecord> GetRecentAlerts(int take)
    {
        lock (_sync) return _recentAlerts.Latest(take);
    }

    public List<string> GetDisconnectedNodes()
    {
        lock (_sync) return DisconnectedNodes.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public List<SensorReading> GetTimeline(string mac, int take)
    {
        lock (_sync)
        {
            return SensorTimeline.TryGetValue(mac, out var timeline)
                ? timeline.Values.TakeLast(take).ToList()
                : new List<SensorReading>();
        }
    }

    /// <summary>One consistent snapshot of everything the dashboard shows (Insights are added by the controller).</summary>
    public StreamSnapshot BuildSnapshot()
    {
        lock (_sync)
        {
            var snapshot = new StreamSnapshot { ServerUtc = DateTime.UtcNow };

            foreach (var d in DeviceRegistry.Values.OrderBy(x => x.Registration.Name, StringComparer.OrdinalIgnoreCase))
            {
                var mac = d.Registration.MacAddress;
                snapshot.Devices.Add(ToView(d));
                snapshot.Series[mac] = SensorTimeline[mac].Values.TakeLast(40).Select(r => r.Value).ToList();
            }

            snapshot.Alerts = _recentAlerts.Latest(30);
            snapshot.DisconnectedNodes = DisconnectedNodes.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
            snapshot.ProcessingLog = _processingLog.Latest(20);
            snapshot.CommandHistory = CommandUndoStack.Take(10).ToList();
            snapshot.Queues = new QueueStats
            {
                TelemetryQueueDepth = TelemetryQueue.Count,
                CriticalQueueDepth = CriticalAlertQueue.Count,
                TotalProcessed = _totalProcessed,
                CriticalProcessed = _criticalProcessed,
                DuplicatesSuppressed = _duplicatesSuppressed
            };

            // Total load = meter1 + meter2 + ... using the overloaded '+' operator of SensorReading (Part 1).
            var meters = DeviceRegistry.Values
                .Where(d => d.Registration.Category == SensorCategory.PowerConsumption
                            && d.LastReading is not null
                            && d.Status != DeviceStatus.Disconnected
                            && d.Status != DeviceStatus.AwaitingData)
                .Select(d => d.LastReading!)
                .ToList();
            if (meters.Count > 0) snapshot.TotalPowerLoadWatts = meters.Aggregate((a, b) => a + b).Value;

            return snapshot;
        }
    }

    /// <summary>A copy of the state the predictive engine needs (it never touches the store directly).</summary>
    public EngineInput BuildEngineInput()
    {
        lock (_sync)
        {
            var input = new EngineInput { ActiveAlertKeys = new HashSet<string>(ActiveAlertKeys, StringComparer.OrdinalIgnoreCase) };

            foreach (var d in DeviceRegistry.Values)
            {
                var reg = d.Registration;
                input.Devices.Add(new EngineDevice
                {
                    Mac = reg.MacAddress,
                    Name = reg.Name,
                    Category = reg.Category,
                    Unit = reg.Unit,
                    Status = d.Status,
                    LastValue = d.LastReading is not null && d.LastSeenUtc.HasValue ? d.LastReading.Value : null,
                    LastSeenUtc = d.LastSeenUtc,
                    Recent = SensorTimeline[reg.MacAddress].Values.TakeLast(6).Select(r => r.Value).ToList()
                });
            }

            return input;
        }
    }
}
