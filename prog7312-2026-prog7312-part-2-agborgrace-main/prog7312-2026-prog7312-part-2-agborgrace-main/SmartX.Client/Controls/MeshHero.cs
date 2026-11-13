using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartX.Client.Controls;

/// <summary>
/// The animated mesh network on the landing page: sensor nodes that pulse, links between neighbours, and data
/// packets hopping across the mesh toward the gateway. Pure vector drawing (no images or video needed) so it stays
/// crisp at any size. The animation only runs while the control is on screen.
/// </summary>
public class MeshHero : FrameworkElement
{
    private sealed class Node
    {
        public double X;
        public double Y;
        public double Phase;
        public bool IsGateway;
    }

    private sealed class Packet
    {
        public int Edge;
        public double T;
        public double Speed;
        public bool Forward;
    }

    private readonly List<Node> _nodes = new();
    private readonly List<(int A, int B)> _edges = new();
    private readonly List<Packet> _packets = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _rng = new(11);

    private static readonly Pen EdgePen = MakePen(Color.FromArgb(80, 20, 184, 166), 1.3);
    private static readonly Brush PacketBrush = MakeBrush(Color.FromRgb(130, 255, 236));
    private static readonly Brush NodeBrush = MakeBrush(Color.FromRgb(20, 184, 166));
    private static readonly Brush GatewayBrush = MakeBrush(Color.FromRgb(79, 142, 247));
    private static readonly Brush NodeGlow = MakeGlow(Color.FromRgb(20, 184, 166));
    private static readonly Brush GatewayGlow = MakeGlow(Color.FromRgb(79, 142, 247));
    private static readonly Brush LabelBrush = MakeBrush(Color.FromRgb(160, 190, 230));
    private static readonly Typeface LabelFace = new("Segoe UI");

    public MeshHero()
    {
        BuildGraph();

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Step();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private static Pen MakePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush MakeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush MakeGlow(Color color)
    {
        var brush = new RadialGradientBrush(Color.FromArgb(120, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private void BuildGraph()
    {
        // Hand-placed positions (0..1 of the control size). The last one is the gateway.
        double[,] positions =
        {
            { 0.10, 0.22 }, { 0.20, 0.68 }, { 0.30, 0.42 }, { 0.38, 0.12 }, { 0.40, 0.86 },
            { 0.52, 0.55 }, { 0.58, 0.20 }, { 0.62, 0.84 }, { 0.90, 0.16 }, { 0.92, 0.82 },
            { 0.26, 0.92 }, { 0.76, 0.50 }
        };

        for (int i = 0; i < positions.GetLength(0); i++)
        {
            _nodes.Add(new Node
            {
                X = positions[i, 0],
                Y = positions[i, 1],
                Phase = _rng.NextDouble() * Math.PI * 2,
                IsGateway = i == positions.GetLength(0) - 1
            });
        }

        // Link every pair of nearby nodes (a mesh, not a star).
        for (int a = 0; a < _nodes.Count; a++)
        {
            for (int b = a + 1; b < _nodes.Count; b++)
            {
                double dx = _nodes[a].X - _nodes[b].X, dy = _nodes[a].Y - _nodes[b].Y;
                if (Math.Sqrt(dx * dx + dy * dy) < 0.36) _edges.Add((a, b));
            }
        }
    }

    private void Step()
    {
        if (_packets.Count < 16 && _edges.Count > 0 && _rng.NextDouble() < 0.18)
        {
            _packets.Add(new Packet
            {
                Edge = _rng.Next(_edges.Count),
                Speed = 0.012 + _rng.NextDouble() * 0.012,
                Forward = _rng.Next(2) == 0
            });
        }

        for (int i = _packets.Count - 1; i >= 0; i--)
        {
            _packets[i].T += _packets[i].Speed;
            if (_packets[i].T >= 1) _packets.RemoveAt(i);
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth, h = ActualHeight;
        if (w < 50 || h < 50) return;

        double time = _clock.Elapsed.TotalSeconds;
        Point P(Node n) => new(n.X * w, n.Y * h);

        // links
        foreach (var (a, b) in _edges)
            dc.DrawLine(EdgePen, P(_nodes[a]), P(_nodes[b]));

        // packets travelling along links
        foreach (var packet in _packets)
        {
            var (a, b) = _edges[packet.Edge];
            var from = P(_nodes[packet.Forward ? a : b]);
            var to = P(_nodes[packet.Forward ? b : a]);
            var pos = new Point(from.X + (to.X - from.X) * packet.T, from.Y + (to.Y - from.Y) * packet.T);
            dc.DrawEllipse(PacketBrush, null, pos, 3, 3);
        }

        // nodes (pulsing glow + solid core)
        foreach (var node in _nodes)
        {
            var centre = P(node);
            double pulse = 1 + 0.18 * Math.Sin(time * 2.2 + node.Phase);
            double glow = (node.IsGateway ? 26 : 17) * pulse;

            dc.DrawEllipse(node.IsGateway ? GatewayGlow : NodeGlow, null, centre, glow, glow);
            dc.DrawEllipse(node.IsGateway ? GatewayBrush : NodeBrush, null, centre, node.IsGateway ? 8 : 5, node.IsGateway ? 8 : 5);
        }

        // gateway label
        var gateway = _nodes[^1];
        var text = new FormattedText("GATEWAY", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            LabelFace, 11, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var gp = P(gateway);
        dc.DrawText(text, new Point(gp.X - text.Width / 2, gp.Y + 14));
    }
}
