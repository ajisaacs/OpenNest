using OpenNest.Bending;
using OpenNest.Breakoffs;
using OpenNest.Dimensions;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Math;

namespace OpenNest.Tests.Dimensions;

public class DimensionBuilderTests
{
    private static List<Entity> Rectangle() =>
    [
        new Line(1, 2, 11, 2), new Line(11, 2, 11, 7), new Line(11, 7, 1, 7), new Line(1, 7, 1, 2),
    ];

    private static DimensionLayout Horizontal(IEnumerable<Entity> entities, double offset = -1) =>
        DimensionBuilder.Layout(entities, new DrawingDimension { Kind = DimensionKind.OverallHorizontal, Offset = offset });

    private static DimensionLayout Vertical(IEnumerable<Entity> entities, double offset = 1) =>
        DimensionBuilder.Layout(entities, new DrawingDimension { Kind = DimensionKind.OverallVertical, Offset = offset });

    [Fact]
    public void Horizontal_MeasuresXAndSitsOnTheOffsetSide()
    {
        var below = Horizontal(Rectangle(), -1.5);

        Assert.True(below.IsValid, below.Error);
        Assert.Equal(10, below.Value, 9);
        Assert.Equal(new Vector(1, 2), below.ExtensionStart);
        Assert.Equal(new Vector(1, 0.5), below.LineStart);
        Assert.Equal(new Vector(11, 0.5), below.LineEnd);

        Assert.Equal(new Vector(11, 8.5), Horizontal(Rectangle(), 1.5).LineEnd);
    }

    [Fact]
    public void Vertical_MeasuresYAndSitsOnTheOffsetSide()
    {
        var right = Vertical(Rectangle(), 2);

        Assert.True(right.IsValid, right.Error);
        Assert.Equal(5, right.Value, 9);
        Assert.Equal(new Vector(11, 2), right.ExtensionStart);
        Assert.Equal(new Vector(13, 2), right.LineStart);
        Assert.Equal(new Vector(13, 7), right.LineEnd);

        Assert.Equal(new Vector(-1, 2), Vertical(Rectangle(), -2).LineStart);
    }

    [Fact]
    public void ValueText_HasThreeDecimals()
    {
        Assert.Equal("10.000", Horizontal(Rectangle()).ValueText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArcBulgingPastItsEndpoints_CountsTowardTheOverallSize(bool reversed)
    {
        // Upper half of a circle of radius 2 at the origin, both endpoints on Y = 0.
        var arc = reversed
            ? new Arc(Vector.Zero, 2, Angle.HalfPI * 2, 0, true)
            : new Arc(Vector.Zero, 2, 0, Angle.HalfPI * 2);
        var entities = new List<Entity> { arc, new Line(-2, 0, 2, 0) };

        Assert.Equal(2, Vertical(entities).Value, 9);
        Assert.Equal(4, Horizontal(entities).Value, 9);
    }

    [Fact]
    public void ArcCrossingZeroDegrees_CountsTowardTheOverallSize()
    {
        var arc = new Arc(Vector.Zero, 2, Angle.TwoPI - Angle.HalfPI / 2, Angle.HalfPI / 2);

        var box = DimensionBuilder.Extents([arc]);

        Assert.Equal(2, box.Right, 9);
    }

    [Theory]
    [InlineData("SCRIBE")]
    [InlineData("etch")]
    [InlineData("ENGRAVE")]
    public void Marks_DoNotCount(string layer)
    {
        var entities = Rectangle();
        entities.Add(new Line(0, 2, 20, 2) { Layer = new Layer(layer) });

        Assert.Equal(10, Horizontal(entities).Value, 9);
    }

    [Fact]
    public void SpecialLayerMarks_DoNotCount()
    {
        var entities = Rectangle();
        entities.Add(new Line(0, 2, 20, 2) { Layer = SpecialLayers.Scribe });
        entities.Add(new Line(0, 3, 30, 3) { Layer = SpecialLayers.Rapid });
        entities.Add(new Line(0, 4, 40, 4) { Layer = SpecialLayers.Break });

        Assert.Equal(10, Horizontal(entities).Value, 9);
    }

    [Fact]
    public void TabsCount_TheyBoundMaterial()
    {
        var entities = Rectangle();
        entities.Add(new Line(11, 3, 12, 3) { Layer = SpecialLayers.Tab });

        Assert.Equal(11, Horizontal(entities).Value, 9);
    }

    [Fact]
    public void AppliedBreakoff_StillMeasuresTheWholeOutline()
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 10), new Line(10, 10, 0, 10), new Line(0, 10, 0, 0),
            new Line(5, 0, 5, 10),
        };
        var breakoff = new Breakoff { EdgeIds = [entities[4].Id] };

