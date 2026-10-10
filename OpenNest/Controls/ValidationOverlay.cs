using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Controls;

/// <summary>Display-only findings, valid while the inspector keeps editing disabled.</summary>
internal sealed class ValidationOverlay : IDisposable
{
    private readonly PlateView view;
    private readonly Timer timer;
    private readonly Stopwatch elapsed = new();
    private PostVerificationFinding finding;
    private GraphicsPath areas;
    private float scale;

    public ValidationOverlay(PlateView view)
    {
        this.view = view;
        timer = new Timer { Interval = 30 };
        timer.Tick += (_, _) => view.Invalidate();
    }

    internal bool IsAnimating => timer.Enabled;
    internal PostVerificationFinding Finding => finding;

    public void Show(PostVerificationFinding value)
    {
        Clear();
        finding = value;
        var canAnimate = value?.Kind == PostVerificationKind.RapidCrossing
            ? value.RapidStart is { } start && IsFinite(start) && value.RapidEnd is { } end && IsFinite(end)
            : value?.Location is { } point && IsFinite(point);
        if (canAnimate)
        {
            elapsed.Restart();
            timer.Start();
        }
        view.Invalidate();
    }

    private static bool IsFinite(Vector point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    public void Clear()
    {
        timer.Stop();
        elapsed.Reset();
        finding = null;
        areas?.Dispose();
        areas = null;
        view.Invalidate();
    }

    public void Draw(Graphics graphics)
    {
        if (finding == null)
            return;
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (finding.Overlap is { } overlap)
            {
                if (areas == null || scale != view.ViewScale)
                {
                    areas?.Dispose();
                    areas = new GraphicsPath(FillMode.Winding);
                    foreach (var region in overlap.Regions)
                        areas.AddPolygon(region.Vertices.Select(view.PointWorldToGraph).ToArray());
                    scale = view.ViewScale;
                }
                using var brush = new SolidBrush(Color.FromArgb(100, 255, 0, 80));
                graphics.FillPath(brush, areas);
            }
            var dpi = view.DeviceDpi / 96f;
            if (finding.Kind == PostVerificationKind.RapidCrossing)
            {
                if (finding.RapidStart is { } start && IsFinite(start)
                    && finding.RapidEnd is { } end && IsFinite(end))
                {
                    var pulse = (1 + System.Math.Cos(elapsed.Elapsed.TotalSeconds * System.Math.PI * 2 / 1.2)) / 2;
                    using var rapid = new Pen(Color.FromArgb((int)(65 + 190 * pulse), Color.DarkOrange), 3 * dpi)
                    { DashStyle = DashStyle.Dash };
                    graphics.DrawLine(rapid, view.PointWorldToGraph(start), view.PointWorldToGraph(end));
                }
                using var contactHalo = new Pen(Color.White, 5 * dpi);
                using var contactPen = new Pen(Color.DeepPink, 2 * dpi);
                foreach (var contact in finding.ContactPoints.Where(IsFinite))
                {
                    var point = view.PointWorldToGraph(contact);
                    foreach (var pen in new[] { contactHalo, contactPen })
                    {
                        graphics.DrawEllipse(pen, point.X - 8 * dpi, point.Y - 8 * dpi, 16 * dpi, 16 * dpi);
                        graphics.DrawLine(pen, point.X - 12 * dpi, point.Y, point.X + 12 * dpi, point.Y);
                        graphics.DrawLine(pen, point.X, point.Y - 12 * dpi, point.X, point.Y + 12 * dpi);
                    }
                }
                return;
            }
            if (finding.Location is not { } location || !double.IsFinite(location.X) || !double.IsFinite(location.Y))
                return;
            var center = view.PointWorldToGraph(location);
            var phase = timer.Enabled ? (float)(elapsed.Elapsed.TotalSeconds % 0.8 / 0.8) : 0;
            var radius = (12 + 24 * phase) * dpi;
            var alpha = (int)(255 * (1 - phase));
            using var halo = new Pen(Color.FromArgb(alpha, Color.White), 5 * dpi);
            using var ring = new Pen(Color.FromArgb(alpha, Color.DeepPink), 3 * dpi);
            graphics.DrawEllipse(halo, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            graphics.DrawEllipse(ring, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            graphics.DrawLine(ring, center.X - 5 * dpi, center.Y, center.X + 5 * dpi, center.Y);
            graphics.DrawLine(ring, center.X, center.Y - 5 * dpi, center.X, center.Y + 5 * dpi);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    public void Dispose()
    {
        timer.Dispose();
        areas?.Dispose();
    }
}
