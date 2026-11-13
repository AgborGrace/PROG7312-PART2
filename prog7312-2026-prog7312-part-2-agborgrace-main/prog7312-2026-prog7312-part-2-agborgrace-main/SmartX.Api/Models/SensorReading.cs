namespace SmartX.Api.Models;

/// <summary>
/// A single numeric sensor reading. PART 1 requirement (Operator overloading): readings from related
/// sensors can be aggregated or compared directly in code.
///
/// Part 2 uses these operators for real:
///   '-' : ThresholdPolicy uses the delta between consecutive readings to detect a severe power spike.
///   '+' : the dashboard's "Total load" KPI aggregates the smart meters with  meter1 + meter2 + ...
/// </summary>
public class SensorReading
{
    public string DeviceMac { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public SensorReading() { }

    public SensorReading(string deviceMac, double value, string unit)
    {
        DeviceMac = deviceMac;
        Value = value;
        Unit = unit;
        TimestampUtc = DateTime.UtcNow;
    }

    /// <summary>Aggregates two readings, e.g. "Meter3 = Meter1 + Meter2".</summary>
    public static SensorReading operator +(SensorReading a, SensorReading b) =>
        new($"{a.DeviceMac}+{b.DeviceMac}", a.Value + b.Value, a.Unit);

    /// <summary>Delta between two readings (used for spike detection).</summary>
    public static double operator -(SensorReading a, SensorReading b) => a.Value - b.Value;

    // C# requires comparison operators to be overloaded in matching pairs.
    public static bool operator >(SensorReading a, SensorReading b) => a.Value > b.Value;
    public static bool operator <(SensorReading a, SensorReading b) => a.Value < b.Value;

    public override string ToString() => $"{DeviceMac}: {Value:F2}{Unit} @ {TimestampUtc:HH:mm:ss}";
}
