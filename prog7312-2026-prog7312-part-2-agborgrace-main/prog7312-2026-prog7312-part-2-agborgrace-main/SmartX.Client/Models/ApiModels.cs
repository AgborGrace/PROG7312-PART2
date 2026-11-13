using System;
using System.Collections.Generic;

namespace SmartX.Client.Models;

// ---- Enums mirror the API's enums. The API sends/receives them as readable strings. ----
public enum SensorCategory { Environmental, PowerConsumption, Actuator }
public enum DeviceStatus { AwaitingData, Normal, Warning, Critical, Disconnected }
public enum CommandAction { TurnOn, TurnOff, Toggle, ShutDownAll }
public enum IngestOutcome { Queued, CriticalProcessed, UnknownDevice, QueueFull }
public enum AlertType { CriticalReading, OutOfRange, Disconnected }
public enum AlertSeverity { Warning, Critical }

/// <summary>A device as returned by the API (registration + live state). Bound directly to the devices grid.</summary>
public class DeviceDto
{
    public string Mac { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Facility { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string SubZone { get; set; } = string.Empty;
    public DeviceStatus Status { get; set; }
    public double? LastValue { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public bool IsOn { get; set; }
    public string? AttachedFileName { get; set; }
    public DateTime RegisteredUtc { get; set; }

    // ---- display helpers used by the grid and the cards ----
    public string Location => $"{Facility} > {Zone} > {SubZone}";

    public string CategoryText => Category switch
    {
        SensorCategory.Environmental => "Environmental",
        SensorCategory.PowerConsumption => "Power",
        _ => "Actuator"
    };

    public string StatusText => Status switch
    {
        DeviceStatus.AwaitingData => "Awaiting data",
        DeviceStatus.Normal => "Normal",
        DeviceStatus.Warning => "Warning",
        DeviceStatus.Critical => "Critical",
        _ => "Disconnected"
    };

    public string LastValueText
    {
        get
        {
            if (Category == SensorCategory.Actuator) return IsOn ? "ON" : "OFF";
            return LastValue.HasValue ? $"{LastValue.Value:0.#} {Unit}".Trim() : "-";
        }
    }

    public string LastSeenText => LastSeenUtc.HasValue ? LastSeenUtc.Value.ToLocalTime().ToString("HH:mm:ss") : "never";
    public string AttachmentText => string.IsNullOrEmpty(AttachedFileName) ? "-" : AttachedFileName!;
}

/// <summary>Body of POST api/sensors/register.</summary>
public class RegistrationDto
{
    public string MacAddress { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Facility { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string SubZone { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public class IngestRequestDto
{
    public string DeviceMac { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public class IngestResultDto
{
    public IngestOutcome Outcome { get; set; }
    public bool Critical { get; set; }
    public int QueueDepth { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class QueueStatsDto
{
    public int TelemetryQueueDepth { get; set; }
    public int CriticalQueueDepth { get; set; }
    public long TotalProcessed { get; set; }
    public long CriticalProcessed { get; set; }
    public long DuplicatesSuppressed { get; set; }
}

public class ProcessedEventDto
{
    public long Seq { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Mac { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime ProcessedUtc { get; set; }
    public double WaitMs { get; set; }
}

public class AlertDto
{
    public long Id { get; set; }
    public string Mac { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime RaisedUtc { get; set; }
}

public class DeviceCommandDto
{
    public long Id { get; set; }
    public CommandAction Action { get; set; }
    public string Mac { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IssuedBy { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; }
}

public class InsightDto
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public string? SearchTerm { get; set; }
    public string? CommandMac { get; set; }
    public CommandAction? Command { get; set; }
}

public class StreamSnapshotDto
{
    public DateTime ServerUtc { get; set; }
    public List<DeviceDto> Devices { get; set; } = new();
    public Dictionary<string, List<double>> Series { get; set; } = new();
    public List<AlertDto> Alerts { get; set; } = new();
    public List<string> DisconnectedNodes { get; set; } = new();
    public QueueStatsDto Queues { get; set; } = new();
    public List<ProcessedEventDto> ProcessingLog { get; set; } = new();
    public List<DeviceCommandDto> CommandHistory { get; set; } = new();
    public List<InsightDto> Insights { get; set; } = new();
    public double TotalPowerLoadWatts { get; set; }
}

public class ImportResultDto
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<string> Messages { get; set; } = new();
    public List<DeviceDto> Devices { get; set; } = new();
}

public class UndoResultDto
{
    public bool Undone { get; set; }
    public string Message { get; set; } = string.Empty;
    public DeviceCommandDto? Command { get; set; }
}

public class DisconnectReportDto
{
    public bool Added { get; set; }
    public string Message { get; set; } = string.Empty;
}
