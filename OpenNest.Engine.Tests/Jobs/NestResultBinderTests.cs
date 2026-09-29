using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestResultBinderTests
{
    [Theory]
    [InlineData("ghost", 1, 1, 0)]
    [InlineData(null, 1, 1, 0)]
    [InlineData("p", double.NaN, 1, 0)]
    [InlineData("p", 1, double.PositiveInfinity, 0)]
    [InlineData("p", 1, 1, double.NaN)]
    public void MalformedSheetCannotBePartiallyBound(string? partId, double x, double y, double rotation)
    {
        var drawing = new Drawing("bracket", TestDrawingFactory.Rectangle());
        var before = PartGeometrySnapshot.FromProgram(drawing.Program).Motions;
        var sheet = new NestJobPlateResult(0, new NestPlateStock("sheet", new Size(48, 96)),
            new[]
            {
                new NestJobPlacement("p", 0, 1, 1, 0),
                new NestJobPlacement(partId!, 1, x, y, rotation),
            });
        var drawings = new Dictionary<string, Drawing> { ["p"] = drawing };

        Assert.Throws<ArgumentException>(() => NestResultBinder.Bind(sheet, drawings));

        Assert.Equal(before, PartGeometrySnapshot.FromProgram(drawing.Program).Motions);
        Assert.Equal(0, drawing.Quantity.Nested);
    }
}
