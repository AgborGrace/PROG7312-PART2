using System.Windows;
using System.Windows.Controls;

namespace SmartX.Client.Controls;

public partial class SmartXLogo : UserControl
{
    public static readonly DependencyProperty ShowWordmarkProperty = DependencyProperty.Register(
        nameof(ShowWordmark), typeof(bool), typeof(SmartXLogo),
        new PropertyMetadata(true, (d, _) => ((SmartXLogo)d).ApplyWordmark()));

    public static readonly DependencyProperty MarkSizeProperty = DependencyProperty.Register(
        nameof(MarkSize), typeof(double), typeof(SmartXLogo),
        new PropertyMetadata(34.0, (d, _) => ((SmartXLogo)d).ApplySize()));

    public SmartXLogo()
    {
        InitializeComponent();
    }

    /// <summary>Show the "SMART-X / MESH GATEWAY" text next to the hexagon mark.</summary>
    public bool ShowWordmark
    {
        get => (bool)GetValue(ShowWordmarkProperty);
        set => SetValue(ShowWordmarkProperty, value);
    }

    /// <summary>Width/height of the hexagon mark in device-independent pixels.</summary>
    public double MarkSize
    {
        get => (double)GetValue(MarkSizeProperty);
        set => SetValue(MarkSizeProperty, value);
    }

    private void ApplyWordmark()
    {
        if (Wordmark is null) return;
        Wordmark.Visibility = ShowWordmark ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplySize()
    {
        if (MarkBox is null) return;
        MarkBox.Width = MarkSize;
        MarkBox.Height = MarkSize;
    }
}
