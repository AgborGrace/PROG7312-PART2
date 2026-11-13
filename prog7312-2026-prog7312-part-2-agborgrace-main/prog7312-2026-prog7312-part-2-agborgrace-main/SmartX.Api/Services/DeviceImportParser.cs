using System.Text.RegularExpressions;
using SmartX.Api.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Parses the uploaded .txt device list. One device per line:
///
///     MAC, Name, Category, Facility, Zone, SubZone [, Unit]
///
/// Separators may be comma, semicolon, pipe or tab. Blank lines and lines starting with '#' are ignored,
/// and a header line starting with "MAC" is skipped. Category accepts Environmental, Power /
/// PowerConsumption / "Power Consumption", and Actuator (any case).
/// </summary>
public static class DeviceImportParser
{
    public static readonly Regex MacPattern =
        new(@"^([0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}$", RegexOptions.Compiled);

    private static readonly char[] Separators = { ',', ';', '|', '\t' };

    public static string NormalizeMac(string mac) => (mac ?? string.Empty).Trim().Replace('-', ':').ToUpperInvariant();

    public static string DefaultUnit(SensorCategory category) => category switch
    {
        SensorCategory.Environmental => "%",
        SensorCategory.PowerConsumption => "W",
        _ => string.Empty
    };

    public static bool TryParseCategory(string raw, out SensorCategory category)
    {
        var key = (raw ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
        switch (key)
        {
            case "environmental":
            case "env":
                category = SensorCategory.Environmental;
                return true;
            case "power":
            case "powerconsumption":
                category = SensorCategory.PowerConsumption;
                return true;
            case "actuator":
                category = SensorCategory.Actuator;
                return true;
            default:
                category = SensorCategory.Environmental;
                return false;
        }
    }

    public static (List<SensorRegistration> Parsed, List<string> Errors) Parse(string text)
    {
        var parsed = new List<SensorRegistration>();
        var errors = new List<string>();

        var lines = (text ?? string.Empty).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            var line = lines[i].Trim().TrimStart('\uFEFF');   // tolerate a UTF-8 BOM on the first line
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var cols = line.Split(Separators).Select(c => c.Trim()).ToArray();

            // Skip an optional header row.
            if (cols[0].Equals("mac", StringComparison.OrdinalIgnoreCase) ||
                cols[0].Equals("macaddress", StringComparison.OrdinalIgnoreCase))
                continue;

            if (cols.Length < 6)
            {
                errors.Add($"Line {lineNo}: expected MAC, Name, Category, Facility, Zone, SubZone [, Unit] but found {cols.Length} column(s).");
                continue;
            }

            if (!MacPattern.IsMatch(cols[0]))
            {
                errors.Add($"Line {lineNo}: '{cols[0]}' is not a valid MAC address (use AA:BB:CC:DD:EE:01).");
                continue;
            }

            if (string.IsNullOrWhiteSpace(cols[1]))
            {
                errors.Add($"Line {lineNo}: device name is empty.");
                continue;
            }

            if (!TryParseCategory(cols[2], out var category))
            {
                errors.Add($"Line {lineNo}: unknown category '{cols[2]}' (use Environmental, Power or Actuator).");
                continue;
            }

            // Recursive validation of the Facility -> Zone -> SubZone hierarchy (reuses the Part 1 algorithm).
            var facility = new DeviceConfigNode(cols[3]);
            facility.AddChild(cols[4]).AddChild(cols[5]);
            var configErrors = new List<string>();
            if (!RecursiveConfigValidator.Validate(facility, configErrors))
            {
                errors.Add($"Line {lineNo}: {string.Join(" ", configErrors)}");
                continue;
            }

            var unit = cols.Length >= 7 && !string.IsNullOrWhiteSpace(cols[6]) ? cols[6] : DefaultUnit(category);

            parsed.Add(new SensorRegistration
            {
                MacAddress = NormalizeMac(cols[0]),
                Name = cols[1],
                Category = category,
                Facility = cols[3],
                Zone = cols[4],
                SubZone = cols[5],
                Unit = unit
            });
        }

        return (parsed, errors);
    }
}
