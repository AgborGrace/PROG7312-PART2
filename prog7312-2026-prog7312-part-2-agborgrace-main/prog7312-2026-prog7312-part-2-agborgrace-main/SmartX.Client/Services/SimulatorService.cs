using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartX.Client.Models;

namespace SmartX.Client.Services;

/// <summary>
/// Simulated ESP32 devices. IMPORTANT: it never touches the data structures directly — every reading is a real HTTP POST to
/// api/telemetry/ingest, exactly as a physical device would send it. It lives at application level (not inside a page),
/// so switching tabs never stops it.
///
/// Behaviour: moisture sensors dry out slowly and recover while a pump in the same zone is switched ON; power meters
/// fluctuate around a base load; actuators report their commanded state; temperature drifts gently.
/// </summary>
public class SimulatorService
{
    private readonly ApiClient _api;
    private readonly Random _rng = new();
    private readonly object _lock = new();

    private readonly Dictionary<string, double> _state = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _silenced = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private long _packetsSent;
    private long _errors;

    public SimulatorService(ApiClient api) => _api = api;

    public bool IsRunning => _cts is not null;
    public long PacketsSent => Interlocked.Read(ref _packetsSent);
    public long Errors => Interlocked.Read(ref _errors);
    public string LastError { get; private set; } = string.Empty;

    public event EventHandler? StateChanged;

