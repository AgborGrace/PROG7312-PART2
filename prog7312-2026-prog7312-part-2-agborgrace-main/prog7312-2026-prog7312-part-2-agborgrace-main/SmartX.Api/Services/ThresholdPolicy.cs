using SmartX.Api.Models;

namespace SmartX.Api.Services;

public enum ReadingClass
{
    Normal,
    OutOfRange,
    Critical
}

public record ReadingEvaluation(ReadingClass Class, string Reason);

/// <summary>
/// The business rules that decide whether a reading is normal, out of range (warning) or CRITICAL.
/// Critical readings are the ones that jump the queue (priority queue fast path).
/// </summary>
public static class ThresholdPolicy
{
    public const double MoistureLowWarning = 20;
    public const double MoistureLowCritical = 10;
    public const double MoistureHighWarning = 90;

    public const double TempHighWarning = 38;
    public const double TempHighCritical = 45;
    public const double TempLowWarning = 5;
    public const double TempLowCritical = 0;

    public const double PowerWarning = 2200;
    public const double PowerCritical = 3000;
    public const double PowerSpikeDelta = 1000;

    public static bool IsTemperatureUnit(string unit)
    {
        var u = (unit ?? string.Empty).Trim().ToLowerInvariant();
        return u.Contains('c');   // "°C", "C", "celsius"
    }

    public static ReadingEvaluation Evaluate(SensorRegistration device, SensorReading current, SensorReading? previous)
    {
        double v = current.Value;

        switch (device.Category)
        {
            case SensorCategory.PowerConsumption:
                if (v >= PowerCritical)
                    return new(ReadingClass.Critical, $"severe power spike: {v:F0} W (limit {PowerCritical:F0} W)");

                // Uses the overloaded '-' operator of SensorReading (Part 1) to get the delta.
                if (previous is not null && (current - previous) >= PowerSpikeDelta)
                    return new(ReadingClass.Critical, $"power jumped by {(current - previous):F0} W in one reading ({v:F0} W)");

                if (v >= PowerWarning)
                    return new(ReadingClass.OutOfRange, $"power draw high: {v:F0} W (warning above {PowerWarning:F0} W)");
                break;

            case SensorCategory.Environmental:
                if (IsTemperatureUnit(device.Unit))
                {
                    if (v >= TempHighCritical || v <= TempLowCritical)
                        return new(ReadingClass.Critical, $"critical temperature: {v:F1} °C");
                    if (v >= TempHighWarning)
                        return new(ReadingClass.OutOfRange, $"temperature high: {v:F1} °C (warning above {TempHighWarning:F0} °C)");
                    if (v <= TempLowWarning)
                        return new(ReadingClass.OutOfRange, $"temperature low: {v:F1} °C (warning below {TempLowWarning:F0} °C)");
                }
                else
                {
                    if (v < MoistureLowCritical)
                        return new(ReadingClass.Critical, $"critical moisture drop: {v:F1}% (below {MoistureLowCritical:F0}%)");
                    if (v < MoistureLowWarning)
                        return new(ReadingClass.OutOfRange, $"moisture below {MoistureLowWarning:F0}% (reading {v:F1}%)");
                    if (v > MoistureHighWarning)
                        return new(ReadingClass.OutOfRange, $"moisture above {MoistureHighWarning:F0}% (reading {v:F1}%)");
                }
                break;

            // Actuators report their state (0/1); there is no "out of range" for them.
            case SensorCategory.Actuator:
            default:
                break;
        }

        return new(ReadingClass.Normal, string.Empty);
    }
}
