using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Controls
{
    /// <summary>
    /// Packs a set's drawings into rows for <see cref="DrawingSetPreviewControl"/>, every one at
    /// the same scale and in its own orientation. Rows are filled tallest first and each row's
    /// parts share a baseline with a label strip beneath it. The label strip and minimum cell
    /// width are fixed in pixels, so the layout is solved for the largest scale that fits the view.
    /// </summary>
    /// <remarks>
    /// At a given scale, filling each row as far as the view's width allows beats any other split
    /// of the tallest-first order into rows: each row then starts no earlier, so it is no taller,
    /// and there are no more rows. A larger scale only widens cells, so the packed height grows
    /// with scale and the largest scale that fits is found by bisection.
    /// </remarks>
    internal static class DrawingSetPreviewLayout
    {
        private const int ScaleIterations = 60;

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
            labelPixels = System.Math.Max(labelPixels, 0);
            // A cell wider than the view could never be placed, so the floor gives way to the view.
            minCellPixels = System.Math.Clamp(minCellPixels, 0, viewWidth);
            var clean = sizes.Select(s => (Width: Positive(s.Width), Height: Positive(s.Height))).ToArray();
            var largest = clean.Max(s => System.Math.Max(s.Width, s.Height));
            var spacing = largest * 0.04;
            var order = Enumerable.Range(0, clean.Length)
                .OrderByDescending(i => clean[i].Height)
                .ThenBy(i => i)
                .ToArray();

            Result PackAt(double scale) =>
                Pack(clean, order, viewWidth / scale, spacing, labelPixels / scale, minCellPixels / scale);

            bool Fits(Result packed, double scale) =>
                packed.Width * scale <= viewWidth * (1 + 1e-9) && packed.Height * scale <= viewHeight * (1 + 1e-9);

            // No scale can exceed the one that fits the widest or the tallest part alone.
            var high = System.Math.Min(viewWidth / clean.Max(s => s.Width), viewHeight / clean.Max(s => s.Height));
            var low = high * 1e-9;
            var packed = PackAt(low);
            if (!Fits(packed, low))
            {
                // Even shrunk to nothing, the label strips of the rows overflow the view: keep
                // that packing and shrink it to fit, labels included.
                return WithScale(packed, System.Math.Min(viewWidth / packed.Width, viewHeight / packed.Height));
            }

            if (Fits(PackAt(high), high))
                low = high;
            for (var i = 0; i < ScaleIterations && high - low > low * 1e-9; i++)
            {
                var scale = (low + high) / 2;
                if (Fits(PackAt(scale), scale))
                    low = scale;
                else
                    high = scale;
            }

            return WithScale(PackAt(low), low);
        }

        private static Result WithScale(Result packed, double scale) => new()
        {
            Placements = packed.Placements,
            Width = packed.Width,
            Height = packed.Height,
            LabelHeight = packed.LabelHeight,
            Scale = scale,
        };

        private static Result Pack((double Width, double Height)[] sizes, int[] order, double rowWidth, double spacing,
            double labelHeight, double minCell)
        {
            var placements = new Placement[sizes.Length];
            var rowTop = 0.0;
            var width = 0.0;
            var start = 0;
            while (start < order.Length)
            {
                // Take parts while they fit the row; a row always takes at least one.
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
