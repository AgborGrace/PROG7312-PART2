using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SmartX.Client.Converters;
using SmartX.Client.Models;

namespace SmartX.Client.Views;

/// <summary>
/// MODULE 2 — Real-Time Command Stream and History (Part 2).
/// Polls ONE endpoint (api/stream/snapshot) every second and renders: live device cards with sparklines, KPIs,
/// manual override commands with undo, search/filter by category and alert state, alerts, the processing log,
/// Suggested Actions / Automated Insights from the predictive engine, and a test lab for the simulator.
/// All collections are updated in place (cards by MAC address) so the screen never flickers or loses selection.
/// The page instance lives as long as the shell, so switching tabs keeps everything.
/// </summary>
public partial class CommandStreamView : UserControl
{
    private readonly ObservableCollection<DeviceCardVm> _cards = new();
    private readonly Dictionary<string, DeviceCardVm> _cardByMac = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICollectionView _cardView;

    private readonly ObservableCollection<InsightVm> _suggested = new();
    private readonly ObservableCollection<InsightVm> _insights = new();
    private readonly ObservableCollection<AlertVm> _alerts = new();
    private readonly ObservableCollection<ProcessedVm> _processing = new();
    private readonly ObservableCollection<CommandVm> _history = new();

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    private string _alertSig = string.Empty, _procSig = string.Empty, _histSig = string.Empty;
    private string _sugSig = string.Empty, _insSig = string.Empty;

    private SensorCategory? _categoryFilter;
    private DeviceStatus? _stateFilter;
    private bool _ready;
    private bool _polling;

