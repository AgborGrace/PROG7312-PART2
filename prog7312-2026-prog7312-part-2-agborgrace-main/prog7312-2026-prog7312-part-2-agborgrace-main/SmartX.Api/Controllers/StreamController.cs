using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Models;
using SmartX.Api.Services;

namespace SmartX.Api.Controllers;

/// <summary>One call returns everything the Real-Time Command Stream dashboard needs.</summary>
[ApiController]
[Route("api/stream")]
public class StreamController : ControllerBase
{
    private readonly SmartXDataStore _store;
    private readonly PredictiveEngine _engine;

    public StreamController(SmartXDataStore store, PredictiveEngine engine)
    {
        _store = store;
        _engine = engine;
    }

    [HttpGet("snapshot")]
    public ActionResult<StreamSnapshot> Snapshot()
    {
        var snapshot = _store.BuildSnapshot();
        snapshot.Insights = _engine.GenerateInsights(_store.BuildEngineInput());
        return Ok(snapshot);
    }
}
