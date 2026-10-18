using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Controls;

/// <summary>
/// Plays a <see cref="CutSimulation"/> over the plate: the cut trail grows behind the torch,
/// travel is traced faintly and parts the torch has not reached yet are drawn muted.
/// Display only; it never changes the plate.
/// </summary>
internal sealed class CutSimulationOverlay : IDisposable
{
    private const double ArcStepRadians = System.Math.PI / 36;

    // Wheel zooms stretch the cached trail until the wheel has been still this long.
    private static readonly TimeSpan ZoomSettle = TimeSpan.FromMilliseconds(150);

    private readonly PlateView view;
    private readonly Timer timer;
    private readonly Timer settleTimer;
    private readonly Stopwatch clock = new();
    private readonly Stopwatch sinceZoom = new();
    private CutSimulation simulation;
    private TimeSpan time;

    // The finished trail, rendered once at the scale it was built for, so panning and playback
    // only blit it and draw the moves since. Null when the whole timeline would be too large an
    // image (zoomed in), where each paint draws only the moves on screen.
    private Bitmap trail;
    private RectangleF trailRect;
    private float trailScale;
    private PointF trailPhase;
    private int trailCount;
    private float seenScale;

    public CutSimulationOverlay(PlateView view)
    {
        this.view = view;
        timer = new Timer { Interval = 30 };
        timer.Tick += (_, _) => Tick();
        settleTimer = new Timer { Interval = (int)ZoomSettle.TotalMilliseconds + 10 };
        settleTimer.Tick += (_, _) =>
        {
            settleTimer.Stop();
            view.Invalidate();
        };
    }

    /// <summary>Raised when the playhead, play state or timeline changes.</summary>
    public event EventHandler Changed;

    public CutSimulation Simulation => simulation;
    public bool IsActive => simulation != null;
    public bool IsPlaying => timer.Enabled;
    public double Speed { get; set; } = 10;

