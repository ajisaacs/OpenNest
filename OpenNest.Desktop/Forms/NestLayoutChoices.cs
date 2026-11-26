using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;

namespace OpenNest.Forms
{
    /// <summary>The sheet edge a layout leaves its free strip against.</summary>
    internal enum SheetEdge
    {
        Right,
        Left,
        Top,
        Bottom,
    }

    /// <summary>
    /// The free strip a sheet's parts leave: the distance from the parts to the sheet edge they
    /// were packed away from. Parts packed in vertical strips advance along the sheet's length
    /// and leave it against the right edge (left in quadrants 2 and 3); horizontal strips leave
    /// it against the top (bottom in quadrants 3 and 4). Of the two, the strip with more area is
    /// the one reported.
    /// </summary>
    internal readonly record struct SheetRemnant(SheetEdge Edge, double Distance, double Area)
    {
        /// <summary>True when the strip runs the sheet's full height (vertical strips).</summary>
        public bool AlongLength => Edge is SheetEdge.Right or SheetEdge.Left;

        /// <summary>Null for an empty sheet and for an outlined one, which has no such edge.</summary>
        public static SheetRemnant? Of(ProposedPlate plate)
        {
            var stock = plate?.Stock;
            if (stock == null || stock.Outline != null || plate.Parts.Count + plate.Occupants.Count == 0)
                return null;
            var parts = plate.Parts.Concat(plate.Occupants).ToList();
            var length = stock.Size.Length;
            var width = stock.Size.Width;
            // Sheet extents around the origin, as the plate view places them for this quadrant.
            var fromRight = stock.Quadrant is 2 or 3;
            var fromTop = stock.Quadrant is 3 or 4;
            var x = fromRight ? -length : 0;
            var y = fromTop ? -width : 0;
            var left = parts.Min(p => p.BoundingBox.Left);
            var right = parts.Max(p => p.BoundingBox.Right);
            var bottom = parts.Min(p => p.BoundingBox.Bottom);
            var top = parts.Max(p => p.BoundingBox.Top);
            var alongLength = System.Math.Max(0, fromRight ? left - x : x + length - right);
            var alongWidth = System.Math.Max(0, fromTop ? bottom - y : y + width - top);
            return alongLength * width >= alongWidth * length
                ? new SheetRemnant(fromRight ? SheetEdge.Left : SheetEdge.Right, alongLength, alongLength * width)
                : new SheetRemnant(fromTop ? SheetEdge.Bottom : SheetEdge.Top, alongWidth, alongWidth * length);
        }

        public override string ToString() => $"{Distance:0.##} to {EdgeName}";

        private string EdgeName => Edge switch
        {
            SheetEdge.Right => "right edge",
            SheetEdge.Left => "left edge",
            SheetEdge.Top => "top",
            _ => "bottom",
        };
    }

    /// <summary>
    /// One whole-job layout a user may choose: its sheets, what it places, the free strip its
    /// last sheet leaves, and how to turn it into a proposal.
    /// </summary>
    internal sealed class NestLayoutChoice
    {
        /// <param name="accept">Validates and binds the layout; null for a layout that can only be shown.</param>
        public NestLayoutChoice(IReadOnlyList<ProposedPlate> sheets,
            Func<CancellationToken, NestPipelineResult> accept = null)
        {
            Sheets = sheets ?? Array.Empty<ProposedPlate>();
            Accept = accept;
            SheetCount = Sheets.Sum(s => s.Quantity);
            PartCount = Sheets.Sum(s => s.Quantity * s.Parts.Count);
            SheetArea = Sheets.Sum(s => s.Quantity * s.Stock.Area);
            PartArea = Sheets.Sum(s => s.Quantity * s.Parts.Sum(p => p.BaseDrawing.Area));
            // The last sheet holds the leftovers, so its free strip is the usable drop.
            if (Sheets.Count > 0)
                Remnant = SheetRemnant.Of(Sheets[^1]);
        }

        public IReadOnlyList<ProposedPlate> Sheets { get; }
        public Func<CancellationToken, NestPipelineResult> Accept { get; }
        public int SheetCount { get; }
        public int PartCount { get; }
        public double SheetArea { get; }
        public double PartArea { get; }
        public double Utilization => SheetArea > 0 ? PartArea / SheetArea : 0;

        /// <summary>The free strip on the last sheet; null when it has none to measure.</summary>
        public SheetRemnant? Remnant { get; }

        /// <summary>"2 sheets · 130 parts · 29.9% used · 112.4 to right edge on sheet 2".</summary>
        public string Summary
        {
            get
            {
                var text = $"{SheetCount} {(SheetCount == 1 ? "sheet" : "sheets")} · {PartCount} parts · {Utilization:P1} used";
                if (Remnant is not { } remnant)
                    return text;
                return SheetCount == 1 ? $"{text} · {remnant}" : $"{text} · {remnant} on sheet {SheetCount}";
            }
        }

