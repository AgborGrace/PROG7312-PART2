namespace SmartX.Api.Models;

public enum DeviceStatus
{
    AwaitingData,
    Normal,
    Warning,
    Critical,
    Disconnected
}

/// <summary>
/// The live, mutable state of one registered device. These objects are the VALUES of the
/// DeviceRegistry dictionary, so a telemetry packet finds its device in O(1) by MAC address.
/// </summary>
public class DeviceRuntime
{
    public DeviceRuntime(SensorRegistration registration) => Registration = registration;

    public SensorRegistration Registration { get; }
    public DeviceStatus Status { get; set; } = DeviceStatus.AwaitingData;

    /// <summary>Most recent PROCESSED reading (the baseline used for spike detection).</summary>
    public SensorReading? LastReading { get; set; }

    public DateTime? LastSeenUtc { get; set; }

    /// <summary>Commanded state of an actuator (pump/relay/valve). Only manual override commands change it.</summary>
    public bool IsOn { get; set; }
}
