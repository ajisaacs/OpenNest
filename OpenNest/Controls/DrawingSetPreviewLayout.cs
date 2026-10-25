using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Controls
{
    /// <summary>
    /// Packs a set's drawings into rows for <see cref="DrawingSetPreviewControl"/>, every one at
    /// the same scale and in its own orientation. Rows are filled tallest first (next-fit
    /// decreasing height) and each row's parts share a baseline with a label strip beneath it.
    /// The row width is chosen to give the largest fit scale in the view, and the label strip
    /// and minimum cell width are fixed in pixels, so they are solved together with that scale.
    /// </summary>
    internal static class DrawingSetPreviewLayout
    {
        private const int RowWidthCandidates = 48;
        private const int ScaleIterations = 4;

        /// <summary>Where one drawing lands: its bounding box's lower-left corner and its label cell, in world units (Y up).</summary>
        internal readonly record struct Placement(int Index, double PartX, double PartY, double CellX, double CellWidth, double LabelTop);

        internal sealed class Result
        {
            public IReadOnlyList<Placement> Placements { get; init; } = Array.Empty<Placement>();

            /// <summary>The whole layout spans X 0..Width and Y -Height..0.</summary>
            public double Width { get; init; }

            public double Height { get; init; }

            /// <summary>Pixels per world unit when the layout is fitted to the view.</summary>
            public double Scale { get; init; }

            /// <summary>Height of the label strip under each row, in world units.</summary>
            public double LabelHeight { get; init; }
        }

        /// <param name="sizes">Each drawing's bounding-box width (X) and height (Y).</param>
        /// <param name="viewWidth">Usable view width in pixels (the fit border already removed).</param>
        /// <param name="viewHeight">Usable view height in pixels.</param>
        /// <param name="labelPixels">Height of a label line in pixels.</param>
        /// <param name="minCellPixels">Narrowest cell in pixels, so a small part's label stays readable.</param>
        public static Result Arrange(IReadOnlyList<(double Width, double Height)> sizes, double viewWidth, double viewHeight,
            double labelPixels, double minCellPixels)
        {
            if (sizes == null)
                throw new ArgumentNullException(nameof(sizes));
            if (sizes.Count == 0)
                return new Result();

            viewWidth = System.Math.Max(viewWidth, 1);
            viewHeight = System.Math.Max(viewHeight, 1);
            var clean = sizes.Select(s => (Width: Positive(s.Width), Height: Positive(s.Height))).ToArray();
            var largest = clean.Max(s => System.Math.Max(s.Width, s.Height));
            var spacing = largest * 0.04;
            var order = Enumerable.Range(0, clean.Length)
                .OrderByDescending(i => clean[i].Height)
                .ThenBy(i => i)
                .ToArray();

            // Any row narrower than the widest part (plus its spacing) cannot hold it; wider
            // than every part side by side is one row. Candidates are spread geometrically between.
            var narrowest = clean.Max(s => s.Width);
            var widest = clean.Sum(s => s.Width) + spacing * clean.Length;
            Result best = null;
            for (var c = 0; c <= RowWidthCandidates; c++)
            {
                var rowWidth = narrowest * System.Math.Pow(widest / narrowest, (double)c / RowWidthCandidates);
                var scale = System.Math.Min(viewWidth / widest, viewHeight / clean.Sum(s => s.Height));
                Result result = null;
                for (var i = 0; i < ScaleIterations; i++)
                {
                    result = Pack(clean, order, rowWidth, spacing, labelPixels / scale, minCellPixels / scale);
                    scale = System.Math.Min(viewWidth / result.Width, viewHeight / result.Height);
                }

                // The label strip was sized for the previous scale; repack once at the final one
                // so the reported scale and the strip agree.
                result = Pack(clean, order, rowWidth, spacing, labelPixels / scale, minCellPixels / scale);
                result = new Result
                {
                    Placements = result.Placements,
                    Width = result.Width,
                    Height = result.Height,
                    LabelHeight = result.LabelHeight,
                    Scale = System.Math.Min(viewWidth / result.Width, viewHeight / result.Height),
                };
                if (best == null || result.Scale > best.Scale)
                    best = result;
            }

            return best;
        }

        private static Result Pack((double Width, double Height)[] sizes, int[] order, double rowWidth, double spacing,
            double labelHeight, double minCell)
        {
            var placements = new Placement[sizes.Length];
            var rowTop = 0.0;
            var width = 0.0;
            var start = 0;
            while (start < order.Length)
            {
                // Next-fit: take parts while they fit the row; a row always takes at least one.
                var x = 0.0;
                var end = start;
                while (end < order.Length)
                {
                    var cell = System.Math.Max(sizes[order[end]].Width, minCell);
                    if (end > start && x + cell > rowWidth)
                        break;
                    x += cell + spacing;
                    end++;
                }

                // Order is tallest first, so the row's first part sets its height.
                var rowHeight = sizes[order[start]].Height;
                var baseline = -(rowTop + rowHeight);
                x = 0.0;
                for (var k = start; k < end; k++)
                {
                    var index = order[k];
                    var size = sizes[index];
                    var cell = System.Math.Max(size.Width, minCell);
                    placements[index] = new Placement(index, x + (cell - size.Width) / 2, baseline, x, cell, baseline);
                    x += cell + spacing;
                }

                width = System.Math.Max(width, x - spacing);
                rowTop += rowHeight + labelHeight + spacing;
                start = end;
            }

            return new Result
            {
                Placements = placements,
                Width = width,
                Height = rowTop - spacing,
                LabelHeight = labelHeight,
            };
        }

        private static double Positive(double value) =>
            double.IsFinite(value) && value > 0 ? value : 1e-6;
    }
}
