using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Models;
using SmartX.Api.Services;

namespace SmartX.Api.Controllers;

/// <summary>Part 1 endpoints (device registration, file attachment, history) — now backed by the Part 2 data store.</summary>
[ApiController]
[Route("api/sensors")]
public class SensorsController : ControllerBase
{
    private readonly SmartXDataStore _store;
    private readonly IngestionPipeline _pipeline;
    private readonly IWebHostEnvironment _env;

    public SensorsController(SmartXDataStore store, IngestionPipeline pipeline, IWebHostEnvironment env)
    {
        _store = store;
        _pipeline = pipeline;
        _env = env;
    }

    /// <summary>GET api/sensors — every registered device with its live state.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<DeviceView>> GetAll() => Ok(_store.GetDevices(null, null, null));

    /// <summary>POST api/sensors/register — validates (MAC format + recursive configuration check) and registers a device.</summary>
    [HttpPost("register")]
    public ActionResult Register([FromBody] SensorRegistration registration)
    {
        var (status, message) = _pipeline.Onboard(registration);

        return status switch
        {
            OnboardStatus.Created => Created($"api/devices/{registration.MacAddress}", _store.GetDevice(registration.MacAddress)),
            OnboardStatus.Duplicate => Conflict(new { message }),
            _ => BadRequest(new { message })
        };
    }

    /// <summary>POST api/sensors/{mac}/upload — attaches a config file, log file or deployment photo to a device.</summary>
    [HttpPost("{mac}/upload")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult> UploadFile(string mac, IFormFile file)
    {
        mac = DeviceImportParser.NormalizeMac(mac);

        if (!_store.DeviceExists(mac))
            return NotFound(new { message = "Unknown device MAC address. Register the device first." });

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file was supplied." });

        var uploadsFolder = Path.Combine(_env.ContentRootPath, "Uploads");
        Directory.CreateDirectory(uploadsFolder);

        var safeFileName = $"{mac.Replace(':', '-')}_{Path.GetFileName(file.FileName)}";
        var fullPath = Path.Combine(uploadsFolder, safeFileName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        _store.AttachFile(mac, safeFileName);
        return Ok(new { message = "File uploaded.", fileName = safeFileName });
    }

    /// <summary>GET api/sensors/{mac}/history — the device's readings in timestamp order (SortedDictionary).</summary>
    [HttpGet("{mac}/history")]
    public ActionResult<IEnumerable<SensorReading>> GetHistory(string mac, [FromQuery] int take = 200)
        => Ok(_store.GetTimeline(DeviceImportParser.NormalizeMac(mac), Math.Clamp(take, 1, 500)));
}
