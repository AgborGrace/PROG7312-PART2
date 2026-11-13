namespace SmartX.Api.Models;

/// <summary>What the API returns for a device (registration + live state, flattened for the client).</summary>
public class DeviceView
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
}

/// <summary>Body of POST api/telemetry/ingest — what a device (or the simulator) sends.</summary>
public class IngestRequest
{
    public string DeviceMac { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public enum IngestOutcome
{
    /// <summary>Normal packet: placed in the FIFO TelemetryQueue, processed shortly.</summary>
    Queued,

    /// <summary>Critical packet: went through the priority queue and was processed immediately.</summary>
    CriticalProcessed,

    UnknownDevice,
    QueueFull
}

public class IngestResult
{
    public IngestOutcome Outcome { get; set; }
    public bool Critical => Outcome == IngestOutcome.CriticalProcessed;
    public int QueueDepth { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class QueueStats
{
    public int TelemetryQueueDepth { get; set; }
    public int CriticalQueueDepth { get; set; }
    public long TotalProcessed { get; set; }
    public long CriticalProcessed { get; set; }
    public long DuplicatesSuppressed { get; set; }
}

/// <summary>One entry of the processing log — proves the order in which packets were processed.</summary>
public class ProcessedEvent
{
    public long Seq { get; set; }
    public string Kind { get; set; } = "Normal";   // "Normal" or "Critical"
    public string Mac { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime ProcessedUtc { get; set; }
    public double WaitMs { get; set; }
}

/// <summary>An entry of the "Suggested Actions" / "Automated Insights" panels.</summary>
public class Insight
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "AutomatedInsight";   // "SuggestedAction" or "AutomatedInsight"
    public string Severity { get; set; } = "Info";           // "Info" | "Warning" | "Critical"
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public string? SearchTerm { get; set; }
    public string? CommandMac { get; set; }
    public CommandAction? Command { get; set; }
}

/// <summary>Everything the Command Stream dashboard needs, returned by ONE call per refresh.</summary>
public class StreamSnapshot
{
    public DateTime ServerUtc { get; set; }
    public List<DeviceView> Devices { get; set; } = new();
    public Dictionary<string, List<double>> Series { get; set; } = new();
    public List<AlertRecord> Alerts { get; set; } = new();
    public List<string> DisconnectedNodes { get; set; } = new();
    public QueueStats Queues { get; set; } = new();
    public List<ProcessedEvent> ProcessingLog { get; set; } = new();
    public List<DeviceCommand> CommandHistory { get; set; } = new();
    public List<Insight> Insights { get; set; } = new();
    public double TotalPowerLoadWatts { get; set; }
}

public class ImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<string> Messages { get; set; } = new();
    public List<DeviceView> Devices { get; set; } = new();
}

public class UndoResult
{
    public bool Undone { get; set; }
    public string Message { get; set; } = string.Empty;
    public DeviceCommand? Command { get; set; }
}

public class DisconnectReportResult
{
    public bool Added { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SearchEventRequest
{
    public string Term { get; set; } = string.Empty;
}
