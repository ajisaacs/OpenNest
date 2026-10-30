#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.FlatBar;

/// <summary>One part laid on a bar: its type, rotation and material box start along the bar.</summary>
internal readonly record struct BarPart(BarType Type, BarOrientation Orientation, double AxisStart, double AxisEnd);

/// <summary>A laid-out bar: its parts in cutting order and the material envelope they span.</summary>
internal sealed record BarSheet(BarStock Bar, IReadOnlyList<BarPart> Parts, Box? Envelope)
{
    public IEnumerable<(string PartId, double X, double Y, double Rotation)> Poses() =>
        Parts.Select(p =>
        {
            var (x, y) = Bar.Pose(p.Orientation, p.AxisStart);
            return (p.Type.Id, x, y, p.Orientation.Angle);
        });

    public Dictionary<int, int> Counts() =>
        Parts.GroupBy(p => p.Type.Index).ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>
/// Turns a packed bar into poses. Each part starts box-to-box after the previous one, then
/// slides back along the bar until its spacing-offset outline meets a part already laid
/// (the Compactor push the desktop uses), trying each shortest rotation and keeping the one
/// that ends soonest. Every slid pose is confirmed with the layout check's own clearance test
/// and backed off until it passes; the box-to-box start always passes, so the result is never
/// worse than plain box packing. Parts whose outline fills their box gain nothing and skip the
/// slide. The length a slide frees at the end of a run is refilled from the remaining demand,
/// lowest priority number first, then longest first.
/// </summary>
internal sealed class BarLayout
{
    private static readonly double[] BackOffs = { 0.0005, 0.002, 0.008, 0.032, 0.128 };
    private const int MaxSlideOrientations = 4;
    private const int GainRun = 3;

    /// <summary>Gain held back so float noise in later slides cannot overfill a packed run.</summary>
    private const double GainMargin = 1e-6;

    private readonly IReadOnlyList<BarType> types;
    private readonly Dictionary<int, Drawing> drawings = new();
    private readonly Dictionary<(string Stock, int Type), bool> boxy = new();
    private readonly Dictionary<(string Stock, int Type), double> gains = new();

    public BarLayout(IReadOnlyList<BarType> types) => this.types = types;

    public BarSheet Lay(BarStock bar, PackedBar packed, IReadOnlyList<int> remaining, CancellationToken token)
    {
        var available = remaining.ToArray();
        foreach (var item in packed.Items)
            available[item.Type]--;

        var laid = new List<(BarPart Part, Part Shape)>();
        for (var s = 0; s < bar.Segments.Count; s++)
        {
            var segment = bar.Segments[s];
            var cursor = segment.Start;
            var first = laid.Count;
            var items = s < packed.Segments.Count ? packed.Segments[s].Items : new List<BarItem>();
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                // Packing counts on each run nesting by its measured gain; a part that does not
                // fit after all returns to the demand for later bars.
                var placed = Place(bar, types[item.Type], segment, cursor, laid, first, requireFit: true);
                if (placed == null)
                {
                    available[item.Type]++;
                    continue;
                }
                laid.Add(placed.Value);
                cursor = placed.Value.Part.AxisEnd + bar.Spacing;
            }

            // Refill the length the slides freed.
            while (true)
            {
                token.ThrowIfCancellationRequested();
                (BarPart Part, Part Shape)? added = null;
                foreach (var type in RefillOrder(bar, available))
                {
                    if (bar.Lengths[type.Index] > segment.End - cursor + BarPacker.OverhangAllowance
                        && (laid.Count == first || Boxy(type, bar)))
                        continue;
                    added = Place(bar, type, segment, cursor, laid, first, requireFit: true);
                    if (added != null)
                        break;
                }
                if (added == null)
                    break;
                laid.Add(added.Value);
                available[added.Value.Part.Type.Index]--;
                cursor = added.Value.Part.AxisEnd + bar.Spacing;
            }
        }

        return new BarSheet(bar, laid.Select(l => l.Part).ToList(), Envelope(bar, laid.Select(l => l.Part)));
    }

    /// <summary>
    /// How much shorter each further copy of the part makes a run than box-to-box, averaged
    /// over a run of <see cref="GainRun"/> copies laid as <see cref="Lay"/> would lay them.
    /// Averaging over two joints keeps the figure safe for parts that only nest every other
    /// copy (one mitred end: turned pairs nest, square ends meet square ends). Zero for parts
    /// that fill their box.
    /// </summary>
    public double SelfGain(BarStock bar, BarType type)
    {
        if (gains.TryGetValue((bar.Stock.Id, type.Index), out var known))
            return known;
        var gain = 0.0;
        var length = bar.Lengths[type.Index];
        if (!double.IsNaN(length) && !Boxy(type, bar))
        {
            var boxRun = GainRun * length + (GainRun - 1) * bar.Spacing;
            var segment = new BarSegment(bar.AxisStart, bar.AxisStart + boxRun);
            var laid = new List<(BarPart Part, Part Shape)>();
            var cursor = segment.Start;
            for (var i = 0; i < GainRun; i++)
            {
                var next = Place(bar, type, segment, cursor, laid, 0, requireFit: i > 0);
                if (next == null)
                    break;
                laid.Add(next.Value);
                cursor = next.Value.Part.AxisEnd + bar.Spacing;
            }
            if (laid.Count == GainRun)
                gain = System.Math.Max(0, (segment.End - laid[^1].Part.AxisEnd) / (GainRun - 1) - GainMargin);
        }
        gains[(bar.Stock.Id, type.Index)] = gain;
        return gain;
    }

