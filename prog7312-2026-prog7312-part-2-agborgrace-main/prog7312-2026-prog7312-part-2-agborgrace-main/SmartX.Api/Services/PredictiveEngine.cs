using SmartX.Api.Collections;
using SmartX.Api.Models;

namespace SmartX.Api.Services;

/// <summary>A device as the engine sees it (copied out of the data store under its lock).</summary>
public class EngineDevice
{
    public string Mac { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DeviceStatus Status { get; set; }
    public double? LastValue { get; set; }
    public DateTime? LastSeenUtc { get; set; }

    /// <summary>The last few readings in timestamp order (taken from the SensorTimeline SortedDictionary).</summary>
    public List<double> Recent { get; set; } = new();
}

public class EngineInput
{
    /// <summary>Alert keys ("MAC:Type") that are active RIGHT NOW (the ActiveAlertKeys HashSet).</summary>
    public HashSet<string> ActiveAlertKeys { get; set; } = new();
    public List<EngineDevice> Devices { get; set; } = new();
}

public class UserActionEvent
{
    public DateTime Utc { get; set; }
    public string Kind { get; set; } = string.Empty;   // "Search" or "Command"
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// PREDICTIVE ACTION ENGINE (Automated Action Engine).
///
/// 1. RECORDS history — every search the operator submits and every manual command they issue
///    (_history, a BoundedLog), plus every alert the system raises ("trigger events").
/// 2. ANALYSES patterns — each time the operator acts, the action is credited to every alert that fired within
///    the last PatternWindow (e.g. "searched Pump A" within 3 minutes of "Moisture Sensor B below 20%").
///    This builds an association table:  trigger  =>  action  (support count + confidence).
/// 3. SUGGESTS — when a trigger is active again and a learned pattern has enough support (MinSupport) and
///    confidence (MinConfidence), the action is proposed in "Suggested Actions" BEFORE the operator searches.
///    Suggestions are never random: each one cites the recorded evidence ("3 of 4 times").
/// 4. HIGHLIGHTS — independent of history, it scans live data for faulty devices, out-of-range readings,
///    dropped sensors and trends (using the SortedDictionary timeline) for the "Automated Insights" panel.
/// </summary>
public class PredictiveEngine
{
    private readonly object _sync = new();

    private const int MinSupport = 2;            // pattern must have happened at least twice
    private const double MinConfidence = 0.5;    // ...and followed at least half of the trigger occurrences
    private static readonly TimeSpan PatternWindow = TimeSpan.FromSeconds(180);

    // ---- recorded history ----
    private readonly BoundedLog<UserActionEvent> _history = new(500);
    private readonly List<TriggerEvent> _recentTriggers = new();
    private readonly Dictionary<string, int> _triggerCounts = new();
    private readonly Dictionary<string, string> _triggerText = new();
    private readonly Dictionary<string, int> _searchCounts = new(StringComparer.OrdinalIgnoreCase);

    // trigger key -> (action key -> statistics). Indexed by trigger so look-ups are O(1) per active alert.
    private readonly Dictionary<string, Dictionary<string, PatternStat>> _patternsByTrigger = new();
    private readonly Dictionary<string, ActionInfo> _actionInfo = new();

    private sealed class TriggerEvent
    {
        public string Key = string.Empty;
        public DateTime Utc;
        public HashSet<string> CreditedActions = new();
    }

    private sealed class PatternStat
    {
        public int Count;
        public DateTime LastUtc;
    }

    private sealed class ActionInfo
    {
        public string Kind = string.Empty;
        public string Label = string.Empty;
        public string? SearchTerm;
        public string? Mac;
        public CommandAction? Command;
    }

    // ------------------------------------------------------------------ recording

    /// <summary>Called by the data store each time a (non-duplicate) alert is raised.</summary>
    public void RecordTrigger(string key, string deviceName, string message, DateTime utc)
    {
        lock (_sync)
        {
            _triggerCounts[key] = _triggerCounts.GetValueOrDefault(key) + 1;
            _triggerText[key] = $"{deviceName}: {message}";
            _recentTriggers.Add(new TriggerEvent { Key = key, Utc = utc });
            if (_recentTriggers.Count > 200) _recentTriggers.RemoveAt(0);
        }
    }

