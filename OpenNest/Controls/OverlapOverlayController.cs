using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Collections;
using OpenNest.Diagnostics;

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

    public async Task CheckAsync()
    {
        if (!CanUseView || view.Plate == null)
            return;
        if (view.InvokeRequired)
            throw new InvalidOperationException("Overlap checks must be started on the UI thread.");

        CancelWorker();
        var plate = view.Plate;
        var generation = state.Begin(plate);
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
        var generation = state.Generation;
        state.EnsureFresh(view.Plate);
        if (generation != state.Generation)
        {
            CancelWorker();
            ReleasePath();
            NotifyChanged();
        }

        if (state.DisplayMode == OverlapDisplayMode.Areas && state.Report != null)
        {
            EnsurePath();
            if (path.PointCount > 0)
            {
                using var brush = new SolidBrush(Color.FromArgb(100, 255, 0, 80));
                // A single winding fill is the union: triple overlap is not painted darker.
                graphics.FillPath(brush, path);
            }
        }
        DrawStateLabel(graphics);
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
    }
}
