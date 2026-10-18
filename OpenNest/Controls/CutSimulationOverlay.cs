using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Controls;

/// <summary>
/// Plays a <see cref="CutSimulation"/> over the plate: the cut trail grows behind the torch,
/// travel is traced faintly and parts the torch has not reached yet are drawn muted.
/// Display only; it never changes the plate.
/// </summary>
internal sealed class CutSimulationOverlay : IDisposable
{
    private const double ArcStepRadians = System.Math.PI / 36;

    private readonly PlateView view;
    private readonly Timer timer;
    private readonly Stopwatch clock = new();
    private CutSimulation simulation;
    private TimeSpan time;

    // Finished steps, cached in graph coordinates for the scale they were built at.
    private GraphicsPath cutTrail, scribeTrail, rapidTrail;
    private int trailCount;
    private float trailScale;

    public CutSimulationOverlay(PlateView view)
    {
        this.view = view;
        timer = new Timer { Interval = 30 };
        timer.Tick += (_, _) => Tick();
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
            UpdateTrail(index);

            using var rapidPen = new Pen(Color.FromArgb(150, 70, 150, 255), 1 * dpi) { DashStyle = DashStyle.Dash };
            using var cutGlow = new Pen(Color.FromArgb(70, 255, 140, 0), 6 * dpi) { LineJoin = LineJoin.Round };
            using var cutPen = new Pen(Color.OrangeRed, 2 * dpi) { LineJoin = LineJoin.Round };
            using var scribePen = new Pen(Color.MediumOrchid, 1.5f * dpi);

            graphics.DrawPath(rapidPen, rapidTrail);
            graphics.DrawPath(cutGlow, cutTrail);
            graphics.DrawPath(cutPen, cutTrail);
            graphics.DrawPath(scribePen, scribeTrail);

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
                        graphics.DrawLines(rapidPen, partial);
                        break;
                    case CutSimulationStepKind.Scribe:
                        graphics.DrawLines(scribePen, partial);
                        break;
                    case CutSimulationStepKind.Cut:
                        graphics.DrawLines(cutGlow, partial);
                        graphics.DrawLines(cutPen, partial);
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

    private void UpdateTrail(int index)
    {
        var finished = System.Math.Clamp(index, 0, simulation.Steps.Count);
        if (cutTrail == null || trailScale != view.ViewScale || finished < trailCount)
        {
            ResetTrail();
            cutTrail = new GraphicsPath();
            scribeTrail = new GraphicsPath();
            rapidTrail = new GraphicsPath();
            trailScale = view.ViewScale;
        }

        for (; trailCount < finished; trailCount++)
        {
            var step = simulation.Steps[trailCount];
            var path = step.Kind switch
            {
                CutSimulationStepKind.Cut => cutTrail,
                CutSimulationStepKind.Scribe => scribeTrail,
                CutSimulationStepKind.Rapid => rapidTrail,
                _ => null,
            };
            if (path == null)
                continue;
            var points = Points(step, 1);
            if (points.Length < 2)
                continue;
            path.StartFigure();
            path.AddLines(points);
        }
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
        cutTrail?.Dispose();
        scribeTrail?.Dispose();
        rapidTrail?.Dispose();
        cutTrail = scribeTrail = rapidTrail = null;
        trailCount = 0;
    }

    public void Dispose()
    {
        timer.Dispose();
        ResetTrail();
    }
}
