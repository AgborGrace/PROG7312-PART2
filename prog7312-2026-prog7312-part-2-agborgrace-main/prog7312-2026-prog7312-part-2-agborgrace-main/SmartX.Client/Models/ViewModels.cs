using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartX.Client.Models;

/// <summary>Minimal INotifyPropertyChanged base so cards update in place without flicker.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>One live device tile on the Command Stream dashboard.</summary>
public class DeviceCardVm : ObservableObject
{
    private string _name = string.Empty;
    private string _subtitle = string.Empty;
    private string _valueText = "-";
    private string _statusText = string.Empty;
    private string _lastSeenText = string.Empty;
    private DeviceStatus _status = DeviceStatus.AwaitingData;
    private bool _isOn;
    private IReadOnlyList<double> _history = new List<double>();

    public DeviceCardVm(string mac) => Mac = mac;

    public string Mac { get; }
    public SensorCategory Category { get; private set; }
    public string Zone { get; private set; } = string.Empty;
    public string Facility { get; private set; } = string.Empty;
    public string CategoryText { get; private set; } = string.Empty;
    public bool IsActuator => Category == SensorCategory.Actuator;

    public string Name { get => _name; private set => Set(ref _name, value); }
    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }
    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string LastSeenText { get => _lastSeenText; private set => Set(ref _lastSeenText, value); }
    public DeviceStatus Status { get => _status; private set => Set(ref _status, value); }
    public bool IsOn { get => _isOn; private set => Set(ref _isOn, value); }
    public IReadOnlyList<double> History { get => _history; private set => Set(ref _history, value); }

    public void Update(DeviceDto device, List<double>? series)
    {
        Category = device.Category;
        Zone = device.Zone;
        Facility = device.Facility;
        CategoryText = device.CategoryText;

        Name = device.Name;
        Subtitle = $"{device.CategoryText}  |  {device.Zone}";
        ValueText = device.LastValueText;
        StatusText = device.StatusText;
        LastSeenText = device.LastSeenText;
        IsOn = device.IsOn;
        Status = device.Status;
        History = series ?? new List<double>();
    }
}

/// <summary>An entry in the Suggested Actions / Automated Insights lists.</summary>
public class InsightVm
{
    public InsightVm(InsightDto dto)
    {
        Id = dto.Id;
        Kind = dto.Kind;
        Severity = dto.Severity;
        Title = dto.Title;
        Detail = dto.Detail;
        SearchTerm = dto.SearchTerm;
        CommandMac = dto.CommandMac;
        Command = dto.Command;
    }

    public string Id { get; }
    public string Kind { get; }
    public string Severity { get; }
    public string Title { get; }
    public string Detail { get; }
    public string? SearchTerm { get; }
    public string? CommandMac { get; }
    public CommandAction? Command { get; }

    public bool HasAction => (Command.HasValue && CommandMac is not null) || !string.IsNullOrEmpty(SearchTerm);
    public string ActionLabel => Command.HasValue ? "Apply" : "Search";
}

public class AlertVm
{
    public AlertVm(AlertDto dto)
    {
        Id = dto.Id;
        Severity = dto.Severity.ToString();
        Text = $"{dto.DeviceName}: {dto.Message}";
        TimeText = dto.RaisedUtc.ToLocalTime().ToString("HH:mm:ss");
        Icon = dto.Type == AlertType.Disconnected ? "\u26A0" : dto.Severity == AlertSeverity.Critical ? "\u25CF" : "\u25B2";
    }

    public long Id { get; }
    public string Severity { get; }
    public string Text { get; }
    public string TimeText { get; }
    public string Icon { get; }
}

public class ProcessedVm
{
    public ProcessedVm(ProcessedEventDto dto)
    {
        Seq = dto.Seq;
        Kind = dto.Kind;
        Severity = dto.Kind == "Critical" ? "Critical" : "Info";
        Text = $"#{dto.Seq}  {dto.Kind.ToUpperInvariant()}  {dto.DeviceName}  {dto.Value:0.#}{dto.Unit}  (waited {dto.WaitMs:0} ms)";
    }

    public long Seq { get; }
    public string Kind { get; }
    public string Severity { get; }
    public string Text { get; }
}

public class CommandVm
{
    public CommandVm(DeviceCommandDto dto)
    {
        Id = dto.Id;
        Text = $"{dto.IssuedUtc.ToLocalTime():HH:mm:ss}  {dto.Description}";
    }

    public long Id { get; }
    public string Text { get; }
}