    // ------------------------------------------------------------------ start / stop

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _cts = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                await TickAsync();
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // stopped by the user
        }
    }

    private async Task TickAsync()
    {
        var response = await _api.GetDevicesAsync();
        if (!response.IsSuccess || response.Data is null)
        {
            RegisterError(response.Error);
            return;
        }

        var devices = response.Data;
        var sends = new List<Task>();

        foreach (var device in devices)
        {
            if (IsSilenced(device.Mac)) continue;   // simulating a dropped-out sensor
            sends.Add(SendReadingAsync(device, NextValue(device, devices)));
        }

        await Task.WhenAll(sends);
    }

    private async Task SendReadingAsync(DeviceDto device, double value)
    {
        var result = await _api.IngestAsync(device.Mac, value, device.Unit);
        if (result.IsSuccess) Interlocked.Increment(ref _packetsSent);
        else RegisterError(result.Error);
    }

    private void RegisterError(string message)
    {
        Interlocked.Increment(ref _errors);
        LastError = message;
    }

    // ------------------------------------------------------------------ value generation

    private static bool IsTemperature(string unit) => unit.Trim().ToLowerInvariant().Contains('c');

    private double NextValue(DeviceDto device, IReadOnlyList<DeviceDto> all)
    {
        lock (_lock)
        {
            switch (device.Category)
            {
                case SensorCategory.Actuator:
                    return device.IsOn ? 1 : 0;

                case SensorCategory.PowerConsumption:
                {
                    double current = _state.TryGetValue(device.Mac, out var p) ? p : 520;
                    current = Math.Clamp(current + (_rng.NextDouble() - 0.5) * 70, 320, 900);
                    _state[device.Mac] = current;
                    return Math.Round(current, 0);
                }

                default:
                    if (IsTemperature(device.Unit))
                    {
                        double temp = _state.TryGetValue(device.Mac, out var t) ? t : 24;
                        temp = Math.Clamp(temp + (_rng.NextDouble() - 0.5) * 0.8, 17, 33);
                        _state[device.Mac] = temp;
                        return Math.Round(temp, 1);
                    }
                    else
                    {
                        // A pump that is ON in the same zone waters the bed (moisture rises); otherwise it slowly dries out.
                        bool pumpOn = all.Any(a => a.Category == SensorCategory.Actuator && a.IsOn &&
                                                   a.Zone == device.Zone &&
                                                   a.Name.Contains("pump", StringComparison.OrdinalIgnoreCase));
                        double moisture = _state.TryGetValue(device.Mac, out var m) ? m : 55;
                        moisture += pumpOn ? 1.8 : -0.35;
                        moisture += (_rng.NextDouble() - 0.5) * 0.6;
                        moisture = Math.Clamp(moisture, 0, 100);
                        _state[device.Mac] = moisture;
                        return Math.Round(moisture, 1);
                    }
            }
        }
    }

    // ------------------------------------------------------------------ test-lab controls

    public bool IsSilenced(string mac)
    {
        lock (_lock) return _silenced.Contains(mac);
    }

    /// <summary>Stops/starts one device sending telemetry (simulates a sensor dropping out). Returns true if now silenced.</summary>
    public bool ToggleSilence(string mac)
    {
        lock (_lock)
        {
            if (!_silenced.Add(mac)) { _silenced.Remove(mac); return false; }
            return true;
        }
    }

    private void SetSilenced(string mac, bool silenced)
    {
        lock (_lock)
        {
            if (silenced) _silenced.Add(mac); else _silenced.Remove(mac);
        }
    }

    private void SetState(string mac, double value)
    {
        lock (_lock) _state[mac] = value;
    }

    private async Task<(List<DeviceDto>? Devices, string Error)> LoadDevicesAsync()
    {
        var response = await _api.GetDevicesAsync();
        return response.IsSuccess && response.Data is not null
            ? (response.Data, string.Empty)
            : (null, response.Error);
    }

    private static DeviceDto? PickMoistureSensor(IEnumerable<DeviceDto> devices) =>
        devices.FirstOrDefault(d => d.Category == SensorCategory.Environmental && !IsTemperature(d.Unit) &&
                                    d.Name.Contains("Sensor B", StringComparison.OrdinalIgnoreCase))
        ?? devices.FirstOrDefault(d => d.Category == SensorCategory.Environmental && !IsTemperature(d.Unit));

    private static DeviceDto? PickPump(IEnumerable<DeviceDto> devices) =>
        devices.FirstOrDefault(d => d.Category == SensorCategory.Actuator && d.Name.Contains("Pump A", StringComparison.OrdinalIgnoreCase))
        ?? devices.FirstOrDefault(d => d.Category == SensorCategory.Actuator && d.Name.Contains("pump", StringComparison.OrdinalIgnoreCase))
        ?? devices.FirstOrDefault(d => d.Category == SensorCategory.Actuator);

    /// <summary>
    /// QUEUE TEST: fires a burst of normal packets (they pile up in the FIFO queue) and then ONE critical packet.
    /// The critical packet is processed immediately, ahead of the queued ones — visible in the Processing Log.
    /// </summary>
    public async Task<string> SendBurstAsync(int normalCount = 20)
    {
        var (devices, error) = await LoadDevicesAsync();
        if (devices is null) return "Could not load devices: " + error;

        var sensor = PickMoistureSensor(devices);
        if (sensor is null) return "No moisture sensor found. Import the sample device list first.";

        SetSilenced(sensor.Mac, true);   // keep the normal simulator from interfering with the test
        try
        {
            var normal = Enumerable.Range(0, normalCount)
                .Select(i => _api.IngestAsync(sensor.Mac, 45 + (i % 10), sensor.Unit))
                .ToList();
            await Task.WhenAll(normal);
            Interlocked.Add(ref _packetsSent, normal.Count(t => t.Result.IsSuccess));

            var critical = await _api.IngestAsync(sensor.Mac, 4, sensor.Unit);
            if (critical.IsSuccess) Interlocked.Increment(ref _packetsSent);

            return critical.IsSuccess && critical.Data is not null
                ? $"Sent {normalCount} normal packets + 1 critical for {sensor.Name}. {critical.Data.Message}"
                : "Critical packet failed: " + critical.Error;
        }
        finally
        {
            SetSilenced(sensor.Mac, false);
            SetState(sensor.Mac, 50);
        }
    }

    /// <summary>Sends one severe power spike (3400 W) from the first smart meter.</summary>
    public async Task<string> InjectPowerSpikeAsync()
    {
        var (devices, error) = await LoadDevicesAsync();
        if (devices is null) return "Could not load devices: " + error;

        var meter = devices.FirstOrDefault(d => d.Category == SensorCategory.PowerConsumption);
        if (meter is null) return "No smart meter found. Import the sample device list first.";

        var result = await _api.IngestAsync(meter.Mac, 3400, meter.Unit);
        if (result.IsSuccess) Interlocked.Increment(ref _packetsSent);
        return result.IsSuccess && result.Data is not null ? $"{meter.Name}: {result.Data.Message}" : "Failed: " + result.Error;
    }

    /// <summary>
    /// PREDICTION TEST: performs a realistic operator routine THREE times through the real API —
    /// moisture drops below 20%, the operator searches "Pump A" and turns it ON, moisture recovers —
    /// then drops it a fourth time. The engine has now recorded the pattern and suggests the action BEFORE the operator searches.
    /// </summary>
    public async Task<bool> RunLearningScenarioAsync(Action<string> report)
    {
        var (devices, error) = await LoadDevicesAsync();
        if (devices is null) { report("Could not load devices: " + error); return false; }

        var sensor = PickMoistureSensor(devices);
        var pump = PickPump(devices);
        if (sensor is null || pump is null)
        {
            report("Import the sample device list first (need a moisture sensor and a pump).");
            return false;
        }

        report($"Training the engine with {sensor.Name} -> {pump.Name} ...");
        SetSilenced(sensor.Mac, true);

        try
        {
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                report($"Cycle {cycle}/3: {sensor.Name} drops to 14% (below the 20% threshold)");
                await _api.IngestAsync(sensor.Mac, 14, sensor.Unit);
                await Task.Delay(1500);   // the background processor raises the alert

                report($"Cycle {cycle}/3: operator searches \"{pump.Name}\" and turns it ON");
                await _api.RecordSearchAsync(pump.Name);
                await _api.SendCommandAsync(pump.Mac, CommandAction.TurnOn);
                await Task.Delay(300);

                report($"Cycle {cycle}/3: moisture recovers to 55%");
                await _api.IngestAsync(sensor.Mac, 55, sensor.Unit);
                await Task.Delay(1500);   // alert clears
                await _api.UndoAsync();   // pump back OFF (undo is not recorded as a learned action)
            }

            report($"Final drop: {sensor.Name} falls to 14% again — watch Suggested Actions ...");
            await _api.IngestAsync(sensor.Mac, 14, sensor.Unit);
            await Task.Delay(1500);
            report("Done. The engine now suggests the action it learned, before you search for it.");
            return true;
        }
        finally
        {
            SetState(sensor.Mac, 14);
            SetSilenced(sensor.Mac, false);
        }
    }
}
