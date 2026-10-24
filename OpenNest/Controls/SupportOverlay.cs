using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.Controls
{
    /// <summary>
    /// View &gt; Draw Support: the plate's support grid or slats and, for each part, the contacts it
    /// rests on and its center of gravity, green where the bed holds it level and red where it can
    /// tip; a red part's best tab point is marked with an orange diamond. Display only: it
    /// takes part in no hit test, selection or sequencing. Analyses are cached per part until
    /// its program, pose, the bed or the quadrant changes.
    /// </summary>
    internal sealed class SupportOverlay
    {
        // Grid points are drawn only while they are at least this far apart on screen.
        private const float MinimumGridSpacing = 6f;

        // Grid points are this many whole pixels square.
        private const float GridPointSize = 3f;

        private static readonly Color GridColor = Color.FromArgb(110, 120, 120, 120);
        private static readonly Color SlatColor = Color.FromArgb(90, 120, 120, 120);
        private static readonly Color ContactColor = Color.FromArgb(220, 30, 150, 60);
        private static readonly Color SupportedColor = Color.FromArgb(230, 20, 140, 50);
        private static readonly Color TippingColor = Color.FromArgb(240, 210, 30, 30);
        private static readonly Color TabColor = Color.FromArgb(240, 240, 140, 0);

        private readonly ConditionalWeakTable<Part, Entry> cache = new();

        private sealed record Entry(CNC.Program Program, Vector Location, double Rotation, SupportBed Bed, int Quadrant,
            double Clearance, PartSupport Support);

        public void Draw(Graphics g, PlateView view)
        {
            var plate = view.DisplayPlate;
            if (plate?.SupportBed is not { } bed)
                return;

            if (bed is SupportGrid grid)
                DrawGrid(g, view, plate, grid);
            else if (bed is SupportSlats slats)
                DrawSlats(g, view, plate, slats);

            using var contactBrush = new SolidBrush(ContactColor);
            using var supportedPen = new Pen(SupportedColor, 2f);
            using var tippingPen = new Pen(TippingColor, 2f);
            using var tabBrush = new SolidBrush(TabColor);
            // Tab points keep clear of inside corners as Plan Cutting's do.
            var clearance = SupportAnalyzer.InsideCornerClearance(plate.CuttingParameters?.TabConfig?.Size ?? 0);
            foreach (var part in plate.Parts)
            {
                if (part?.BaseDrawing == null || part.BaseDrawing.IsCutOff)
                    continue;
                var support = Analyze(part, bed, plate.Quadrant, clearance);
                if (!support.IsComplete)
                    continue;

                foreach (var contact in support.Contacts)
                    Dot(g, view, contact, 2.5f, contactBrush);
                Cross(g, view, support.CenterOfGravity, support.IsSupported ? supportedPen : tippingPen);
                if (!support.IsSupported && support.BestTabPoint is { } tab)
                    Diamond(g, view, tab, tabBrush);
            }
        }

        private PartSupport Analyze(Part part, SupportBed bed, int quadrant, double clearance)
        {
            if (cache.TryGetValue(part, out var entry) && ReferenceEquals(entry.Program, part.Program)
                && entry.Location == part.Location && entry.Rotation == part.Rotation && Equals(entry.Bed, bed)
                && entry.Quadrant == quadrant && entry.Clearance == clearance)
                return entry.Support;
            var support = SupportAnalyzer.Analyze(bed, quadrant, part, clearance);
            cache.AddOrUpdate(part, new Entry(part.Program, part.Location, part.Rotation, bed, quadrant, clearance,
                support));
            return support;
        }

        private static Box Sheet(Plate plate)
        {
            var left = plate.Quadrant is 2 or 3 ? -plate.Size.Length : 0;
            var bottom = plate.Quadrant is 3 or 4 ? -plate.Size.Width : 0;
            return new Box(left, bottom, plate.Size.Length, plate.Size.Width);
        }

        // Each slat a strip cut off to the sheet, at least a pixel wide however far out the view is.
        private static void DrawSlats(Graphics g, PlateView view, Plate plate, SupportSlats slats)
        {
            if (view.LengthWorldToGui(slats.Pitch) < MinimumGridSpacing)
                return;
            using var brush = new SolidBrush(SlatColor);
            const float minimum = 1f;
            foreach (var slat in slats.SlatsIn(Sheet(plate), plate.Quadrant))
            {
                var a = view.PointWorldToGraph(new Vector(slat.Left, slat.Top));
                var b = view.PointWorldToGraph(new Vector(slat.Right, slat.Bottom));
                var x = System.Math.Min(a.X, b.X);
                var y = System.Math.Min(a.Y, b.Y);
                var width = System.Math.Abs(b.X - a.X);
                var height = System.Math.Abs(b.Y - a.Y);
                if (width < minimum)
                {
                    x -= (minimum - width) / 2;
                    width = minimum;
                }
                if (height < minimum)
                {
                    y -= (minimum - height) / 2;
                    height = minimum;
                }
                g.FillRectangle(brush, x, y, width, height);
            }
        }

        private static void DrawGrid(Graphics g, PlateView view, Plate plate, SupportGrid grid)
        {
            if (view.LengthWorldToGui(System.Math.Min(grid.PitchX, grid.PitchY)) < MinimumGridSpacing)
                return;
            var sheet = Sheet(plate);
            using var brush = new SolidBrush(GridColor);
            // Anti-aliased dots smear differently at each subpixel offset; whole-pixel squares
            // drawn without smoothing are all the same size.
            // The view is translated by a fractional origin, so snap in device pixels.
            using var transform = g.Transform;
            var dx = transform.OffsetX;
            var dy = transform.OffsetY;
            var half = (GridPointSize - 1) / 2;
            var smoothing = g.SmoothingMode;
            var offset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            try
            {
                foreach (var point in grid.PointsIn(sheet, plate.Quadrant))
                {
                    var p = view.PointWorldToGraph(point);
                    var x = (float)System.Math.Round(p.X + dx) - dx - half;
                    var y = (float)System.Math.Round(p.Y + dy) - dy - half;
                    g.FillRectangle(brush, x, y, GridPointSize, GridPointSize);
                }
            }
            finally
            {
                g.SmoothingMode = smoothing;
                g.PixelOffsetMode = offset;
            }
        }

        private static void Dot(Graphics g, PlateView view, Vector point, float radius, Brush brush)
        {
            var p = view.PointWorldToGraph(point);
            g.FillEllipse(brush, p.X - radius, p.Y - radius, radius * 2, radius * 2);
        }

        private static void Cross(Graphics g, PlateView view, Vector point, Pen pen)
        {
            const float size = 6f;
            var p = view.PointWorldToGraph(point);
            g.DrawEllipse(pen, p.X - size, p.Y - size, size * 2, size * 2);
            g.DrawLine(pen, p.X - size, p.Y, p.X + size, p.Y);
            g.DrawLine(pen, p.X, p.Y - size, p.X, p.Y + size);
        }

        private static void Diamond(Graphics g, PlateView view, Vector point, Brush brush)
        {
            const float size = 5f;
            var p = view.PointWorldToGraph(point);
            g.FillPolygon(brush, new[]
            {
                new PointF(p.X, p.Y - size), new PointF(p.X + size, p.Y),
                new PointF(p.X, p.Y + size), new PointF(p.X - size, p.Y),
            });
        }
    }
}
