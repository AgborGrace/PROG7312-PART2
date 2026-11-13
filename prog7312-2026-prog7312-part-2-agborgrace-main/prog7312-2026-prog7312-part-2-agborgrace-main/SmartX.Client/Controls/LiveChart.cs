using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SmartX.Client.Controls;

/// <summary>
/// A tiny hand-drawn line chart (sparkline with a soft fill). Bind <see cref="Values"/> to a list of readings and
/// <see cref="LineBrush"/> to a status colour; the chart redraws itself whenever either changes.
/// Drawn directly with DrawingContext — no third-party charting library needed.
/// </summary>
public class LiveChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(LiveChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(LiveChart),
        new FrameworkPropertyMetadata(Brushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen GridPen = CreateGridPen();

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    private static Pen CreateGridPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
        pen.Freeze();
        return pen;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth, h = ActualHeight;
        if (w < 8 || h < 8) return;

        // faint horizontal guide lines
        for (int i = 1; i < 4; i++)
        {
            double y = Math.Round(h * i / 4.0) + 0.5;
            dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        }

        var values = Values;
        if (values is null || values.Count < 2) return;

        double min = values.Min(), max = values.Max();
        if (Math.Abs(max - min) < 1e-9) { min -= 1; max += 1; }
        double pad = (max - min) * 0.15;
        min -= pad;
        max += pad;

        var points = new List<Point>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            double x = w * i / (values.Count - 1);
            double y = h - (values[i] - min) / (max - min) * h;
            points.Add(new Point(x, y));
        }

        // soft fill under the line
        var fillGeometry = new StreamGeometry();
        using (var ctx = fillGeometry.Open())
        {
            ctx.BeginFigure(new Point(0, h), true, true);
            ctx.PolyLineTo(points, false, false);
            ctx.LineTo(new Point(w, h), false, false);
        }
        fillGeometry.Freeze();

        var fillBrush = LineBrush.Clone();
        fillBrush.Opacity = 0.16;
        dc.DrawGeometry(fillBrush, null, fillGeometry);

        // the line itself
        var lineGeometry = new StreamGeometry();
        using (var ctx = lineGeometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.GetRange(1, points.Count - 1), true, true);
        }
        lineGeometry.Freeze();

        var pen = new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, lineGeometry);

        // dot on the latest reading
        dc.DrawEllipse(LineBrush, null, points[points.Count - 1], 3, 3);
    }
}
