using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Models;
using SmartX.Api.Services;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly SmartXDataStore _store;

    public AlertsController(SmartXDataStore store) => _store = store;

    [HttpGet]
    public ActionResult<IEnumerable<AlertRecord>> Recent() => Ok(_store.GetRecentAlerts(50));

    [HttpGet("disconnected")]
    public ActionResult<IEnumerable<string>> Disconnected() => Ok(_store.GetDisconnectedNodes());

    /// <summary>
    /// POST api/alerts/disconnect/{mac} — report a node as down. Call it twice: the first call adds the node
    /// to the DisconnectedNodes HashSet and raises ONE alert, the second is reported as a suppressed duplicate.
    /// </summary>
    [HttpPost("disconnect/{mac}")]
    public ActionResult<DisconnectReportResult> ReportDisconnect(string mac)
    {
        var (found, result) = _store.ReportDisconnect(DeviceImportParser.NormalizeMac(mac));
        return found ? Ok(result) : NotFound(result);
    }
}