    /// <summary>Material envelope of laid parts, for salvage-credited cost.</summary>
    public static Box? Envelope(BarStock bar, IEnumerable<BarPart> parts)
    {
        var list = parts.ToList();
        if (list.Count == 0)
            return null;
        var start = list.Min(p => p.AxisStart);
        var end = list.Max(p => p.AxisEnd);
        var across = list.Max(p => bar.Across(p.Orientation));
        return bar.AlongX
            ? new Box(start, bar.CrossStart, end - start, across)
            : new Box(bar.CrossStart, start, across, end - start);
    }

    private IEnumerable<BarType> RefillOrder(BarStock bar, int[] available) =>
        types.Where(t => available[t.Index] > 0 && !double.IsNaN(bar.Lengths[t.Index]))
            .OrderBy(t => t.Priority)
            .ThenByDescending(t => bar.Lengths[t.Index])
            .ThenBy(t => t.Index);

    /// <summary>
    /// Places one part after <paramref name="cursor"/>, slid back as far as clearance allows.
    /// With <paramref name="requireFit"/>, returns null when no rotation ends inside the segment.
    /// </summary>
    private (BarPart Part, Part Shape)? Place(BarStock bar, BarType type, BarSegment segment, double cursor,
        List<(BarPart Part, Part Shape)> laid, int first, bool requireFit)
    {
        var limit = segment.End + BarPacker.OverhangAllowance;
        var length = bar.Lengths[type.Index];
        var boxStart = cursor;
        // An item packed at exactly the bar length (overhang allowance) starts at the segment start.
        if (bar.Along(bar.Fits[type.Index][0]) > length)
            boxStart = segment.Start;

        (BarPart Part, Part Shape)? best = null;
        var neighbors = laid.Skip(first).ToList();
        var slide = neighbors.Count > 0 && !(Boxy(type, bar) && neighbors.All(n => Boxy(n.Part.Type, bar)));
        foreach (var orientation in bar.Fits[type.Index].Take(slide ? MaxSlideOrientations : 1))
        {
            var start = boxStart;
            var shape = Shape(bar, type, orientation, start);
            if (slide)
            {
                var travel = Slide(bar, shape, neighbors, segment, start);
                if (travel > 0)
                {
                    start = Confirm(bar, type, orientation, start, travel, neighbors);
                    shape = Shape(bar, type, orientation, start);
                }
            }
            var end = start + bar.Along(orientation);
            if (end > limit && (requireFit || start > segment.Start))
                continue;
            if (best == null || end < best.Value.Part.AxisEnd - 1e-9)
                best = (new BarPart(type, orientation, start, end), shape);
        }
        return best;
    }

    /// <summary>How far the part can slide back before its offset outline meets a laid part.</summary>
    private static double Slide(BarStock bar, Part shape, List<(BarPart Part, Part Shape)> neighbors,
        BarSegment segment, double start)
    {
        var room = start - segment.Start;
        if (room <= 0)
            return 0;
        var direction = bar.AlongX ? new Vector(-1, 0) : new Vector(0, -1);
        var huge = new Box(-1e9, -1e9, 2e9, 2e9);
        var obstacles = neighbors.Select(n => n.Shape).ToList();
        var travel = Compactor.Push(new List<Part> { shape }, obstacles, huge, bar.Spacing, direction);
        return System.Math.Clamp(travel, 0, room);
    }

    /// <summary>
    /// Backs the slid start off until the layout check's clearance test passes against every
    /// laid part the new one can reach; falls back to the box-to-box start.
    /// </summary>
    private double Confirm(BarStock bar, BarType type, BarOrientation orientation, double boxStart, double travel,
        List<(BarPart Part, Part Shape)> neighbors)
    {
        if (type.Geometry == null)
            return boxStart;
        foreach (var backOff in BackOffs.Prepend(0))
        {
            var slid = travel - backOff;
            if (slid <= 0)
                break;
            var start = boxStart - slid;
            var pose = Placement(bar, type, orientation, start);
            var clear = neighbors
                .Where(n => n.Part.AxisEnd + bar.Spacing + 1e-6 > start)
                .All(n => n.Part.Type.Geometry != null && NestLayoutCheck.Clears(
                    type.Geometry, pose, n.Part.Type.Geometry,
                    Placement(bar, n.Part.Type, n.Part.Orientation, n.Part.AxisStart), bar.Spacing));
            if (clear)
                return start;
        }
        return boxStart;
    }

    private static NestJobPlacement Placement(BarStock bar, BarType type, BarOrientation orientation, double start)
    {
        var (x, y) = bar.Pose(orientation, start);
        return new NestJobPlacement(type.Id, 0, x, y, orientation.Angle);
    }

    private Part Shape(BarStock bar, BarType type, BarOrientation orientation, double start)
    {
        if (!drawings.TryGetValue(type.Index, out var drawing))
        {
            drawing = new Drawing(type.Id, DrawingJobMapper.ToProgram(type.Part.Geometry));
            drawings[type.Index] = drawing;
        }
        var (x, y) = bar.Pose(orientation, start);
        var part = new Part(drawing);
        part.Rotate(orientation.Angle);
        part.Location = new Vector(x, y);
        part.UpdateBounds();
        return part;
    }

    /// <summary>
    /// True when the part's outline fills its box at the bar's rotations: such parts already
    /// touch box-to-box, so sliding them cannot gain length.
    /// </summary>
    private bool Boxy(BarType type, BarStock bar)
    {
        if (boxy.TryGetValue((bar.Stock.Id, type.Index), out var known))
            return known;
        var fits = bar.Fits[type.Index];
        var value = type.Geometry != null && fits.Count > 0 && fits.All(o =>
            System.Math.Abs(System.Math.Abs(type.Geometry.Perimeter.Area()) - o.Width * o.Height)
                <= 1e-6 * o.Width * o.Height);
        boxy[(bar.Stock.Id, type.Index)] = value;
        return value;
    }
}
