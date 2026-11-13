namespace SmartX.Api.Models;

/// <summary>
/// A node in the nested deployment-configuration tree (Facility -> Zone -> SubZone). Built from a
/// <see cref="SensorRegistration"/> so <see cref="Services.RecursiveConfigValidator"/> can walk it recursively.
/// </summary>
public class DeviceConfigNode
{
    public string Name { get; set; } = string.Empty;
    public List<DeviceConfigNode> Children { get; } = new();

    public DeviceConfigNode(string name) => Name = name;

    public DeviceConfigNode AddChild(string name)
    {
        var child = new DeviceConfigNode(name);
        Children.Add(child);
        return child;
    }
}
