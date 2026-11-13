using System.Text.Json.Serialization;

namespace SmartX.Api.Models;

public enum CommandAction
{
    TurnOn,
    TurnOff,
    Toggle,
    ShutDownAll
}

/// <summary>
/// A manual override command. Commands are pushed on the CommandUndoStack (Stack&lt;T&gt;), so the most recent
/// command is always on top and can be reverted with "Undo". <see cref="PreviousStates"/> is a snapshot
/// (MAC -> previous IsOn) so even a bulk "shut down all actuators" command can be undone in one step.
/// </summary>
public class DeviceCommand
{
    public long Id { get; set; }
    public CommandAction Action { get; set; }

    /// <summary>Target MAC address, or "*" for ShutDownAll.</summary>
    public string Mac { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IssuedBy { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; }

    [JsonIgnore]
    public Dictionary<string, bool> PreviousStates { get; set; } = new();
}

public class CommandRequest
{
    public string Mac { get; set; } = string.Empty;
    public CommandAction Action { get; set; }
}
