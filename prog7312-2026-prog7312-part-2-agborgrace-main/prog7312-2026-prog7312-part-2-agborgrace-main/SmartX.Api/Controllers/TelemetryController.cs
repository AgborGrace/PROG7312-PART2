using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Models;
using SmartX.Api.Services;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public class TelemetryController : ControllerBase
{
    private readonly SmartXDataStore _store;

    public TelemetryController(SmartXDataStore store) => _store = store;

    /// <summary>
    /// POST api/telemetry/ingest — the endpoint a device (or the simulator) hits.
    /// The value is wrapped in the generic TelemetryPacket&lt;double&gt; and handed to the data store, which either
    /// puts it on the FIFO queue (202 Accepted) or — for a critical reading — processes it immediately (200 OK).
    /// </summary>
    [HttpPost("ingest")]
    public ActionResult<IngestResult> Ingest([FromBody] IngestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceMac))
            return BadRequest(new { message = "DeviceMac is required." });

        if (double.IsNaN(request.Value) || double.IsInfinity(request.Value))
            return BadRequest(new { message = "Value must be a finite number." });

        var packet = new TelemetryPacket<double>(
            DeviceImportParser.NormalizeMac(request.DeviceMac), request.Value, request.Unit ?? string.Empty);

        var result = _store.Ingest(packet);

        return result.Outcome switch
        {
            IngestOutcome.UnknownDevice => NotFound(result),
            IngestOutcome.QueueFull => StatusCode(StatusCodes.Status429TooManyRequests, result),
            IngestOutcome.CriticalProcessed => Ok(result),
            _ => Accepted(result)
        };
    }

    /// <summary>GET api/telemetry/{mac}/timeline?take=60 — latest readings in timestamp order.</summary>
    [HttpGet("{mac}/timeline")]
    public ActionResult<IEnumerable<SensorReading>> Timeline(string mac, [FromQuery] int take = 60)
        => Ok(_store.GetTimeline(DeviceImportParser.NormalizeMac(mac), Math.Clamp(take, 1, 500)));
}