    /// <summary>Called when the operator submits a search (Enter / Search button).</summary>
    public void RecordSearch(string term)
    {
        term = (term ?? string.Empty).Trim();
        if (term.Length == 0) return;

        lock (_sync)
        {
            _searchCounts[term] = _searchCounts.GetValueOrDefault(term) + 1;
            Credit($"search:{term.ToLowerInvariant()}", new ActionInfo
            {
                Kind = "Search",
                Label = $"Search for \"{term}\"",
                SearchTerm = term
            });
        }
    }

    /// <summary>Called after a manual override command was executed successfully.</summary>
    public void RecordCommand(CommandAction action, string mac, string targetName)
    {
        lock (_sync)
        {
            Credit($"command:{action}:{mac}", new ActionInfo
            {
                Kind = "Command",
                Label = $"{Describe(action)} {targetName}".Trim(),
                Mac = mac,
                Command = action
            });
        }
    }

    // Caller must hold _sync.
    private void Credit(string actionKey, ActionInfo info)
    {
        var now = DateTime.UtcNow;
        _actionInfo[actionKey] = info;
        _history.Add(new UserActionEvent { Utc = now, Kind = info.Kind, Key = actionKey, Label = info.Label });

        foreach (var trigger in _recentTriggers)
        {
            if (now - trigger.Utc > PatternWindow) continue;
            if (!trigger.CreditedActions.Add(actionKey)) continue;   // credit each action once per alert episode

            if (!_patternsByTrigger.TryGetValue(trigger.Key, out var byAction))
            {
                byAction = new Dictionary<string, PatternStat>();
                _patternsByTrigger[trigger.Key] = byAction;
            }

            if (!byAction.TryGetValue(actionKey, out var stat))
            {
                stat = new PatternStat();
                byAction[actionKey] = stat;
            }

            stat.Count++;
            stat.LastUtc = now;
        }
    }

    // ------------------------------------------------------------------ generating insights

