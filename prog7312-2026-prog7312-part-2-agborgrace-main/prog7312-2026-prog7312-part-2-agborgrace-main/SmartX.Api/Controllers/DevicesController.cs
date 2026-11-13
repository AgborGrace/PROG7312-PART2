using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Models;
using SmartX.Api.Services;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    private readonly SmartXDataStore _store;
    private readonly IngestionPipeline _pipeline;

    public DevicesController(SmartXDataStore store, IngestionPipeline pipeline)
    {
        _store = store;
        _pipeline = pipeline;
    }

    /// <summary>GET api/devices?search=&amp;category=&amp;state= — search/filter by name, category or alert state.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<DeviceView>> Get(
        [FromQuery] string? search, [FromQuery] SensorCategory? category, [FromQuery] DeviceStatus? state)
        => Ok(_store.GetDevices(search, category, state));

    [HttpGet("{mac}")]
    public ActionResult<DeviceView> GetOne(string mac)
    {
        var device = _store.GetDevice(DeviceImportParser.NormalizeMac(mac));
        return device is null ? NotFound(new { message = "Unknown device." }) : Ok(device);
    }

    /// <summary>
    /// POST api/devices/import — uploads a .txt list of IoT devices. The file is READ and PARSED here:
    /// every valid line is validated, registered and seeded, so the grid, the command stream and the
    /// rest of the system update automatically (no "fake success" message).
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(1_000_000)]
    public async Task<ActionResult<ImportResult>> Import(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file was supplied." });

        if (!Path.GetExtension(file.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Please upload a .txt file (one device per line)." });

        string text;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            text = await reader.ReadToEndAsync();
        }

        var (parsed, errors) = DeviceImportParser.Parse(text);
        var result = new ImportResult { Failed = errors.Count };
        result.Messages.AddRange(errors);

        foreach (var registration in parsed)
        {
            var (status, message) = _pipeline.Onboard(registration);

            if (status == OnboardStatus.Created)
            {
                result.Imported++;
                var view = _store.GetDevice(registration.MacAddress);
                if (view is not null) result.Devices.Add(view);
            }
            else if (status == OnboardStatus.Duplicate)
            {
                result.Skipped++;
                result.Messages.Add($"{registration.Name}: {message}");
            }
            else
            {
                result.Failed++;
                result.Messages.Add($"{registration.Name}: {message}");
            }
        }

        if (result.Imported == 0 && result.Skipped == 0 && result.Failed == 0)
            return BadRequest(new { message = "The file did not contain any device lines." });

        return Ok(result);
    }
}
