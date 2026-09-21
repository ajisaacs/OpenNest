using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;

namespace OpenNest.Engine.Tests.Jobs;

public class CandidatePlacementContextTests
{
    [Fact]
    public void FreshItemsReusePrivateDrawingsAndReflectCurrentRequirement()
    {
        var context = new CandidatePlacementContext();
        var geometry = PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(6, 4));

        var first = Assert.Single(
            context.CreateItems(
                new[]
                {
                    new NestJobPart(
                        "part",
                        geometry,
                        3,
                        priority: 7,
                        rotation: RotationPolicy.BoundedSweep(0.1, 0.7, 0.2)
                    ),
                }
            )
        );
        var current = Assert.Single(
            context.CreateItems(
                new[]
                {
                    new NestJobPart(
                        "part",
                        geometry,
                        1,
                        priority: 4,
                        rotation: RotationPolicy.Fixed(0.5)
                    ),
                }
            )
        );

        Assert.NotSame(first, current);
        Assert.Same(first.Drawing, current.Drawing);
        Assert.Equal(1, current.Quantity);
        Assert.Equal(4, current.Priority);
        Assert.Equal(OpenNest.Math.Angle.TwoPI, current.StepAngle);
        Assert.Equal(0.5, current.RotationStart);
        Assert.Equal(0.5, current.RotationEnd);
    }

    [Fact]
    public void PlacementMappingUsesPrivateDrawingIdentityAndRejectsInvalidParts()
    {
        var context = new CandidatePlacementContext();
        var items = context.CreateItems(
            new[]
            {
                new NestJobPart(
                    "part",
                    PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(6, 4)),
                    1
                ),
            }
        );
        var placed = new Part(items[0].Drawing, new Vector(7, 11));
        placed.Rotate(0.3);

        var placement = Assert.Single(context.MapPlacements(new[] { placed }));

        Assert.Equal("part", placement.PartId);
        Assert.Equal(0, placement.InstanceIndex);
        // The mapping carries the part's committed pose; Rotate moves both program and location.
        Assert.Equal(placed.Location.X, placement.X, 9);
        Assert.Equal(placed.Location.Y, placement.Y, 9);
        Assert.Equal(placed.Rotation, placement.Rotation, 9);
    }

    [Fact]
    public void NullPartIsRejected()
    {
        var context = new CandidatePlacementContext();
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = context.MapPlacements(new List<Part> { null! });
            }
        );
    }

    [Fact]
    public void ForeignDrawingWithKnownNameIsRejected()
    {
        var context = new CandidatePlacementContext();
        _ = context.CreateItems(
            new[]
            {
                new NestJobPart(
                    "part",
                    PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(6, 4)),
                    1
                ),
            }
        );
        // Same requirement name, but a foreign Drawing instance: identity is by reference.
        var foreign = new Part(new Drawing("part", TestDrawingFactory.Rectangle(6, 4)));
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = context.MapPlacements(new[] { foreign });
            }
        );
    }
}
