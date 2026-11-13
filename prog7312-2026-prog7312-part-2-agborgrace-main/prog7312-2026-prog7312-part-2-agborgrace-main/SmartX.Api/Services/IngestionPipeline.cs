using SmartX.Api.Models;

namespace SmartX.Api.Services;

public enum OnboardStatus
{
    Created,
    Invalid,
    Duplicate
}

/// <summary>
/// Device onboarding pipeline (carried over from Part 1 and now wired into the Part 2 data store).
///
/// Part 1 requirements that are still demonstrated here:
///   - Collections / Lists   : _registrationLog is a List&lt;SensorRegistration&gt; audit trail of every onboarding.
///   - Jagged arrays         : _historicalBatches is a float[][] — one row of raw historical samples per device
///                             (rows have different lengths). Each row is converted into List&lt;SensorReading&gt;
///                             objects and loaded into the SensorTimeline (SortedDictionary) in the data store.
///   - Recursion             : the Facility -> Zone -> SubZone hierarchy is validated with RecursiveConfigValidator.
///
/// Part 2 note: look-ups never loop over these lists — device look-ups go through the DeviceRegistry dictionary.
/// </summary>
public class IngestionPipeline
{
    private readonly SmartXDataStore _store;
    private readonly object _sync = new();
    private readonly Random _rng = new();

    private readonly List<SensorRegistration> _registrationLog = new();
    private float[][] _historicalBatches = Array.Empty<float[]>();

    public IngestionPipeline(SmartXDataStore store) => _store = store;

    public IReadOnlyList<SensorRegistration> RegistrationLog
    {
        get { lock (_sync) return _registrationLog.ToList(); }
    }

    /// <summary>The raw jagged array (one row per onboarded sensor) — exposed for inspection/testing.</summary>
    public float[][] GetHistoricalBatches()
    {
        lock (_sync) return _historicalBatches;
    }

    /// <summary>Validates, registers and seeds one device. Used by both the register form and the .txt import.</summary>
    public (OnboardStatus Status, string Message) Onboard(SensorRegistration registration)
    {
        // 1. Server-side MAC validation (never trust the client).
        if (string.IsNullOrWhiteSpace(registration.MacAddress) ||
            !DeviceImportParser.MacPattern.IsMatch(registration.MacAddress.Trim()))
            return (OnboardStatus.Invalid, "MAC address must look like AA:BB:CC:DD:EE:01.");

        // 2. Recursive validation of the nested Facility -> Zone -> SubZone configuration.
        var facility = new DeviceConfigNode(registration.Facility ?? string.Empty);
        facility.AddChild(registration.Zone ?? string.Empty).AddChild(registration.SubZone ?? string.Empty);

        var errors = new List<string>();
        if (!RecursiveConfigValidator.Validate(facility, errors))
            return (OnboardStatus.Invalid, "Invalid deployment configuration: " + string.Join(" ", errors));

        // 3. Register in the DeviceRegistry dictionary (rejects duplicates in O(1)).
        if (!_store.TryRegisterDevice(registration, out var error))
            return (OnboardStatus.Duplicate, error);

        // 4. Seed realistic history: raw jagged-array row -> List<SensorReading> -> sorted timeline.
        if (registration.Category != SensorCategory.Actuator)
        {
            var rawRow = CreateRawBatch(registration);
            var readings = StoreBatchRow(registration, rawRow);
            _store.SeedTimeline(registration.MacAddress, readings);
        }

        lock (_sync) _registrationLog.Add(registration);
        return (OnboardStatus.Created, "Device registered.");
    }

    // Realistic baseline values per kind of sensor (so charts look sensible straight after onboarding).
    private float[] CreateRawBatch(SensorRegistration reg)
    {
        var row = new float[_rng.Next(8, 13)];   // devices report at different rates -> rows differ in length
        double centre, spread;

        if (reg.Category == SensorCategory.PowerConsumption) { centre = 520; spread = 140; }
        else if (ThresholdPolicy.IsTemperatureUnit(reg.Unit)) { centre = 24; spread = 1.5; }
        else { centre = 56; spread = 4; }

        for (int i = 0; i < row.Length; i++)
            row[i] = (float)Math.Round(centre + (_rng.NextDouble() - 0.5) * 2 * spread, 1);

        return row;
    }

    // Appends the raw row to the jagged array, then turns it into time-stamped SensorReading objects.
    private List<SensorReading> StoreBatchRow(SensorRegistration reg, float[] rawRow)
    {
        lock (_sync)
        {
            var grown = new float[_historicalBatches.Length + 1][];
            Array.Copy(_historicalBatches, grown, _historicalBatches.Length);
            grown[^1] = rawRow;
            _historicalBatches = grown;
        }

        var now = DateTime.UtcNow;
        var list = new List<SensorReading>(rawRow.Length);
        for (int i = 0; i < rawRow.Length; i++)
        {
            list.Add(new SensorReading(reg.MacAddress, rawRow[i], reg.Unit)
            {
                // oldest first, 20 seconds apart, ending just before "now"
                TimestampUtc = now.AddSeconds(-20.0 * (rawRow.Length - i))
            });
        }

        return list;
    }
}
