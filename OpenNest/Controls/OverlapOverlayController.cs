using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Collections;
using OpenNest.Diagnostics;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.Controls;

/// <summary>
/// UI-thread owner of overlap requests and their cached display geometry.
/// Workers see only owned analyzer snapshots, never the live plate or view.
/// When auto-check is enabled, an unchecked or out-of-date layout is rechecked after it
/// has been quiet for <see cref="AutoCheckDelay"/> (see <see cref="OverlapAutoCheckScheduler"/>).
/// </summary>
internal sealed class OverlapOverlayController : IDisposable
{
    private readonly PlateView view;
    private readonly OverlapReportState state = new();
    private readonly OverlapAutoCheckScheduler autoCheck = new();
    // Prepared drawing material survives rechecks; only in-place program edits clear it.
    private readonly OverlapMaterialCache materialCache = new();
    // Last completed report: pairs of parts unchanged since then are reused, not clipped again.
    private PlateOverlapReport baseline;
    private readonly System.Windows.Forms.Timer autoCheckTimer;
    private Func<Units> autoCheckUnits;
    private ObservableList<Part> observedParts;
    private CancellationTokenSource cancellation;
    private GraphicsPath path;
    private IReadOnlyList<PlateOverlapPair> pathPairs;
    private float pathScale;
    private Units capturedUnits;
    private IReadOnlyList<PlateOverlapPair> hoveredPairs = Array.Empty<PlateOverlapPair>();
    private string hoverText;
    private OverlapHoverPages hoverPages;
    private Point hoverPoint;
    private (float Scale, PointF Offset, int Dpi) hoverTransform;
    private bool disposed;

    public OverlapOverlayController(PlateView view)
    {
        this.view = view;
        autoCheckTimer = new System.Windows.Forms.Timer { Interval = DefaultAutoCheckDelayMs };
        autoCheckTimer.Tick += AutoCheckTick;
    }

    public const int DefaultAutoCheckDelayMs = 500;

    public event EventHandler StateChanged;
    public OverlapCheckStatus Status => state.Status;
    public PlateOverlapReport Report => state.Report;
    public bool IsRunning => state.IsRunning;

    // Deterministic worker seam for STA lifecycle tests. Capture always stays on the UI thread.
    // Null runs the incremental analyzer against the last completed report.
    internal Func<PlateOverlapSnapshot, CancellationToken, PlateOverlapReport> Analyze { get; set; }
    internal GraphicsPath CachedPath => path;
    internal OverlapHoverPages HoverPages => hoverPages;
    internal RectangleF HoverBounds { get; private set; }
    internal bool IsAutoCheckEnabled => autoCheckUnits != null;
    internal bool IsAutoCheckScheduled => autoCheckTimer.Enabled;
    // Test seam for gestures that cannot be simulated off a real message loop.
    internal Func<bool> IsInteractionActive { get; set; } = DefaultInteractionActive;

    public int AutoCheckDelay
    {
        get => autoCheckTimer.Interval;
        set => autoCheckTimer.Interval = value;
    }

    public string Message => autoCheck.IsWaiting
        ? "Overlaps: check pending…"
        : state.Message;

    /// <summary>
    /// Recheck automatically after layout edits settle. Units are read when each request
    /// starts, like the manual command. Pass null to return to manual-only checks.
    /// </summary>
    public void SetAutoCheck(Func<Units> units)
    {
        autoCheckUnits = units;
        autoCheck.Reset();
        autoCheckTimer.Stop();
        NotifyChanged();
    }

    public OverlapDisplayMode DisplayMode
    {
        get => state.DisplayMode;
        set
        {
            if (state.DisplayMode == value)
                return;
            state.DisplayMode = value;
            NotifyChanged();
        }
    }

    public void SetPlate(Plate plate)
    {
        Unsubscribe();
        CancelWorker();
        state.Reset();
        autoCheck.Reset();
        baseline = null;
        ReleasePath();
        observedParts = plate?.Parts;
        if (observedParts != null)
        {
            observedParts.ItemAdded += PartAdded;
            observedParts.ItemRemoved += PartRemoved;
            observedParts.ItemChanged += PartChanged;
            observedParts.ItemsReordered += PartsReordered;
        }
        NotifyChanged();
    }

    public Task CheckAsync(Units units) => CheckAsync(units, automatic: false);