        var applied = BreakoffBuilder.Apply(entities, [breakoff], out var errors);

        Assert.Empty(errors);
        Assert.Equal(10, Horizontal(applied).Value, 9);
        Assert.Equal(10, Vertical(applied).Value, 9);
    }

    [Fact]
    public void NoGeometry_IsNotValid()
    {
        var layout = Horizontal([]);

        Assert.False(layout.IsValid);
        Assert.Null(DimensionBuilder.Extents([]));
    }

    [Fact]
    public void OnlyMarks_IsNotValid()
    {
        Assert.False(Horizontal([new Line(0, 0, 5, 0) { Layer = SpecialLayers.Scribe }]).IsValid);
    }

    [Fact]
    public void AxisWithNoSize_IsNotValid()
    {
        Assert.False(Vertical([new Line(0, 0, 5, 0)]).IsValid);
        Assert.True(Horizontal([new Line(0, 0, 5, 0)]).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void UnusableOffset_IsNotValid(double offset)
    {
        Assert.False(Horizontal(Rectangle(), offset).IsValid);
    }

    [Fact]
    public void OffsetAt_FollowsThePointToEitherSide_AndKeepsClear()
    {
        var horizontal = Horizontal(Rectangle());

        Assert.Equal(3, horizontal.OffsetAt(new Vector(5, 10), 0.5), 9);
        Assert.Equal(-3, horizontal.OffsetAt(new Vector(5, -1), 0.5), 9);
        Assert.Equal(0.5, horizontal.OffsetAt(new Vector(5, 5), 0.5), 9);
        Assert.Equal(-0.5, horizontal.OffsetAt(new Vector(5, 3), 0.5), 9);

        var vertical = Vertical(Rectangle());

        Assert.Equal(4, vertical.OffsetAt(new Vector(15, 5), 0.5), 9);
        Assert.Equal(-4, vertical.OffsetAt(new Vector(-3, 5), 0.5), 9);
    }

    [Fact]
    public void MissingOverall_AddsOnlyWhatIsAbsent()
    {
        var both = DimensionBuilder.MissingOverall(Rectangle(), []);

        Assert.Equal(2, both.Count);
        Assert.Equal(-1, Assert.Single(both, d => d.Kind == DimensionKind.OverallHorizontal).Offset, 9);
        Assert.Equal(1, Assert.Single(both, d => d.Kind == DimensionKind.OverallVertical).Offset, 9);

        var one = DimensionBuilder.MissingOverall(Rectangle(), [both[0]]);
        Assert.Equal(both[1].Kind, Assert.Single(one).Kind);

        Assert.Empty(DimensionBuilder.MissingOverall(Rectangle(), both));
        Assert.Empty(DimensionBuilder.MissingOverall([], []));
    }

    [Fact]
    public void Dimensions_LeaveTheProgramUnchanged()
    {
        var entities = Rectangle();
        var result = new CadImportResult { Entities = entities, Name = "part" };
        var plain = CadImporter.BuildDrawing(result, entities, new List<Bend>(), 1, null, null);
        var dimensioned = CadImporter.BuildDrawing(result, entities, new List<Bend>(), 1, null, null,
            dimensions: [new DrawingDimension { Kind = DimensionKind.OverallHorizontal, Offset = -1 }]);

        Assert.Single(dimensioned.Dimensions);
        Assert.Equal(plain.Program.Codes.Select(c => c.ToString()), dimensioned.Program.Codes.Select(c => c.ToString()));
        Assert.Equal(plain.Source.Offset, dimensioned.Source.Offset);
    }

    [Fact]
    public void ApplyEdit_TakesTheEditedDimensions()
    {
        var entities = Rectangle();
        var result = new CadImportResult { Entities = entities, Name = "part" };
        var drawing = CadImporter.BuildDrawing(result, entities, new List<Bend>(), 1, null, null);
        var edited = CadImporter.BuildDrawing(result, entities, new List<Bend>(), 1, null, null,
            dimensions: [new DrawingDimension { Kind = DimensionKind.OverallVertical, Offset = 2 }]);

        CadImporter.ApplyEdit(drawing, edited);

        Assert.Equal(2, Assert.Single(drawing.Dimensions).Offset);
    }
}
