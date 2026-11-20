#nullable enable
using Clipper2Lib;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Static, clearance-inflated rectangles prepared once per stock per solve. Kept out of
/// the part catalog and its spacing cache: clamp clearance is the stock's clamp clearance, not
/// part spacing, and two stocks at the same part spacing may have different clamps.
/// </summary>
internal sealed class ClampObstacles
{
    private readonly Orientation[] rectangles;
    private readonly NoFitCache nfps = new(0);

    public ClampObstacles(NestPlateStock stock)
    {
        rectangles = new Orientation[stock.ClampZones.Count];
        var clearance = stock.ClampClearance;
        for (var i = 0; i < rectangles.Length; i++)
        {
            var box = stock.ClampZones[i].Bounds;
            var (left, right) = EncloseOnGrid(box.Left - clearance, box.Right + clearance);
            var (bottom, top) = EncloseOnGrid(box.Bottom - clearance, box.Top + clearance);
            rectangles[i] = new Orientation
            {
                // Real part types are nonnegative; the private cache cannot alias them.
                TypeIndex = -1,
                Index = i,
                Rotation = 0,
                Tolerance = 0,
                MinX = left,
                MinY = bottom,
                MaxX = right,
                MaxY = top,
                Outline = new PathD
                {
                    new(left, bottom), new(right, bottom), new(right, top), new(left, top),
                },
            };
        }
    }

    // NoFitCache quantizes before adding its safety margin. Enclose the keep-out
    // on that existing lattice so a sub-grid rectangle cannot disappear first.
    private static (double Lower, double Upper) EncloseOnGrid(double lower, double upper)
    {
        var scale = System.Math.Pow(10, NoFitCache.Precision);
        var first = System.Math.Floor(lower * scale);
        // Positive dimensions can be smaller than a coordinate ulp (upper == lower).
        var last = System.Math.Max(System.Math.Ceiling(upper * scale), first + 1);
        return (first / scale, last / scale);
    }

    public int Count => rectangles.Length;

    /// <summary>The keep-out rectangle, clearance included: a part's outline must stay outside it.</summary>
    public PathD Outline(int index) => rectangles[index].Outline;

    // No additional physical spacing. NoFitCache retains the established arc/grid safety
    // margin and covered-hole certification for the moving orientation and rectangle.
    public Nfp Get(int index, Orientation moving) => nfps.Get(rectangles[index], moving);

    /// <summary>A keep-out this close to a work-area side lies on it.</summary>
    private const double EdgeReach = 0.02;

    /// <summary>
    /// <paramref name="area"/> with each side that carries clamps moved in just past their
    /// keep-outs, as a larger edge spacing would, and whether every clamp inside it lies on a side;
    /// when one does not, the area unchanged.
    /// </summary>
    public static (Box Area, bool OnEdges) ClearOfEdges(Box area, ClampObstacles? clamps)
    {
        var allOnEdges = true;
        double left = area.Left, right = area.Right, bottom = area.Bottom, top = area.Top;
        for (var i = 0; clamps != null && i < clamps.Count; i++)
        {
            var box = Clipper.GetBounds(clamps.Outline(i));
            if (box.right <= area.Left || box.left >= area.Right || box.bottom <= area.Bottom || box.top >= area.Top)
                continue; // Outside the work area: nothing to clear.
            if (box.left <= area.Left + EdgeReach)
                left = System.Math.Max(left, box.right);
            else if (box.right >= area.Right - EdgeReach)
                right = System.Math.Min(right, box.left);
            else if (box.top <= area.Bottom + EdgeReach) // Clipper RectD: top is the minimum Y.
                bottom = System.Math.Max(bottom, box.bottom);
            else if (box.bottom >= area.Top - EdgeReach)
                top = System.Math.Min(top, box.top);
            else
                allOnEdges = false;
        }
        if (!allOnEdges || right <= left || top <= bottom)
            return (area, false);
        return (new Box(left, bottom, right - left, top - bottom), true);
    }
}
