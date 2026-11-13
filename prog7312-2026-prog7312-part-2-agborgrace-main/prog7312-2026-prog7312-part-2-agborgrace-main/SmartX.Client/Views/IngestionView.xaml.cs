using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartX.Client.Converters;
using SmartX.Client.Models;

namespace SmartX.Client.Views;

/// <summary>
/// MODULE 1 — Sensor Data Ingestion and Telemetry (Part 1, extended after lecturer feedback).
/// Register devices, attach files, import a .txt device list (parsed by the API and loaded into the grid), and send
/// test readings. The page instance is kept alive by the shell, so nothing is lost when the user switches tabs.
/// </summary>
public partial class IngestionView : UserControl
{
    private static readonly Regex MacPattern = new(@"^([0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}$", RegexOptions.Compiled);

    private readonly ObservableCollection<string> _feed = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    private SensorCategory _category = SensorCategory.Environmental;
    private string? _attachmentPath;
    private string? _selectedMac;
    private bool _refreshing;
    private bool _ready;
    private int _cleanStreak;

    /// <summary>Raised after devices were registered/imported so other modules can refresh at once.</summary>
    public event EventHandler? DevicesChanged;

    public IngestionView()
    {
        InitializeComponent();

        ActivityFeed.ItemsSource = _feed;
        Ring.Percentage = 100;
        _ready = true;

        _timer.Tick += async (_, _) => await RefreshDevicesAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshDevicesAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    // ------------------------------------------------------------------ registration form

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        if (ReferenceEquals(sender, CatPower)) { _category = SensorCategory.PowerConsumption; UnitBox.Text = "W"; }
        else if (ReferenceEquals(sender, CatActuator)) { _category = SensorCategory.Actuator; UnitBox.Text = string.Empty; }
        else { _category = SensorCategory.Environmental; UnitBox.Text = "%"; }
    }

