using System.Collections.Generic;
using OpenNest.Desktop.Rendering;
using OpenNest.Geometry;
using SkiaSharp;

namespace OpenNest.Avalonia.Rendering
{
    /// <summary>Converts the shared plate-view figures to Skia paths.</summary>
    internal static class SkiaFigures
    {
        public static SKPath ToPath(IEnumerable<PathFigure> figures, SKPathFillType fillType = SKPathFillType.EvenOdd)
        {
            var path = new SKPath { FillType = fillType };
            foreach (var figure in figures)
                Add(path, figure);
            return path;
        }

        public static void Add(SKPath path, PathFigure figure)
        {
            path.MoveTo((float)figure.Start.X, (float)figure.Start.Y);
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineTo line:
                        path.LineTo((float)line.End.X, (float)line.End.Y);
                        break;

                    case ArcTo arc:
                        {
                            var r = (float)arc.Radius;
                            var oval = new SKRect((float)arc.Center.X - r, (float)arc.Center.Y - r,
                                (float)arc.Center.X + r, (float)arc.Center.Y + r);
                            if (arc.IsFullCircle)
                            {
                                // Skia drops a whole-turn sweep; two halves keep the contour joined.
                                var half = (float)(arc.SweepAngle / 2);
                                path.ArcTo(oval, (float)arc.StartAngle, half, false);
                                path.ArcTo(oval, (float)arc.StartAngle + half, half, false);
                            }
                            else
                                path.ArcTo(oval, (float)arc.StartAngle, (float)arc.SweepAngle, false);
                            break;
                        }
                }
            }
            if (figure.IsClosed && figure.Segments.Count > 0)
                path.Close();
        }

        public static SKPath Polygons(IEnumerable<IReadOnlyList<Vector>> polygons)
        {
            var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var points in polygons)
            {
                if (points.Count == 0)
                    continue;
                path.MoveTo((float)points[0].X, (float)points[0].Y);
                for (var i = 1; i < points.Count; i++)
                    path.LineTo((float)points[i].X, (float)points[i].Y);
                path.Close();
            }
            return path;
        }
    }

    /// <summary>
    /// A part's shared display geometry as Skia paths in its program frame, before placement.
    /// Built on the UI thread and only read afterwards, so the render thread may draw it.
    /// </summary>
    internal sealed class PartPaths
    {
        private PartPaths(PartDisplayGeometry geometry)
        {
            Geometry = geometry;
            Cut = SkiaFigures.ToPath(geometry.Cut);
            Lead = SkiaFigures.ToPath(geometry.Lead);
            Etch = SkiaFigures.ToPath(geometry.Etch);
            Material = SkiaFigures.ToPath(geometry.Material);
            Display = geometry.Display.Count > 0 ? SkiaFigures.ToPath(geometry.Display) : null;
            Selection = geometry.Cut.Count > 0 ? Material : Etch;
            StrokeBounds = geometry.StrokeBounds();
        }

        public PartDisplayGeometry Geometry { get; }

        public SKPath Cut { get; }

        public SKPath Lead { get; }

        public SKPath Etch { get; }

        public SKPath Material { get; }

        /// <summary>Display-only geometry; null when the drawing has none.</summary>
        public SKPath Display { get; }

        /// <summary>What picks the part: its material, or an etch-only part's marks.</summary>
        public SKPath Selection { get; }

        /// <summary>The bounds of every stroke in the program frame; null when nothing is drawn.</summary>
        public Box StrokeBounds { get; }

        public static PartPaths Build(Part part) => new(PartDisplayGeometry.Build(part));
    }
}
