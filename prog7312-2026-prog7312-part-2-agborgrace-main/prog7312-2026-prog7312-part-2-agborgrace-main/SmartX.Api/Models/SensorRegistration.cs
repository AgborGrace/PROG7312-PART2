namespace SmartX.Api.Models;

/// <summary>The three sensor categories the assignment brief specifies. (Keep the order — the client mirrors it.)</summary>
public enum SensorCategory
{
    Environmental,
    PowerConsumption,
    Actuator
}

/// <summary>A device registration record (typed into the form, or parsed from an imported .txt file).</summary>
public class SensorRegistration
{
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>Friendly name, e.g. "Pump A" or "Moisture Sensor B" (the predictive engine learns from these).</summary>
    public string Name { get; set; } = string.Empty;

    public string Facility { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string SubZone { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }

    /// <summary>Unit the device reports in: "%" (moisture), "°C" (temperature), "W" (power), "" (actuator).</summary>
    public string Unit { get; set; } = string.Empty;

    public string? AttachedFileName { get; set; }
    public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;
}
