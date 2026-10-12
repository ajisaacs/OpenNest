#nullable enable
using Clipper2Lib;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Static, clearance-inflated rectangles prepared once per stock per solve. Kept out of
/// the part catalog and its spacing cache: clamp clearance is bottom edge spacing, not
/// part spacing, and two stocks at the same part spacing may have different clamps.
/// </summary>
internal sealed class ClampObstacles
{
    private readonly Orientation[] rectangles;
    private readonly NoFitCache nfps = new(0);

    public ClampObstacles(NestPlateStock stock)
    {
        rectangles = new Orientation[stock.ClampZones.Count];
        var clearance = System.Math.Max(0, stock.EdgeSpacing.Bottom);
        for (var i = 0; i < rectangles.Length; i++)
        {
            var box = stock.ClampZones[i].Bounds;
            var left = box.Left - clearance;
            var bottom = box.Bottom - clearance;
            var right = box.Right + clearance;
            var top = box.Top + clearance;
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

    public int Count => rectangles.Length;

    // No additional physical spacing. NoFitCache retains the established arc/grid safety
    // margin and covered-hole certification for the moving orientation and rectangle.
    public Nfp Get(int index, Orientation moving) => nfps.Get(rectangles[index], moving);
}
