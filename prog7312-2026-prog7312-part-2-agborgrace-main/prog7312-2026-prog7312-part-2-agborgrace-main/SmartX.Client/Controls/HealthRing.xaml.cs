using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartX.Client.Controls;

/// <summary>
/// Circular health gauge (0-100). Used for "Ingestion Health" on the ingestion screen and "System Health" on the
/// command stream. The colour shifts teal -> amber -> red so state is readable at a glance, backing up the number.
/// </summary>
public partial class HealthRing : UserControl
{
    public static readonly DependencyProperty PercentageProperty = DependencyProperty.Register(
        nameof(Percentage), typeof(double), typeof(HealthRing),
        new PropertyMetadata(100.0, (d, _) => ((HealthRing)d).Render()));

    public HealthRing()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
    }

    public double Percentage
    {
        get => (double)GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, value);
    }

    private void Render()
    {
        if (PercentText is null || ArcPath is null) return;

        double pct = Math.Clamp(Percentage, 0, 100);
        PercentText.Text = $"{pct:F0}%";

        ArcPath.Stroke = pct >= 70
            ? new SolidColorBrush(Color.FromRgb(0x14, 0xB8, 0xA6))
            : pct >= 40
                ? new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23))
                : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        double radius = (size - 14 - 9) / 2;   // margin (7*2) and stroke thickness
        if (radius <= 0) return;
        var centre = new Point(size / 2 - 7, size / 2 - 7);

        const double startAngle = -90;   // 12 o'clock
        double sweep = 360 * (pct / 100.0);

        // A zero-length or full-circle ArcSegment cannot be drawn, so nudge those cases.
        if (sweep <= 0.5) { ArcPath.Data = null; return; }
        if (sweep >= 359.5) sweep = 359.5;

        Point OnCircle(double angleDeg) => new(
            centre.X + radius * Math.Cos(angleDeg * Math.PI / 180),
            centre.Y + radius * Math.Sin(angleDeg * Math.PI / 180));

        var figure = new PathFigure { StartPoint = OnCircle(startAngle) };
        figure.Segments.Add(new ArcSegment(
            OnCircle(startAngle + sweep),
            new Size(radius, radius),
            0,
            sweep > 180,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        ArcPath.Data = geometry;
    }
}
