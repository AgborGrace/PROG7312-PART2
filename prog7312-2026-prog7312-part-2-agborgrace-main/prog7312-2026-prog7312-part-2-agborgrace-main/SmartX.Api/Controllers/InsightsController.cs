using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Models;
using SmartX.Api.Services;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/insights")]
public class InsightsController : ControllerBase
{
    private readonly SmartXDataStore _store;
    private readonly PredictiveEngine _engine;

    public InsightsController(SmartXDataStore store, PredictiveEngine engine)
    {
        _store = store;
        _engine = engine;
    }

    /// <summary>GET api/insights — Suggested Actions + Automated Insights.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<Insight>> Get() => Ok(_engine.GenerateInsights(_store.BuildEngineInput()));

    /// <summary>POST api/insights/search — records a submitted search so the engine can learn from it.</summary>
    [HttpPost("search")]
    public ActionResult RecordSearch([FromBody] SearchEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Term))
            return BadRequest(new { message = "Search term is empty." });

        _engine.RecordSearch(request.Term);
        return Ok(new { recorded = true });
    }

    /// <summary>GET api/insights/history — the recorded search/command history, newest first.</summary>
    [HttpGet("history")]
    public ActionResult<IEnumerable<UserActionEvent>> History() => Ok(_engine.GetHistory(50));
}