    private void BrowseAttachment_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a configuration file, log file or deployment photo",
            Filter = "Supported files|*.json;*.txt;*.log;*.png;*.jpg;*.jpeg|All files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            _attachmentPath = dialog.FileName;
            AttachmentBox.Text = Path.GetFileName(dialog.FileName);
        }
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        // --- client-side validation before any network call ---
        string mac = MacAddressBox.Text.Trim();
        if (!MacPattern.IsMatch(mac)) { SetStatus("MAC address must look like AA:BB:CC:DD:EE:01.", true); return; }

        if (string.IsNullOrWhiteSpace(DeviceNameBox.Text)) { SetStatus("Give the device a name, e.g. Pump A.", true); return; }

        if (string.IsNullOrWhiteSpace(FacilityBox.Text) || string.IsNullOrWhiteSpace(ZoneBox.Text) || string.IsNullOrWhiteSpace(SubZoneBox.Text))
        {
            SetStatus("Facility, Zone and Sub-Zone are all required.", true);
            return;
        }

        var registration = new RegistrationDto
        {
            MacAddress = mac,
            Name = DeviceNameBox.Text.Trim(),
            Facility = FacilityBox.Text.Trim(),
            Zone = ZoneBox.Text.Trim(),
            SubZone = SubZoneBox.Text.Trim(),
            Category = _category,
            Unit = UnitBox.Text.Trim()
        };

        RegisterButton.IsEnabled = false;
        SetStatus("Registering device with the gateway...", false);

        var response = await App.Api.RegisterDeviceAsync(registration);
        RegisterButton.IsEnabled = true;

        if (!response.IsSuccess)
        {
            SetStatus("Registration failed: " + response.Error, true);
            AddFeed($"[FAIL] Registration failed for {mac}: {response.Error}");
            return;
        }

        SetStatus("Device registered successfully.", false);
        AddFeed($"[OK] {registration.Name} ({mac}) registered under {registration.Facility} > {registration.Zone} > {registration.SubZone}.");

        // The API rejects uploads for unknown devices, so the attachment goes up AFTER registration.
        if (_attachmentPath is not null)
        {
            var upload = await App.Api.UploadAttachmentAsync(mac, _attachmentPath);
            AddFeed(upload.IsSuccess
                ? $"[OK] File \"{Path.GetFileName(_attachmentPath)}\" attached to {registration.Name}."
                : $"[FAIL] File upload failed: {upload.Error}");

            _attachmentPath = null;
            AttachmentBox.Text = "No file selected";
        }

        MacAddressBox.Clear();
        DeviceNameBox.Clear();

        await RefreshDevicesAsync();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ .txt import

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a text file that lists your IoT devices",
            Filter = "Text files|*.txt"
        };

        if (dialog.ShowDialog() != true) return;

        ImportButton.IsEnabled = false;
        ImportResultText.Foreground = Palette.Grey;
        ImportResultText.Text = "Uploading and parsing " + Path.GetFileName(dialog.FileName) + "...";

        var response = await App.Api.ImportDevicesAsync(dialog.FileName);
        ImportButton.IsEnabled = true;

        if (!response.IsSuccess || response.Data is null)
        {
            ImportResultText.Foreground = Palette.Red;
            ImportResultText.Text = "Import failed: " + response.Error;
            AddFeed("[FAIL] Import failed: " + response.Error);
            return;
        }

        var result = response.Data;
        bool anyProblem = result.Failed > 0 || result.Skipped > 0;

        var summary = $"Imported {result.Imported} device(s), skipped {result.Skipped} duplicate(s), {result.Failed} line(s) rejected.";
        if (result.Messages.Count > 0)
            summary += Environment.NewLine + string.Join(Environment.NewLine, result.Messages.Take(4).Select(m => "  - " + m))
                       + (result.Messages.Count > 4 ? Environment.NewLine + $"  ...and {result.Messages.Count - 4} more." : string.Empty);

        ImportResultText.Foreground = anyProblem && result.Imported == 0 ? Palette.Amber : Palette.Teal;
        ImportResultText.Text = summary;
        AddFeed($"[IMPORT] {Path.GetFileName(dialog.FileName)}: {result.Imported} imported, {result.Skipped} skipped, {result.Failed} rejected.");

        await RefreshDevicesAsync();
        if (result.Imported > 0) DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ devices grid

    private async Task RefreshDevicesAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            var response = await App.Api.GetDevicesAsync();
            if (!response.IsSuccess || response.Data is null) return;

            // Replacing the ItemsSource clears the selection, so remember it and put it back.
            var devices = response.Data;
            DevicesGrid.ItemsSource = devices;
            DeviceCountText.Text = $"{devices.Count} device(s)";

            if (_selectedMac is not null)
                DevicesGrid.SelectedItem = devices.FirstOrDefault(d => string.Equals(d.Mac, _selectedMac, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void DevicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;

        var selected = DevicesGrid.SelectedItem as DeviceDto;
        _selectedMac = selected?.Mac;
        SendReadingButton.IsEnabled = selected is not null;
    }

    // ------------------------------------------------------------------ test reading -> Ingestion Health ring

    private async void SendReading_Click(object sender, RoutedEventArgs e)
    {
        if (DevicesGrid.SelectedItem is not DeviceDto device) return;

        if (!double.TryParse(ReadingValueBox.Text.Trim(), out double value))
        {
            SetStatus("Enter a numeric reading value first (e.g. 42.5).", true);
            return;
        }

        var response = await App.Api.IngestAsync(device.Mac, value, device.Unit);

        if (!response.IsSuccess || response.Data is null)
        {
            SetStatus("Could not send the reading: " + response.Error, true);
            AddFeed($"[FAIL] Reading for {device.Name} rejected: {response.Error}");
            return;
        }

        if (response.Data.Critical)
        {
            _cleanStreak = 0;
            Ring.Percentage = 30;
            AddFeed($"[CRITICAL] {device.Name}: {response.Data.Message}");
        }
        else
        {
            _cleanStreak++;
            Ring.Percentage = Math.Min(100, 60 + _cleanStreak * 5);
            AddFeed($"[OK] {device.Name} reported {value:0.#} {device.Unit}".TrimEnd() + " (queued).");
        }

        StreakText.Text = $"{_cleanStreak} clean ingestion cycle(s)";
    }

    // ------------------------------------------------------------------ helpers

    private void AddFeed(string text)
    {
        _feed.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        while (_feed.Count > 40) _feed.RemoveAt(_feed.Count - 1);
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError ? Palette.Red : Palette.Grey;
    }
}
