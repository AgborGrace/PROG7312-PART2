using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Windows.Threading;
using SmartX.Client.Converters;

namespace SmartX.Client.Views;

/// <summary>
/// The main menu after sign-in. Each module page is created ONCE and re-used, so switching tabs never loses data,
/// never restarts a download and never freezes the UI (all API calls are async). Only modules 1 and 2 are
/// clickable; module 3 (final PoE) is deliberately visible but greyed out.
/// </summary>
public partial class ShellView : UserControl
{
    private readonly IngestionView _ingestion = new();
    private readonly CommandStreamView _stream = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _tick;
    private bool _ready;

    public event EventHandler? SignedOut;

    public ShellView()
    {
        InitializeComponent();

        UserText.Text = App.Auth.Session?.Email ?? string.Empty;

        // When devices are registered or imported, the command stream refreshes immediately.
        _ingestion.DevicesChanged += (_, _) => _stream.RequestRefresh();

        ModuleHost.Content = _ingestion;
        _ready = true;

        _timer.Tick += async (_, _) => await OnTickAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshApiStatusAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;   // Checked also fires while the XAML is loading
        ModuleHost.Content = ReferenceEquals(sender, NavStream) ? _stream : _ingestion;
    }

    private async Task OnTickAsync()
    {
        _tick++;

        SimText.Text = App.Simulator.IsRunning
            ? $"Running - {App.Simulator.PacketsSent} packets sent"
            : "Stopped (start it in the Command Stream test lab)";

        if (_tick % 5 == 0) await RefreshApiStatusAsync();
    }

    private async Task RefreshApiStatusAsync()
    {
        bool online = await App.Api.PingAsync();
        ApiDot.Fill = online ? Palette.Teal : Palette.Red;
        ApiStatusText.Text = online ? "API online" : "API offline - start SmartX.Api";
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        App.Simulator.Stop();
        App.Auth.SignOut();
        SignedOut?.Invoke(this, EventArgs.Empty);
    }
}