    public CommandStreamView()
    {
        InitializeComponent();

        DeviceList.ItemsSource = _cards;
        SuggestedList.ItemsSource = _suggested;
        InsightList.ItemsSource = _insights;
        AlertList.ItemsSource = _alerts;
        ProcessingList.ItemsSource = _processing;
        HistoryList.ItemsSource = _history;

        // Search / category / alert-state filtering is done on the live view of the cards.
        _cardView = CollectionViewSource.GetDefaultView(_cards);
        _cardView.Filter = FilterCard;

        _ready = true;

        _timer.Tick += async (_, _) => await PollAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            UpdateSimButton();
            await PollAsync();
        };
        Unloaded += (_, _) => _timer.Stop();   // the instance (and its data) is kept; only the polling pauses
    }

    /// <summary>Called by the shell when devices were registered/imported on the ingestion tab.</summary>
    public void RequestRefresh() => _ = PollAsync();

    // ------------------------------------------------------------------ polling

    private async Task PollAsync()
    {
        if (_polling) return;   // never overlap two refreshes
        _polling = true;

        try
        {
            var response = await App.Api.GetSnapshotAsync();

            if (!response.IsSuccess || response.Data is null)
            {
                ConnectionText.Text = "Not connected to the API: " + response.Error;
                ConnectionText.Visibility = Visibility.Visible;
                return;
            }

            ConnectionText.Visibility = Visibility.Collapsed;
            ApplySnapshot(response.Data);
        }
        finally
        {
            _polling = false;
        }
    }

    private void ApplySnapshot(StreamSnapshotDto s)
    {
        // ---- KPIs ----
        int total = s.Devices.Count;
        int healthy = s.Devices.Count(d => d.Status == DeviceStatus.Normal || d.Status == DeviceStatus.AwaitingData);
        int online = s.Devices.Count(d => d.Status == DeviceStatus.Normal || d.Status == DeviceStatus.Warning || d.Status == DeviceStatus.Critical);

        KpiHealthRing.Percentage = total == 0 ? 100 : 100.0 * healthy / total;
        KpiOnline.Text = $"{online}/{total}";
        KpiCritical.Text = s.Devices.Count(d => d.Status == DeviceStatus.Critical).ToString();
        KpiDisconnected.Text = s.DisconnectedNodes.Count.ToString();
        KpiQueue.Text = s.Queues.TelemetryQueueDepth.ToString();
        KpiLoad.Text = $"{s.TotalPowerLoadWatts:0} W";
        KpiSuppressed.Text = s.Queues.DuplicatesSuppressed.ToString();

        // ---- device cards: update existing cards in place, add new ones ----
        foreach (var device in s.Devices)
        {
            if (!_cardByMac.TryGetValue(device.Mac, out var card))
            {
                card = new DeviceCardVm(device.Mac);
                _cardByMac[device.Mac] = card;
                _cards.Add(card);
            }

            s.Series.TryGetValue(device.Mac, out var series);
            card.Update(device, series);
        }

        if (_stateFilter.HasValue) _cardView.Refresh();   // statuses changed, so the state filter may now match differently

        // ---- lists (only rebuilt when their content actually changed) ----
        Sync(_alerts, s.Alerts.Select(a => new AlertVm(a)).ToList(), string.Join(",", s.Alerts.Select(a => a.Id)), ref _alertSig);
        Sync(_processing, s.ProcessingLog.Select(p => new ProcessedVm(p)).ToList(), string.Join(",", s.ProcessingLog.Select(p => p.Seq)), ref _procSig);
        Sync(_history, s.CommandHistory.Select(c => new CommandVm(c)).ToList(), string.Join(",", s.CommandHistory.Select(c => c.Id)), ref _histSig);

        var suggested = s.Insights.Where(i => i.Kind == "SuggestedAction").ToList();
        var automated = s.Insights.Where(i => i.Kind != "SuggestedAction").ToList();
        Sync(_suggested, suggested.Select(i => new InsightVm(i)).ToList(), string.Join("|", suggested.Select(i => i.Id + i.Title)), ref _sugSig);
        Sync(_insights, automated.Select(i => new InsightVm(i)).ToList(), string.Join("|", automated.Select(i => i.Id + i.Title)), ref _insSig);

        NoSuggestionsText.Visibility = _suggested.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoInsightsText.Visibility = _insights.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoHistoryText.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateSelectionUi();
    }

    private static void Sync<T>(ObservableCollection<T> target, List<T> source, string signature, ref string lastSignature)
    {
        if (signature == lastSignature) return;
        lastSignature = signature;

        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    // ------------------------------------------------------------------ search and filters

    private bool FilterCard(object item)
    {
        if (item is not DeviceCardVm card) return false;
        if (_categoryFilter.HasValue && card.Category != _categoryFilter.Value) return false;
        if (_stateFilter.HasValue && card.Status != _stateFilter.Value) return false;

        string term = SearchBox.Text.Trim();
        if (term.Length == 0) return true;

        return card.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || card.Mac.Contains(term, StringComparison.OrdinalIgnoreCase)
            || card.Zone.Contains(term, StringComparison.OrdinalIgnoreCase)
            || card.CategoryText.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _cardView.Refresh();   // live filtering while typing (not recorded as a "search" yet)
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SubmitSearchAsync();
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SubmitSearchAsync();

    /// <summary>A SUBMITTED search (Enter / Search button) is sent to the API, which records it for the predictive engine.</summary>
    private async Task SubmitSearchAsync()
    {
        string term = SearchBox.Text.Trim();
        if (term.Length == 0) return;

        await App.Api.RecordSearchAsync(term);
        await PollAsync();
    }

    private void CategoryChip_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        string? tag = (sender as RadioButton)?.Tag as string;
        _categoryFilter = tag switch
        {
            "Environmental" => SensorCategory.Environmental,
            "PowerConsumption" => SensorCategory.PowerConsumption,
            "Actuator" => SensorCategory.Actuator,
            _ => null
        };
        _cardView.Refresh();
    }

    private void StateChip_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        string? tag = (sender as RadioButton)?.Tag as string;
        _stateFilter = tag switch
        {
            "Normal" => DeviceStatus.Normal,
            "Warning" => DeviceStatus.Warning,
            "Critical" => DeviceStatus.Critical,
            "Disconnected" => DeviceStatus.Disconnected,
            _ => null
        };
        _cardView.Refresh();
    }

    // ------------------------------------------------------------------ selection

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateSelectionUi();
    }

    private void UpdateSelectionUi()
    {
        var card = DeviceList.SelectedItem as DeviceCardVm;

        SelectedNameText.Text = card is null
            ? "No device selected - click a device card"
            : $"{card.Name}  ({card.CategoryText}, {card.StatusText})";

        bool actuator = card is not null && card.IsActuator;
        BtnOn.IsEnabled = actuator;
        BtnOff.IsEnabled = actuator;
        BtnToggle.IsEnabled = actuator;
        DropoutButton.IsEnabled = card is not null;
        ReportDisconnectButton.IsEnabled = card is not null;

        DetailChart.Values = card?.History;
        DetailChart.LineBrush = card is null ? Palette.Teal : Palette.ForStatus(card.Status);
    }

    // ------------------------------------------------------------------ manual override commands

    private async Task RunCommandAsync(CommandAction action)
    {
        string mac;

        if (action == CommandAction.ShutDownAll)
        {
            mac = "*";
        }
        else
        {
            if (DeviceList.SelectedItem is not DeviceCardVm card || !card.IsActuator)
            {
                ShowCommandStatus("Select an actuator (pump, relay or valve) first.", true);
                return;
            }

            mac = card.Mac;
        }

        var response = await App.Api.SendCommandAsync(mac, action);
        ShowCommandStatus(response.IsSuccess ? "Done: " + response.Data?.Description : "Failed: " + response.Error, !response.IsSuccess);
        await PollAsync();
    }

    private async void On_Click(object sender, RoutedEventArgs e) => await RunCommandAsync(CommandAction.TurnOn);
    private async void Off_Click(object sender, RoutedEventArgs e) => await RunCommandAsync(CommandAction.TurnOff);
    private async void Toggle_Click(object sender, RoutedEventArgs e) => await RunCommandAsync(CommandAction.Toggle);

    private async void ShutAll_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show("Switch OFF every pump, relay and valve?", "Shut down all actuators",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.Yes) await RunCommandAsync(CommandAction.ShutDownAll);
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        var response = await App.Api.UndoAsync();
        ShowCommandStatus(response.IsSuccess && response.Data is not null ? response.Data.Message : "Undo failed: " + response.Error,
            !response.IsSuccess);
        await PollAsync();
    }

    private void ShowCommandStatus(string text, bool isError)
    {
        CommandStatusText.Text = text;
        CommandStatusText.Foreground = isError ? Palette.Red : Palette.Teal;
    }

    // ------------------------------------------------------------------ predictive suggestions

    private async void ApplyInsight_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InsightVm insight) return;

        if (insight.Command.HasValue && insight.CommandMac is not null)
        {
            var response = await App.Api.SendCommandAsync(insight.CommandMac, insight.Command.Value);
            ShowCommandStatus(response.IsSuccess ? "Done: " + response.Data?.Description : "Failed: " + response.Error, !response.IsSuccess);
            await PollAsync();
        }
        else if (!string.IsNullOrEmpty(insight.SearchTerm))
        {
            SearchBox.Text = insight.SearchTerm;
            await SubmitSearchAsync();
        }
    }

    // ------------------------------------------------------------------ test lab

    private void UpdateSimButton() => SimButton.Content = App.Simulator.IsRunning ? "Stop simulator" : "Start simulator";

    private void Sim_Click(object sender, RoutedEventArgs e)
    {
        if (App.Simulator.IsRunning) App.Simulator.Stop(); else App.Simulator.Start();
        UpdateSimButton();
        LabStatusText.Text = App.Simulator.IsRunning
            ? "Simulator running: every registered device sends a packet to the API every 2 seconds."
            : "Simulator stopped.";
    }

    private async void Burst_Click(object sender, RoutedEventArgs e)
    {
        BurstButton.IsEnabled = false;
        LabStatusText.Text = "Sending 20 normal packets, then 1 critical...";
        LabStatusText.Text = await App.Simulator.SendBurstAsync();
        BurstButton.IsEnabled = true;
        await PollAsync();
    }

    private async void Spike_Click(object sender, RoutedEventArgs e)
    {
        SpikeButton.IsEnabled = false;
        LabStatusText.Text = await App.Simulator.InjectPowerSpikeAsync();
        SpikeButton.IsEnabled = true;
        await PollAsync();
    }

    private void Dropout_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DeviceCardVm card) return;

        if (!App.Simulator.IsRunning)
        {
            LabStatusText.Text = "Start the simulator first - a dropout means the device stops sending.";
            return;
        }

        bool silenced = App.Simulator.ToggleSilence(card.Mac);
        LabStatusText.Text = silenced
            ? $"{card.Name} stopped sending. It will be flagged as disconnected after about 15 seconds."
            : $"{card.Name} is sending again and will reconnect.";
    }

    private async void ReportDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DeviceCardVm card) return;

        var response = await App.Api.ReportDisconnectAsync(card.Mac);
        LabStatusText.Text = response.IsSuccess && response.Data is not null ? response.Data.Message : "Failed: " + response.Error;
        await PollAsync();
    }

    private async void Scenario_Click(object sender, RoutedEventArgs e)
    {
        ScenarioButton.IsEnabled = false;
        await App.Simulator.RunLearningScenarioAsync(message => LabStatusText.Text = message);
        ScenarioButton.IsEnabled = true;
        await PollAsync();
    }
}