    public TimeSpan Time
    {
        get => time;
        set
        {
            if (simulation == null)
                return;
            time = value < TimeSpan.Zero ? TimeSpan.Zero : value > simulation.TotalTime ? simulation.TotalTime : value;
            view.Invalidate();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Shows <paramref name="value"/>, keeping the playhead where it was when possible.</summary>
    public void Load(CutSimulation value)
    {
        simulation = value;
        ResetTrail();
        Time = time;
        if (simulation == null)
            Pause();
        view.Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Play()
    {
        if (simulation == null || simulation.Steps.Count == 0)
            return;
        if (time >= simulation.TotalTime)
            Time = TimeSpan.Zero;
        clock.Restart();
        timer.Start();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (!timer.Enabled)
            return;
        timer.Stop();
        clock.Reset();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        timer.Stop();
        simulation = null;
        time = TimeSpan.Zero;
        ResetTrail();
        view.Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Tick()
    {
        var elapsed = clock.Elapsed;
        clock.Restart();
        Time = time + TimeSpan.FromTicks((long)(elapsed.Ticks * Speed));
        if (simulation == null || time >= simulation.TotalTime)
            Pause();
    }

    /// <summary>True for a part the torch has not started yet.</summary>
    public bool IsPartPending(int partIndex)
    {
        if (simulation == null)
            return false;
        var index = simulation.IndexAt(time);
        if (index >= simulation.Steps.Count)
            return false;
        if (index < 0)
            return true;
        var current = simulation.Steps[index];
        // Travel toward a part has not touched it yet.
        return partIndex > current.PartIndex
            || partIndex == current.PartIndex && current.Kind == CutSimulationStepKind.Rapid && !PartStarted(index);
    }

    private bool PartStarted(int index)
    {
        var part = simulation.Steps[index].PartIndex;
        for (var i = index - 1; i >= 0 && simulation.Steps[i].PartIndex == part; i--)
        {
            if (simulation.Steps[i].Kind != CutSimulationStepKind.Rapid)
                return true;
        }
        return false;
    }

    public void Draw(Graphics graphics)
    {
        if (simulation == null || simulation.Steps.Count == 0)
            return;

        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var dpi = view.DeviceDpi / 96f;
            var index = simulation.IndexAt(time);
            var finished = System.Math.Clamp(index, 0, simulation.Steps.Count);
            using var pens = new TrailPens(dpi);

            DrawTrail(graphics, pens, finished);

            if (index >= simulation.Steps.Count)
            {
                DrawHead(graphics, simulation.Steps[^1].End, null, 0, dpi);
                return;
            }

            var step = simulation.Steps[index];
            var fraction = simulation.FractionAt(index, time);
            var partial = Points(step, fraction);
            if (partial.Length > 1)
            {
                switch (step.Kind)
                {
                    case CutSimulationStepKind.Rapid:
                        graphics.DrawLines(pens.Rapid, partial);
                        break;
                    case CutSimulationStepKind.Scribe:
                        graphics.DrawLines(pens.Scribe, partial);
                        break;
                    case CutSimulationStepKind.Cut:
                        graphics.DrawLines(pens.Glow, partial);
                        graphics.DrawLines(pens.Cut, partial);
                        break;
                }
            }

            DrawHead(graphics, step.PointAt(fraction), step.Kind, fraction, dpi);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private void DrawTrail(Graphics graphics, TrailPens pens, int finished)
    {
        var scale = view.ViewScale;
        if (scale != seenScale)
        {
            seenScale = scale;
            sinceZoom.Restart();
            settleTimer.Stop();
            settleTimer.Start();
        }

        var visible = graphics.VisibleClipBounds;
        visible.Inflate(pens.Glow.Width, pens.Glow.Width);

        // Mid-zoom: stretch the old image rather than re-render on every wheel notch.
        if (trail != null && trailScale != scale && sinceZoom.Elapsed < ZoomSettle && finished >= trailCount)
        {
            var k = scale / trailScale;
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(trail, new RectangleF(trailRect.X * k, trailRect.Y * k, trailRect.Width * k, trailRect.Height * k));
            DrawSteps(graphics, pens, trailCount, finished, visible);
            return;
        }

        // Keep image pixels on device pixels so the blit is exact.
        PointF phase;
        using (var transform = graphics.Transform)
            phase = new PointF(Fraction(transform.OffsetX), Fraction(transform.OffsetY));

        if (trail == null || trailScale != scale || phase != trailPhase || finished < trailCount)
            RebuildTrail(scale, phase);

        if (trail == null)
        {
            DrawSteps(graphics, pens, 0, finished, visible);
            return;
        }

        if (finished > trailCount)
        {
            using var image = Graphics.FromImage(trail);
            image.SmoothingMode = SmoothingMode.AntiAlias;
            image.TranslateTransform(-trailRect.X, -trailRect.Y);
            DrawSteps(image, pens, trailCount, finished, null);
            trailCount = finished;
        }

        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(trail, trailRect, new RectangleF(0, 0, trail.Width, trail.Height), GraphicsUnit.Pixel);
    }

    private static float Fraction(float value) => value - (float)System.Math.Floor(value);

    private void RebuildTrail(float scale, PointF phase)
    {
        ResetTrail();
        trailScale = scale;
        trailPhase = phase;

        var bounds = RectangleF.Empty;
        foreach (var step in simulation.Steps)
        {
            if (step.Kind != CutSimulationStepKind.Pierce)
                bounds = bounds.IsEmpty ? StepBounds(step) : RectangleF.Union(bounds, StepBounds(step));
        }
        if (bounds.IsEmpty)
            return;

        // The view's pens are a few pixels wide; pad so the glow is not cut off at the edge.
        var pad = 12 * view.DeviceDpi / 96f;
        var x = (float)System.Math.Floor(bounds.Left - pad) - phase.X;
        var y = (float)System.Math.Floor(bounds.Top - pad) - phase.Y;
        var width = (int)System.Math.Ceiling(bounds.Right + pad - x);
        var height = (int)System.Math.Ceiling(bounds.Bottom + pad - y);
        var budget = System.Math.Max(2L * view.Width * view.Height, 2_000_000L);
        if (width <= 0 || height <= 0 || (long)width * height > budget)
            return;

        trail = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        trailRect = new RectangleF(x, y, width, height);
    }

    // Draws finished steps [from, to). With a clip, steps off it are skipped and straight
    // rapids are trimmed to it: a dashed pen's cost grows with the length it strokes.
    private void DrawSteps(Graphics graphics, TrailPens pens, int from, int to, RectangleF? clip)
    {
        using var cuts = new GraphicsPath();
        using var scribes = new GraphicsPath();
        using var travel = new GraphicsPath();
        int lastCut = -2, lastScribe = -2, lastTravel = -2;
        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.HighSpeed;

        for (var i = from; i < to; i++)
        {
            var step = simulation.Steps[i];
            if (step.Kind == CutSimulationStepKind.Pierce)
                continue;

            if (step.Kind == CutSimulationStepKind.Rapid && !step.IsArc)
            {
                var a = view.PointWorldToGraph(step.Start);
                var b = view.PointWorldToGraph(step.End);
                var skipped = 0f;
                if (clip is { } rect && !ClipLine(ref a, ref b, rect, out skipped))
                    continue;
                // Keep the dash pattern fixed to the line while it pans; DashStyle.Dash repeats every 4 widths.
                pens.Rapid.DashOffset = skipped / pens.Rapid.Width % 4;
                graphics.DrawLine(pens.Rapid, a, b);
                continue;
            }

            if (clip is { } visible && !StepBounds(step).IntersectsWith(visible))
                continue;

            switch (step.Kind)
            {
                case CutSimulationStepKind.Cut:
                    AddStep(cuts, step, i, ref lastCut);
                    break;
                case CutSimulationStepKind.Scribe:
                    AddStep(scribes, step, i, ref lastScribe);
                    break;
                default:
                    AddStep(travel, step, i, ref lastTravel);
                    break;
            }
        }

        pens.Rapid.DashOffset = 0;
        graphics.SmoothingMode = smoothing;
        if (travel.PointCount > 0)
            graphics.DrawPath(pens.Rapid, travel);
        if (cuts.PointCount > 0)
        {
            graphics.DrawPath(pens.Glow, cuts);
            graphics.DrawPath(pens.Cut, cuts);
        }
        if (scribes.PointCount > 0)
            graphics.DrawPath(pens.Scribe, scribes);
    }

    // Consecutive moves join into one figure; arcs go in as GDI+ arcs, which flatten to the size
    // they are drawn at.
    private void AddStep(GraphicsPath path, CutSimulationStep step, int index, ref int last)
    {
        if (last != index - 1)
            path.StartFigure();
        last = index;

        if (step.IsArc)
        {
            var center = view.PointWorldToGraph(step.Center);
            var radius = view.LengthWorldToGui(step.Radius);
            if (radius > 0.01f)
            {
                // Graph y points down, so world angles flip sign.
                path.AddArc(center.X - radius, center.Y - radius, radius * 2, radius * 2,
                    (float)-Angle.ToDegrees(step.StartAngle), (float)-Angle.ToDegrees(step.Sweep));
                return;
            }
        }
        path.AddLine(view.PointWorldToGraph(step.Start), view.PointWorldToGraph(step.End));
    }

    // Graph-space bounds; an arc's whole circle stands in for the arc.
    private RectangleF StepBounds(CutSimulationStep step)
    {
        if (step.IsArc)
        {
            var center = view.PointWorldToGraph(step.Center);
            var radius = view.LengthWorldToGui(step.Radius);
            return new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        }
        var a = view.PointWorldToGraph(step.Start);
        var b = view.PointWorldToGraph(step.End);
        return RectangleF.FromLTRB(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y), System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y));
    }

    // Liang-Barsky: trims a..b to the rectangle; skipped is the distance trimmed from a.
    private static bool ClipLine(ref PointF a, ref PointF b, RectangleF rect, out float skipped)
    {
        skipped = 0;
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float t0 = 0, t1 = 1;
        if (!Clip(-dx, a.X - rect.Left, ref t0, ref t1) || !Clip(dx, rect.Right - a.X, ref t0, ref t1)
            || !Clip(-dy, a.Y - rect.Top, ref t0, ref t1) || !Clip(dy, rect.Bottom - a.Y, ref t0, ref t1))
            return false;

        var start = a;
        a = new PointF(start.X + t0 * dx, start.Y + t0 * dy);
        b = new PointF(start.X + t1 * dx, start.Y + t1 * dy);
        skipped = t0 * (float)System.Math.Sqrt(dx * dx + dy * dy);
        return true;

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0)
                return q >= 0;
            var t = q / p;
            if (p < 0)
            {
                if (t > t1)
                    return false;
                if (t > t0)
                    t0 = t;
            }
            else
            {
                if (t < t0)
                    return false;
                if (t < t1)
                    t1 = t;
            }
            return true;
        }
    }

    private void DrawHead(Graphics graphics, Vector location, CutSimulationStepKind? kind, double fraction, float dpi)
    {
        var center = view.PointWorldToGraph(location);
        var color = kind switch
        {
            CutSimulationStepKind.Cut or CutSimulationStepKind.Pierce => Color.OrangeRed,
            CutSimulationStepKind.Scribe => Color.MediumOrchid,
            _ => Color.DodgerBlue,
        };

        if (kind == CutSimulationStepKind.Pierce)
        {
            var ring = (4 + 14 * (float)fraction) * dpi;
            using var piercePen = new Pen(Color.FromArgb((int)(255 * (1 - fraction)), Color.Gold), 2 * dpi);
            graphics.DrawEllipse(piercePen, center.X - ring, center.Y - ring, ring * 2, ring * 2);
        }

        if (kind is CutSimulationStepKind.Cut or CutSimulationStepKind.Pierce)
        {
            var glow = 9 * dpi;
            using var glowBrush = new SolidBrush(Color.FromArgb(90, Color.Orange));
            graphics.FillEllipse(glowBrush, center.X - glow, center.Y - glow, glow * 2, glow * 2);
        }

        var radius = 5 * dpi;
        using var halo = new Pen(Color.White, 3 * dpi);
        using var pen = new Pen(color, 1.5f * dpi);
        graphics.DrawEllipse(halo, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        graphics.DrawEllipse(pen, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        graphics.DrawLine(pen, center.X - radius * 1.8f, center.Y, center.X + radius * 1.8f, center.Y);
        graphics.DrawLine(pen, center.X, center.Y - radius * 1.8f, center.X, center.Y + radius * 1.8f);
    }

    private PointF[] Points(CutSimulationStep step, double fraction)
    {
        if (!step.IsArc)
            return new[] { view.PointWorldToGraph(step.Start), view.PointWorldToGraph(step.PointAt(fraction)) };

        var segments = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Abs(step.Sweep * fraction) / ArcStepRadians));
        var points = new List<PointF>(segments + 1);
        for (var i = 0; i <= segments; i++)
            points.Add(view.PointWorldToGraph(step.PointAt(fraction * i / segments)));
        return points.ToArray();
    }

    private void ResetTrail()
    {
        trail?.Dispose();
        trail = null;
        trailCount = 0;
    }

    public void Dispose()
    {
        timer.Dispose();
        settleTimer.Dispose();
        ResetTrail();
    }

    private sealed class TrailPens(float dpi) : IDisposable
    {
        public Pen Rapid { get; } = new(Color.FromArgb(150, 70, 150, 255), 1 * dpi) { DashStyle = DashStyle.Dash };
        // A pale casing under the orange core keeps the trail visible on red, orange and brown parts.
        public Pen Glow { get; } = new(Color.FromArgb(210, 255, 255, 255), 5 * dpi) { LineJoin = LineJoin.Round };
        public Pen Cut { get; } = new(Color.OrangeRed, 2 * dpi) { LineJoin = LineJoin.Round };
        public Pen Scribe { get; } = new(Color.MediumOrchid, 1.5f * dpi);

        public void Dispose()
        {
            Rapid.Dispose();
            Glow.Dispose();
            Cut.Dispose();
            Scribe.Dispose();
        }
    }
}
