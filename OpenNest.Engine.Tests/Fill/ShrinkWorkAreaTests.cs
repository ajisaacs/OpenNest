using OpenNest.Engine.Fill;
using OpenNest.Engine.Tests.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Fill;

public class ShrinkWorkAreaTests
{
    [Theory]
    [InlineData(ShrinkAxis.Length, 30, 60)]
    [InlineData(ShrinkAxis.Length, 60, 30)]
    [InlineData(ShrinkAxis.Width, 30, 60)]
    [InlineData(ShrinkAxis.Width, 60, 30)]
    public void EstimateStartBox_ShrinksOnlyRequestedAxis_WithinTranslatedRectangularRemnant(
        ShrinkAxis axis, double length, double width)
    {
        var item = new NestItem
        {
            Drawing = new Drawing("rectangle", TestDrawingFactory.Rectangle(4, 3)),
            Quantity = 2,
        };
        var box = new Box(80, 7, length, width);

        var estimate = ShrinkFiller.EstimateStartBox(item, box, 0.3, axis, 2);

        Assert.Equal(box.Location, estimate.Location);
        Assert.True(box.Contains(estimate), "A shrink estimate must never expand beyond its input remnant.");
        // ShrinkAxis follows the fill/trim direction: Length trims top, Width trims right.
        if (axis == ShrinkAxis.Length)
        {
            Assert.Equal(box.Length, estimate.Length);
            Assert.InRange(estimate.Width, double.Epsilon, box.Width - double.Epsilon);
        }
        else
        {
            Assert.Equal(box.Width, estimate.Width);
            Assert.InRange(estimate.Length, double.Epsilon, box.Length - double.Epsilon);
        }
    }
}