    // Automatic requests keep the display mode and report only on the canvas label; the
    // status bar keeps the user's last command result (for example a fill's timing).
    private async Task CheckAsync(Units units, bool automatic)
    {
        if (!CanUseView || view.Plate == null)
            return;
        if (view.InvokeRequired)
            throw new InvalidOperationException("Overlap checks must be started on the UI thread.");

        CancelWorker();
        var plate = view.Plate;
        var generation = state.Begin(plate, automatic);
        autoCheck.Started(plate);
        capturedUnits = units; // Same request boundary as the owned geometry/names, never read live units in paint.
        var source = new CancellationTokenSource();
        cancellation = source;
        var token = source.Token;
        NotifyChanged(toStatusBar: !automatic);
        try
        {
            // Do not include PreviewManager parts. Capture owns clean geometry and stable names.
            var snapshot = PlateOverlapAnalyzer.Capture(plate.Parts.ToArray(), materialCache, token);
            var previous = baseline;
            var analyze = Analyze ?? ((input, cancel) => PlateOverlapAnalyzer.Analyze(input, previous, cancel));
            var report = await Task.Run(() => analyze(snapshot, token), token);
            if (!CanUseView || generation != state.Generation || !ReferenceEquals(plate, view.Plate))
                return;
            if (token.IsCancellationRequested)
                state.Cancel();
            else if (state.TryPublish(generation, view.Plate, report))
                baseline = report;
            else
                CancelWorker(); // A pose/reference mismatch discovered at publication is stale.
            NotifyChanged(toStatusBar: !automatic);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (CanUseView && generation == state.Generation)
            {
                state.Cancel();
                NotifyChanged(toStatusBar: !automatic);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Overlap check failed: {ex}");
            if (CanUseView && generation == state.Generation)
            {
                if (!state.TryFail(generation, view.Plate))
                    CancelWorker();
                NotifyChanged(toStatusBar: !automatic);
            }
        }
        finally
        {
            if (ReferenceEquals(cancellation, source))
                cancellation = null;
            source.Dispose();
        }
    }

    private bool CanUseView => !disposed && !view.IsDisposed && !view.Disposing && view.IsHandleCreated;

    public void Cancel()
    {
        if (!state.IsRunning)
            return;
        CancelWorker();
        state.Cancel();
        ReleasePath();
        NotifyChanged(toStatusBar: true); // The user's Cancel command.
    }

    /// <summary>
    /// For editors that may mutate clean drawing programs in place: drops cached material
    /// as well as the current report. Call before loading the editor.
    /// </summary>
    public void InvalidateGeometry()
    {
        materialCache.Clear();
        baseline = null;
        state.Invalidate();
        CancelWorker();
        ReleasePath();
        NotifyChanged();
    }

    public void Invalidate()
    {
        var generation = state.Generation;
        var display = state.DisplayPairs;
        state.Invalidate(view.Plate);
        if (!ReferenceEquals(display, state.DisplayPairs))
        {
            ReleasePath();
            ClearHover();
        }
        if (generation == state.Generation)
        {
            // Already unchecked/stale: an edit restarts the quiet period. Bulk fills raise one
            // event per part, so only restart the timer here; the next paint or the timer's own
            // settle check refreshes the layout stamp.
            if (autoCheck.IsWaiting)
                RestartAutoCheckTimer();
            else
                UpdateAutoCheck(repaint: true);
            return;
        }
        CancelWorker();
        NotifyChanged();
    }

    public void ReleaseHandle()
    {
        CancelWorker();
        state.Cancel();
        state.Invalidate();
        ReleasePath();
        // Handle loss is not a user cancel: forget that request so the first paint after
        // recreation can schedule a recheck. A tick while the handle is gone only resets.
        autoCheck.Reset();
        autoCheckTimer.Stop();
        NotifyChanged();
    }

    private void CancelWorker()
    {
        // The awaiting request disposes its own source after the worker exits.
        var source = cancellation;
        cancellation = null;
        source?.Cancel();
    }

    private void NotifyChanged(bool toStatusBar = false)
    {
        if (!ReferenceEquals(pathPairs, state.DisplayPairs))
            ReleasePath();
        ClearHover();
        if (disposed || view.IsDisposed || view.Disposing)
            return;
        UpdateAutoCheck(repaint: false);
        // Without auto-check every change is reported as before.
        if (toStatusBar || autoCheckUnits == null)
            view.Status = Message;
        view.Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Restart the quiet period when the layout changed since it was last observed.
    /// <paramref name="repaint"/> redraws the label if its pending text changed; paint and
    /// NotifyChanged callers draw or invalidate themselves.
    /// </summary>
    private void UpdateAutoCheck(bool repaint)
    {
        if (autoCheckUnits == null || disposed)
            return;
        var wasWaiting = autoCheck.IsWaiting;
        if (autoCheck.Observe(view.Plate, state))
            RestartAutoCheckTimer();
        else if (!autoCheck.IsWaiting)
            autoCheckTimer.Stop();
        if (repaint && wasWaiting != autoCheck.IsWaiting && !view.IsDisposed && !view.Disposing)
            view.Invalidate();
    }

    private void RestartAutoCheckTimer()
    {
        autoCheckTimer.Stop();
        autoCheckTimer.Start();
    }

    private void AutoCheckTick(object sender, EventArgs e) => RunAutoCheckTimer();

    /// <summary>The timer's tick. Returns the started request, if any, for STA tests.</summary>
    internal Task RunAutoCheckTimer()
    {
        autoCheckTimer.Stop();
        if (autoCheckUnits == null || disposed)
            return Task.CompletedTask;
        if (!CanUseView)
        {
            autoCheck.Reset();
            return Task.CompletedTask;
        }
        var busy = view.IsFillInProgress || IsInteractionActive();
        switch (autoCheck.Elapsed(view.Plate, state, busy))
        {
            case OverlapAutoCheckStep.Wait:
                autoCheckTimer.Start();
                return Task.CompletedTask;
            case OverlapAutoCheckStep.Check:
                // CheckAsync reports its own failures through the state label.
                return CheckAsync(autoCheckUnits(), automatic: true);
            default:
                view.Invalidate(); // Pending text may have cleared.
                return Task.CompletedTask;
        }
    }

    // Do not check mid-gesture or while another operation owns the plate: dragging holds a
    // mouse button, fills/auto-nest show a progress window, and modal editors (for example
    // the drawing converter) may be mutating clean programs in place.
    private static bool DefaultInteractionActive() =>
        System.Windows.Forms.Control.MouseButtons != System.Windows.Forms.MouseButtons.None
        || System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>()
            .Any(form => form.Modal || form is NestProgressForm);

    private void PartAdded(object sender, ItemAddedEventArgs<Part> e) => Invalidate();
    private void PartRemoved(object sender, ItemRemovedEventArgs<Part> e) => Invalidate();
    private void PartChanged(object sender, ItemChangedEventArgs<Part> e) => Invalidate();
    private void PartsReordered(object sender, EventArgs e) => Invalidate();

    private void Unsubscribe()
    {
        if (observedParts == null)
            return;
        observedParts.ItemAdded -= PartAdded;
        observedParts.ItemRemoved -= PartRemoved;
        observedParts.ItemChanged -= PartChanged;
        observedParts.ItemsReordered -= PartsReordered;
        observedParts = null;
    }

    public void Draw(Graphics graphics)
    {
        if (disposed)
            return;
        EnsureFresh();
        UpdateAutoCheck(repaint: false); // Drags/nudges are only visible to the stamp; the label is drawn below.
        ValidateHoverTransform();

        if (state.DisplayPairs.Count > 0)
        {
            if (state.DisplayMode is OverlapDisplayMode.Areas or OverlapDisplayMode.Both)
            {
                EnsurePath();
                if (path.PointCount > 0)
                {
                    using var brush = new SolidBrush(Color.FromArgb(100, 255, 0, 80));
                    // A single winding fill is the union: triple overlap is not painted darker.
                    graphics.FillPath(brush, path);
                }
            }
            if (ShowsCentroids)
                DrawCentroids(graphics);
        }
        DrawStateLabel(graphics);
    }

    private bool EnsureFresh()
    {
        if (disposed)
            return false;
        var generation = state.Generation;
        var display = state.DisplayPairs;
        var fresh = state.EnsureFresh(view.Plate);
        if (!ReferenceEquals(display, state.DisplayPairs))
        {
            ReleasePath();
            ClearHover();
        }
        if (generation != state.Generation)
        {
            CancelWorker();
            NotifyChanged();
        }
        return fresh;
    }

    private bool ShowsCentroids => state.DisplayMode is OverlapDisplayMode.Centroids or OverlapDisplayMode.Both;
    private (float, PointF, int) ViewTransform => (view.ViewScale, view.PointControlToGraph(Point.Empty), view.DeviceDpi);

    internal string HoverText
    {
        get
        {
            if (!EnsureFresh())
                ClearHover();
            ValidateHoverTransform();
            return hoverText;
        }
    }

    public void UpdateHover(Point point)
    {
        ValidateHoverTransform();
        if (!EnsureFresh() || !ShowsCentroids || state.Report == null)
        {
            ClearHover();
            return;
        }

        // Graph coordinates are already screen-scaled; remove pan from the pointer once.
        var graphPoint = view.PointControlToGraph(point);
        var hits = OverlapPairPresentation.HitTest(state.Report.Pairs, world =>
        {
            var graph = view.PointWorldToGraph(world);
            return new Vector(graph.X, graph.Y);
        }, new Vector(graphPoint.X, graphPoint.Y), view.DeviceDpi);
        var changed = !hits.SequenceEqual(hoveredPairs);
        var moved = point != hoverPoint;
        hoveredPairs = hits;
        hoverPoint = point;
        hoverTransform = ViewTransform;
        if (changed)
        {
            hoverPages = null;
            HoverBounds = RectangleF.Empty;
            hoverText = hits.Count == 0 ? null : string.Join("\n\n",
                hits.Select(pair => OverlapPairPresentation.Details(pair, capturedUnits)));
        }
        if (changed || (moved && hoverText != null))
            view.Invalidate();
    }

    private void ValidateHoverTransform()
    {
        if (hoverText != null && hoverTransform != ViewTransform)
            ClearHover();
    }

    public void ClearHover()
    {
        var visible = hoverText != null;
        hoverText = null;
        hoverPages = null;
        HoverBounds = RectangleF.Empty;
        hoveredPairs = Array.Empty<PlateOverlapPair>();
        if (visible && !disposed && !view.IsDisposed && !view.Disposing)
            view.Invalidate();
    }

    public bool TryPageHover(int delta)
    {
        // Revalidate even when a key arrives before the next paint/mouse event.
        if (HoverText == null || hoverPages == null || hoverPages.PageCount < 2)
            return false;
        hoverPages.MovePage(delta);
        view.Invalidate();
        return true;
    }

    private void DrawCentroids(Graphics graphics)
    {
        var dpiScale = view.DeviceDpi / 96f;
        var radius = (float)OverlapPairPresentation.MarkerHalfSize(view.DeviceDpi);
        using var halo = new Pen(Color.White, 4 * dpiScale);
        using var crosshair = new Pen(Color.DarkRed, 2 * dpiScale);
        // Stack coincident pair labels instead of replacing them with a fragment count.
        var labelRows = new Dictionary<PointF, int>();
        foreach (var pair in state.DisplayPairs.OrderBy(pair => pair.PartAId).ThenBy(pair => pair.PartBId))
        {
            var center = view.PointWorldToGraph(pair.Centroid);
            if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
                continue;
            foreach (var pen in new[] { halo, crosshair })
            {
                graphics.DrawLine(pen, center.X - radius, center.Y, center.X + radius, center.Y);
                graphics.DrawLine(pen, center.X, center.Y - radius, center.X, center.Y + radius);
            }
            var label = OverlapPairPresentation.Label(pair);
            var size = graphics.MeasureString(label, view.Font);
            labelRows.TryGetValue(center, out var row);
            labelRows[center] = row + 1;
            var x = center.X + radius + 3 * dpiScale;
            var y = center.Y - radius + row * size.Height;
            graphics.FillRectangle(Brushes.White, x, y, size.Width, size.Height);
            graphics.DrawString(label, view.Font, Brushes.DarkRed, x, y);
        }
    }

    // Draw last, over action adorners. The normal part tooltip is suppressed only while
    // these details are actually visible; its independent timer/state is not stolen.
    public bool DrawHover(Graphics graphics)
    {
        var text = HoverText; // Includes a fresh stamp check even if no mouse move/paint preceded this.
        if (text == null)
            return false;
        var saved = graphics.Save();
        try
        {
            graphics.ResetTransform();
            graphics.SetClip(view.ClientRectangle, CombineMode.Intersect);
            var scale = view.DeviceDpi / 96f;
            var padding = 4 * scale;
            var maxWidth = System.Math.Min(560 * scale, view.ClientSize.Width - 2) - padding * 2;
            var lineHeight = (float)System.Math.Ceiling(view.Font.GetHeight(graphics));
            var maxRows = (int)((view.ClientSize.Height - 2 - padding * 2) / lineHeight);
            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            float Measure(string line) => graphics.MeasureString(line, view.Font, PointF.Empty, format).Width;
            hoverPages ??= OverlapHoverPages.Create(text, hoveredPairs.Count, maxRows, maxWidth, line => Measure(line));
            if (hoverPages.NeedsLargerViewport)
            {
                // A viewport smaller than a content line plus its paging hint cannot
                // show details legibly. Say so rather than presenting clipped data.
                HoverBounds = new RectangleF(0, 0, view.ClientSize.Width, view.ClientSize.Height);
                graphics.FillRectangle(Brushes.White, HoverBounds);
                graphics.DrawString("Enlarge view to read overlap details", view.Font, Brushes.DarkRed, HoverBounds);
                return true;
            }
            var lines = hoverPages.Lines.Concat(hoverPages.NavigationLines).ToArray();
            var width = lines.Max(Measure) + padding * 2;
            var height = lines.Length * lineHeight + padding * 2;
            var x = System.Math.Max(0, System.Math.Min(hoverPoint.X + 16 * scale, view.ClientSize.Width - width));
            var y = hoverPoint.Y - height - 6 * scale;
            if (y < 0)
                y = System.Math.Max(0, System.Math.Min(hoverPoint.Y + 20 * scale, view.ClientSize.Height - height));
            HoverBounds = new RectangleF(x, y, width, height);
            graphics.FillRectangle(Brushes.White, x, y, width, height);
            graphics.DrawRectangle(Pens.DimGray, x, y, width, height);
            for (var row = 0; row < lines.Length; row++)
                graphics.DrawString(lines[row], view.Font,
                    row < hoverPages.Lines.Count ? Brushes.Black : Brushes.DarkRed,
                    new PointF(x + padding, y + padding + row * lineHeight), format);
        }
        finally
        {
            graphics.Restore(saved);
        }
        return true;
    }

    private void EnsurePath()
    {
        if (ReferenceEquals(pathPairs, state.DisplayPairs) && pathScale == view.ViewScale && path != null)
            return;
        ReleasePath();
        var next = new GraphicsPath(FillMode.Winding);
        try
        {
            foreach (var pair in state.DisplayPairs)
            {
                foreach (var region in pair.Regions)
                {
                    var vertices = region.Vertices;
                    var points = new PointF[vertices.Count - 1]; // Analyzer repeats the closing vertex.
                    var signedArea = 0.0;
                    var start = vertices[0];
                    for (var i = 0; i < points.Length; i++)
                    {
                        var a = vertices[i] - start;
                        var b = vertices[i + 1] - start;
                        signedArea += a.X * b.Y - b.X * a.Y;
                        // The host has already translated by origin: graph, not control coordinates.
                        points[i] = view.PointWorldToGraph(vertices[i]);
                    }
                    if (signedArea < 0)
                        Array.Reverse(points);
                    next.AddPolygon(points);
                }
            }
            path = next;
            pathPairs = state.DisplayPairs;
            pathScale = view.ViewScale;
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    private void DrawStateLabel(Graphics graphics)
    {
        var saved = graphics.Save();
        try
        {
            graphics.ResetTransform();
            var text = Message + (state.DisplayMode == OverlapDisplayMode.Off ? " (display off)" : "");
            var size = graphics.MeasureString(text, view.Font);
            using var background = new SolidBrush(Color.FromArgb(235, Color.White));
            graphics.FillRectangle(background, 6, 6, size.Width + 8, size.Height + 6);
            graphics.DrawString(text, view.Font, Brushes.DarkRed, 10, 9);
        }
        finally
        {
            graphics.Restore(saved);
        }
    }

    private void ReleasePath()
    {
        path?.Dispose();
        path = null;
        pathPairs = null;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        autoCheckTimer.Stop();
        autoCheckTimer.Dispose();
        Unsubscribe();
        CancelWorker();
        state.Reset();
        ReleasePath();
        ClearHover();
    }
}
