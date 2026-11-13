using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Models;
using SmartX.Api.Services;

namespace SmartX.Api.Controllers;

/// <summary>Manual override commands (water pump, smart relay, valves) with an Undo stack.</summary>
[ApiController]
[Route("api/commands")]
public class CommandsController : ControllerBase
{
    private readonly SmartXDataStore _store;
    private readonly PredictiveEngine _engine;

    public CommandsController(SmartXDataStore store, PredictiveEngine engine)
    {
        _store = store;
        _engine = engine;
    }

    private string CurrentUser => HttpContext.Items["UserEmail"] as string ?? "unknown";

    /// <summary>POST api/commands — executes a command and pushes it on the undo stack.</summary>
    [HttpPost]
    public ActionResult<DeviceCommand> Execute([FromBody] CommandRequest request)
    {
        var (outcome, message, command) = _store.ExecuteCommand(request, CurrentUser);

        if (outcome != CommandOutcome.Executed || command is null)
        {
            return outcome == CommandOutcome.UnknownDevice
                ? NotFound(new { message })
                : BadRequest(new { message });
        }

        // The predictive engine learns from every manual command.
        _engine.RecordCommand(command.Action, command.Mac, command.TargetName);
        return Ok(command);
    }

    /// <summary>POST api/commands/undo — reverts the most recent command. An empty history is reported, never a crash.</summary>
    [HttpPost("undo")]
    public ActionResult<UndoResult> Undo() => Ok(_store.UndoLastCommand());

    /// <summary>GET api/commands/history — the stack, most recent command first.</summary>
    [HttpGet("history")]
    public ActionResult<IEnumerable<DeviceCommand>> History() => Ok(_store.GetCommandHistory(25));
}