    public List<Insight> GenerateInsights(EngineInput input)
    {
        var result = new List<Insight>();

        lock (_sync)
        {
            // ---- 1. Pattern-based SUGGESTED ACTIONS (learned from recorded history) ----
            foreach (var triggerKey in input.ActiveAlertKeys)
            {
                if (!_triggerCounts.TryGetValue(triggerKey, out var occurrences) || occurrences == 0) continue;
                if (!_patternsByTrigger.TryGetValue(triggerKey, out var byAction)) continue;

                var latestEpisode = _recentTriggers.LastOrDefault(t => t.Key == triggerKey);

                foreach (var (actionKey, stat) in byAction)
                {
                    double confidence = Math.Min(1.0, (double)stat.Count / occurrences);
                    if (stat.Count < MinSupport || confidence < MinConfidence) continue;

                    // Already done during this alert episode? Then do not nag.
                    if (latestEpisode is not null && latestEpisode.CreditedActions.Contains(actionKey)) continue;

                    var info = _actionInfo[actionKey];
                    result.Add(new Insight
                    {
                        Id = $"pattern:{triggerKey}=>{actionKey}",
                        Kind = "SuggestedAction",
                        Severity = "Warning",
                        Title = $"Suggested: {info.Label}",
                        Detail = $"{_triggerText.GetValueOrDefault(triggerKey, triggerKey)}. " +
                                 $"After this happened, you did this {stat.Count} of {occurrences} times " +
                                 $"(within {PatternWindow.TotalSeconds:F0}s). Confidence {confidence:P0}.",
                        Confidence = confidence,
                        SearchTerm = info.SearchTerm,
                        CommandMac = info.Mac,
                        Command = info.Command
                    });
                }
            }

            // ---- 2. Proactive AUTOMATED INSIGHTS from live data (no history needed) ----
            foreach (var d in input.Devices)
            {
                string value = d.LastValue.HasValue ? $"{d.LastValue.Value:F1} {d.Unit}".Trim() : "no data";

                switch (d.Status)
                {
                    case DeviceStatus.Critical:
                        result.Add(new Insight
                        {
                            Id = $"critical:{d.Mac}",
                            Severity = "Critical",
                            Title = $"{d.Name} is in a CRITICAL state",
                            Detail = $"Latest reading {value}. Immediate attention recommended."
                        });
                        break;

                    case DeviceStatus.Warning:
                        result.Add(new Insight
                        {
                            Id = $"warning:{d.Mac}",
                            Severity = "Warning",
                            Title = $"{d.Name} is reading out of range",
                            Detail = $"Latest reading {value}. Check the device before it becomes critical."
                        });
                        break;

                    case DeviceStatus.Disconnected:
                        result.Add(new Insight
                        {
                            Id = $"disconnected:{d.Mac}",
                            Severity = "Warning",
                            Title = $"{d.Name} has stopped reporting",
                            Detail = d.LastSeenUtc.HasValue
                                ? $"No telemetry since {d.LastSeenUtc.Value:HH:mm:ss} UTC — check power, signal or batteries."
                                : "No telemetry received — check power, signal or batteries."
                        });
                        break;
                }

                // Trend detection over the last 4 readings (from the SortedDictionary timeline).
                if ((d.Status == DeviceStatus.Normal || d.Status == DeviceStatus.Warning) && d.Recent.Count >= 4)
                {
                    var r = d.Recent.TakeLast(4).ToList();
                    bool falling = r[0] > r[1] && r[1] > r[2] && r[2] > r[3];
                    bool rising = r[0] < r[1] && r[1] < r[2] && r[2] < r[3];

                    if (d.Category == SensorCategory.Environmental && !ThresholdPolicy.IsTemperatureUnit(d.Unit)
                        && falling && r[3] < 35)
                    {
                        result.Add(new Insight
                        {
                            Id = $"trend-down:{d.Mac}",
                            Severity = "Info",
                            Title = $"{d.Name}: moisture trending down",
                            Detail = $"Fell from {r[0]:F1}% to {r[3]:F1}% over the last 4 readings — likely to cross " +
                                     $"{ThresholdPolicy.MoistureLowWarning:F0}% soon."
                        });
                    }
                    else if (d.Category == SensorCategory.PowerConsumption && rising && r[3] > 1500)
                    {
                        result.Add(new Insight
                        {
                            Id = $"trend-up:{d.Mac}",
                            Severity = "Info",
                            Title = $"{d.Name}: power draw rising",
                            Detail = $"Rose from {r[0]:F0} W to {r[3]:F0} W over the last 4 readings."
                        });
                    }
                }
            }

            // ---- 3. Frequently searched devices (from the recorded search history) ----
            var top = _searchCounts.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (top.Key is not null && top.Value >= 3)
            {
                result.Add(new Insight
                {
                    Id = $"frequent-search:{top.Key.ToLowerInvariant()}",
                    Severity = "Info",
                    Title = $"You often search \"{top.Key}\"",
                    Detail = $"Searched {top.Value} times so far — use the shortcut below.",
                    SearchTerm = top.Key
                });
            }
        }

        return result
            .OrderBy(i => SeverityRank(i.Severity))
            .ThenByDescending(i => i.Confidence ?? 0)
            .ToList();
    }

    /// <summary>The recorded user-action history, newest first (used by tests/diagnostics).</summary>
    public List<UserActionEvent> GetHistory(int take)
    {
        lock (_sync) return _history.Latest(take);
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 0,
        "Warning" => 1,
        _ => 2
    };

    public static string Describe(CommandAction action) => action switch
    {
        CommandAction.TurnOn => "Turn ON",
        CommandAction.TurnOff => "Turn OFF",
        CommandAction.Toggle => "Toggle",
        CommandAction.ShutDownAll => "Shut down all actuators",
        _ => action.ToString()
    };
}
