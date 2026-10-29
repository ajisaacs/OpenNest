#nullable enable
using Clipper2Lib;
using OpenNest.Engine.Jobs;

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

    // No additional physical spacing. NoFitCache retains the established arc/grid safety
    // margin and covered-hole certification for the moving orientation and rectangle.
    public Nfp Get(int index, Orientation moving) => nfps.Get(rectangles[index], moving);
}
