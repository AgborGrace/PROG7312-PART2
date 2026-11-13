namespace SmartX.Api.Models;

public enum AlertType
{
    CriticalReading,
    OutOfRange,
    Disconnected
}

public enum AlertSeverity
{
    Warning,
    Critical
}

/// <summary>One alert shown on the dashboard. Duplicates are prevented by the ActiveAlertKeys HashSet.</summary>
public class AlertRecord
{
    public long Id { get; set; }
    public string Mac { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime RaisedUtc { get; set; }
}
