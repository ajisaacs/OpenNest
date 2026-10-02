using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.Tests.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests;

public class CutOffBoundedFillTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var strategy in PlateFillService.BuiltInStrategies)
            foreach (var axis in new[] { CutOffAxis.Vertical, CutOffAxis.Horizontal })
                foreach (var quadrant in new[] { 1, 3 })
                    foreach (var farSide in new[] { false, true })
                        yield return new object[] { strategy, axis, quadrant, farSide };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FillItemCannotCrossSelectedCutOffBoundary(
        string strategy, CutOffAxis axis, int quadrant, bool farSide)
    {
        var plate = new Plate(new Size(20, 20)) { Quadrant = quadrant, PartSpacing = 0.5 };
        var bounds = plate.WorkArea();
        var position = new Vector(bounds.Left + 10, bounds.Bottom + 10);
        plate.CutOffs.Add(new CutOff(position, axis));
        // Independent expected selection box: one side of the cutoff, with spacing.
        var area = axis == CutOffAxis.Vertical
            ? new Box(farSide ? position.X + 0.5 : bounds.Left, bounds.Bottom, 9.5, bounds.Width)
            : new Box(bounds.Left, farSide ? position.Y + 0.5 : bounds.Bottom, bounds.Length, 9.5);
        var drawing = new Drawing("square", TestDrawingFactory.Rectangle(3, 3));
        var parts = PlateFillService.FillItem(strategy, plate,
            new NestItem { Drawing = drawing }, area, null, CancellationToken.None);

        Assert.NotEmpty(parts);
        Assert.Empty(plate.Parts);
        Assert.All(parts, part =>
        {
            Assert.Same(drawing, part.BaseDrawing);
            var box = part.BoundingBox;
            Assert.True(box.Left >= area.Left - 1e-6 && box.Right <= area.Right + 1e-6);
            Assert.True(box.Bottom >= area.Bottom - 1e-6 && box.Top <= area.Top + 1e-6);
        });
    }
}
