using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.NestingEngines;

public static class JobBuilder
{
    public static NestJob Job(NestJobPart[] parts, NestPlateStock[] stock, NestJobOptions? options = null) =>
        new(parts, stock, options);

    public static NestJobPart Part(string id, Program program, int quantity,
        RotationPolicy? rotation = null, int priority = 0) =>
        new(id, PartGeometrySnapshot.FromProgram(program), quantity, priority, rotation);

    /// <param name="width">Y extent.</param>
    /// <param name="length">X extent.</param>
    public static NestPlateStock Stock(string id, double width, double length, double spacing = 0,
        Spacing edge = default, int quadrant = 1, int? quantity = null) =>
        new(id, new Size(width, length), quantity, spacing, edge, quadrant);

    public static NestJobPart Rectangle(string id, double w, double h, int count,
        RotationPolicy? rotation = null, double x = 0, double y = 0) =>
        Part(id, Shapes.Polyline((x, y), (x + w, y), (x + w, y + h), (x, y + h)),
            count, rotation ?? RotationPolicy.Fixed(0));
}