        /// <summary>
        /// Layouts alike enough that only the better one is offered: the same number of sheets
        /// of the same sizes, leaving the free strip the same way.
        /// </summary>
        internal (int Sheets, string Sizes, bool? AlongLength) Family => (SheetCount, sizes ??= SizeKey(), Remnant?.AlongLength);

        private string sizes;

        /// <summary>The sheet sizes with their counts, in a fixed order: "1x48x144" or "1x48x96|1x60x120".</summary>
        private string SizeKey() => string.Join("|", Sheets
            .GroupBy(s => FormattableString.Invariant($"{s.Stock.Size.Width:R}x{s.Stock.Size.Length:R}"), StringComparer.Ordinal)
            .Select(g => FormattableString.Invariant($"{g.Sum(s => s.Quantity)}x{g.Key}"))
            .OrderBy(k => k, StringComparer.Ordinal));

        /// <summary>True when both place the same parts at the same poses on the same stock.</summary>
        public bool SameLayoutAs(NestLayoutChoice other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other == null || Sheets.Count != other.Sheets.Count)
                return false;
            for (var i = 0; i < Sheets.Count; i++)
            {
                var (a, b) = (Sheets[i], other.Sheets[i]);
                if (a.Quantity != b.Quantity || a.Parts.Count != b.Parts.Count
                    || !string.Equals(a.Stock.Id, b.Stock.Id, StringComparison.Ordinal))
                    return false;
                for (var j = 0; j < a.Parts.Count; j++)
                {
                    var (p, q) = (a.Parts[j], b.Parts[j]);
                    if (!string.Equals(p.BaseDrawing.Name, q.BaseDrawing.Name, StringComparison.Ordinal)
                        || System.Math.Abs(p.Location.X - q.Location.X) > 1e-6
                        || System.Math.Abs(p.Location.Y - q.Location.Y) > 1e-6
                        || System.Math.Abs(p.Rotation - q.Rotation) > 1e-9)
                        return false;
                }
            }
            return true;
        }

        /// <summary>More parts placed, then fewer sheets, less sheet area, the larger free strip.</summary>
        internal bool IsBetterThan(NestLayoutChoice other)
        {
            if (PartCount != other.PartCount)
                return PartCount > other.PartCount;
            if (SheetCount != other.SheetCount)
                return SheetCount < other.SheetCount;
            if (System.Math.Abs(SheetArea - other.SheetArea) > 1e-6)
                return SheetArea < other.SheetArea;
            return (Remnant?.Area ?? 0) > (other.Remnant?.Area ?? 0);
        }
    }

    /// <summary>
    /// The layouts a run offers: the engine's best first, then the best layout of every other
    /// family (sheet count, sheet sizes and free-strip direction), so the list holds real alternatives rather
    /// than near copies of the best. Not thread-safe: use it from one thread.
    /// </summary>
    internal sealed class NestLayoutChoices
    {
        private readonly List<NestLayoutChoice> kept = new();
        private readonly int capacity;

        public NestLayoutChoices(int capacity = 3)
        {
            this.capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        /// <summary>The engine's best layout so far; null before the first.</summary>
        public NestLayoutChoice Best { get; private set; }

        /// <summary>The layouts offered, best first.</summary>
        public IReadOnlyList<NestLayoutChoice> Top { get; private set; } = Array.Empty<NestLayoutChoice>();

        /// <summary>
        /// Adds a layout. <paramref name="best"/> marks the engine's new best, which replaces
        /// every layout of its family; any other layout is kept only when its family has none
        /// yet or it beats the one kept. Returns whether <see cref="Top"/> changed.
        /// </summary>
        public bool Add(NestLayoutChoice choice, bool best)
        {
            ArgumentNullException.ThrowIfNull(choice);
            if (choice.Sheets.Count == 0)
                return false;
            if (best)
            {
                kept.RemoveAll(c => c.Family == choice.Family);
                kept.Add(choice);
                Best = choice;
            }
            else
            {
                var rival = kept.Find(c => c.Family == choice.Family);
                if (rival != null && (ReferenceEquals(rival, Best) || !choice.IsBetterThan(rival)))
                    return false;
                kept.Remove(rival);
                kept.Add(choice);
            }

            var top = new List<NestLayoutChoice>();
            if (Best != null)
                top.Add(Best);
            var others = kept.Where(c => !ReferenceEquals(c, Best)).ToList();
            others.Sort((a, b) => a.IsBetterThan(b) ? -1 : b.IsBetterThan(a) ? 1 : 0);
            top.AddRange(others.Take(capacity - top.Count));
            var changed = !top.SequenceEqual(Top);
            Top = top;
            return changed;
        }
    }

    /// <summary>A layout from a running solve; <see cref="Best"/> marks the engine's best so far.</summary>
    internal sealed record NestLayoutReport(NestLayoutChoice Layout, bool Best);
}
