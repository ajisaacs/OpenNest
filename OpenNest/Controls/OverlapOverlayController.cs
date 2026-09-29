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
using OpenNest.Geometry;

namespace OpenNest.Controls;

/// <summary>
/// UI-thread owner of manual overlap requests and their cached display geometry.
/// Workers see only owned analyzer snapshots, never the live plate or view.
/// </summary>
internal sealed class OverlapOverlayController : IDisposable
{
    private readonly PlateView view;
    private readonly OverlapReportState state = new();
    private ObservableList<Part> observedParts;
    private CancellationTokenSource cancellation;
    private GraphicsPath path;
    private PlateOverlapReport pathReport;
    private float pathScale;
    private Units capturedUnits;
    private IReadOnlyList<PlateOverlapPair> hoveredPairs = Array.Empty<PlateOverlapPair>();
    private string hoverText;
    private OverlapHoverPages hoverPages;
    private Point hoverPoint;
    private (float Scale, PointF Offset, int Dpi) hoverTransform;
    private bool disposed;

    public OverlapOverlayController(PlateView view) => this.view = view;

    public event EventHandler StateChanged;
    public OverlapCheckStatus Status => state.Status;
    public PlateOverlapReport Report => state.Report;
    public bool IsRunning => state.IsRunning;

    // Deterministic worker seam for STA lifecycle tests. Capture always stays on the UI thread.
    internal Func<PlateOverlapSnapshot, CancellationToken, PlateOverlapReport> Analyze { get; set; }
        = PlateOverlapAnalyzer.Analyze;
    internal GraphicsPath CachedPath => path;
    internal OverlapHoverPages HoverPages => hoverPages;
    internal RectangleF HoverBounds { get; private set; }

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
        ReleasePath();
        observedParts = plate?.Parts;
        if (observedParts != null)
        {
            observedParts.ItemAdded += PartAdded;
            observedParts.ItemRemoved += PartRemoved;
            observedParts.ItemChanged += PartChanged;
        }
        NotifyChanged();
    }

    public async Task CheckAsync(Units units)
    {
        if (!CanUseView || view.Plate == null)
            return;
        if (view.InvokeRequired)
            throw new InvalidOperationException("Overlap checks must be started on the UI thread.");

        CancelWorker();
        var plate = view.Plate;
        var generation = state.Begin(plate);
        capturedUnits = units; // Same request boundary as the owned geometry/names, never read live units in paint.
        var source = new CancellationTokenSource();
        cancellation = source;
        var token = source.Token;
        ReleasePath();
        NotifyChanged();
        try
        {
            // Do not include PreviewManager parts. Capture owns clean geometry and stable names.
            var snapshot = PlateOverlapAnalyzer.Capture(plate.Parts.ToArray(), token);
            var analyze = Analyze;
            var report = await Task.Run(() => analyze(snapshot, token), token);
            if (!CanUseView || generation != state.Generation || !ReferenceEquals(plate, view.Plate))
                return;
            if (token.IsCancellationRequested)
                state.Cancel();
            else if (!state.TryPublish(generation, view.Plate, report))
                CancelWorker(); // A pose/reference mismatch discovered at publication is stale.
            NotifyChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (CanUseView && generation == state.Generation)
            {
                state.Cancel();
                NotifyChanged();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Overlap check failed: {ex}");
            if (CanUseView && generation == state.Generation)
            {
                if (!state.TryFail(generation, view.Plate))
                    CancelWorker();
                NotifyChanged();
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
        NotifyChanged();
    }

    public void Invalidate()
    {
        var generation = state.Generation;
        state.Invalidate();
        if (generation == state.Generation)
            return;
        CancelWorker();
        ReleasePath();
        NotifyChanged();
    }

    public void ReleaseHandle()
    {
        CancelWorker();
        state.Cancel();
        state.Invalidate();
        ReleasePath();
        NotifyChanged();
    }

    private void CancelWorker()
    {
        // The awaiting request disposes its own source after the worker exits.
        var source = cancellation;
        cancellation = null;
        source?.Cancel();
    }

    private void NotifyChanged()
    {
        ClearHover();
        if (disposed || view.IsDisposed || view.Disposing)
            return;
        view.Status = state.Message;
        view.Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PartAdded(object sender, ItemAddedEventArgs<Part> e) => Invalidate();
    private void PartRemoved(object sender, ItemRemovedEventArgs<Part> e) => Invalidate();
    private void PartChanged(object sender, ItemChangedEventArgs<Part> e) => Invalidate();

    private void Unsubscribe()
    {
        if (observedParts == null)
            return;
        observedParts.ItemAdded -= PartAdded;
        observedParts.ItemRemoved -= PartRemoved;
        observedParts.ItemChanged -= PartChanged;
        observedParts = null;
    }

    public void Draw(Graphics graphics)
    {
        if (disposed)
            return;
        EnsureFresh();
        ValidateHoverTransform();

        if (state.Report != null)
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
        var fresh = state.EnsureFresh(view.Plate);
        if (generation != state.Generation)
        {
            CancelWorker();
            ReleasePath();
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
        foreach (var pair in state.Report.Pairs.OrderBy(pair => pair.PartAId).ThenBy(pair => pair.PartBId))
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
        if (ReferenceEquals(pathReport, state.Report) && pathScale == view.ViewScale && path != null)
            return;
        ReleasePath();
        var next = new GraphicsPath(FillMode.Winding);
        try
        {
            foreach (var pair in state.Report.Pairs)
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
            pathReport = state.Report;
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
            var text = state.Message + (state.DisplayMode == OverlapDisplayMode.Off ? " (display off)" : "");
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
        pathReport = null;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Unsubscribe();
        CancelWorker();
        state.Reset();
        ReleasePath();
        ClearHover();
    }
}
