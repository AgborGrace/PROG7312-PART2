namespace SmartX.Api.Models;

/// <summary>
/// Generic wrapper used to carry ANY telemetry value type (a float for soil moisture, an int for power
/// wattage, a bool for a valve state) through the pipeline uniformly, without one near-identical class per
/// type and without expensive boxing/unboxing of value types into <c>object</c>.
///
/// PART 1 requirement (Generics) — and in Part 2 it is the element type of the FIFO TelemetryQueue
/// (Queue&lt;QueuedTelemetry&gt; holds TelemetryPacket&lt;double&gt;).
/// </summary>
/// <typeparam name="T">The underlying telemetry value type, e.g. float, int, bool, double.</typeparam>
public class TelemetryPacket<T>
{
    public string DeviceMac { get; set; } = string.Empty;
    public T Value { get; set; } = default!;
    public string Unit { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public TelemetryPacket() { }

    public TelemetryPacket(string deviceMac, T value, string unit)
    {
        DeviceMac = deviceMac;
        Value = value;
        Unit = unit;
        TimestampUtc = DateTime.UtcNow;
    }

    public override string ToString() => $"[{TimestampUtc:HH:mm:ss}] {DeviceMac} => {Value}{Unit}";
}
