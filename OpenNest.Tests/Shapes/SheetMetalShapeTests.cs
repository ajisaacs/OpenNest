using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class SheetMetalShapeTests
{
    // 90° bend at 5/16 inside radius, 1/8 thick, K 0.42.
    private static readonly double Allowance = System.Math.PI / 2 * (0.3125 + 0.42 * 0.125);

    [Fact]
    public void Angle_BlankIsTheDevelopedWidthByTheLength()
    {
        var shape = new AngleShape();
        shape.SetPreviewDefaults();

        var box = shape.GetDrawing().Program.BoundingBox();

        // Legs' flats: 2 - 1/8 - 5/16 and 4 - 1/8 - 5/16.
        var expected = 1.5625 + 3.5625 + Allowance;
        Assert.Equal(expected, shape.FlatLength, 9);
        Assert.Equal(12, box.Length, 6);
        Assert.Equal(expected, box.Width, 6);
    }

    [Fact]
    public void Angle_BendsUpAcrossTheBlankFromTheShorterLeg()
    {
        var shape = new AngleShape();
        shape.SetPreviewDefaults();

        var bend = Assert.Single(shape.GetDrawing().Bends);

        var y = 1.5625 + Allowance / 2;
        Assert.Equal(y, shape.BendLineDim, 9);
        Assert.Equal(new Vector(0, y), bend.StartPoint);
        Assert.Equal(new Vector(12, y), bend.EndPoint);
        Assert.Equal(BendDirection.Up, bend.Direction);
        Assert.Equal(90, bend.Angle);
        Assert.Equal(0.3125, bend.Radius);
    }

    [Fact]
    public void Channel_BlankHasTwoBendsInFromEachSide()
    {
        var shape = new ChannelShape();
        shape.SetPreviewDefaults();

        var drawing = shape.GetDrawing();
        var box = drawing.Program.BoundingBox();

        // Sides' flats: 2 - 1/8 - 5/16 each; bottom: 4 - 2(1/8) - 2(5/16).
        var expected = 1.5625 * 2 + 3.125 + Allowance * 2;
        Assert.Equal(expected, shape.FlatLength, 9);
        Assert.Equal(expected, box.Width, 6);
        var lines = drawing.Bends.Select(b => b.StartPoint.Y).OrderBy(y => y).ToList();
        var inset = 1.5625 + Allowance / 2;
        Assert.Equal(2, lines.Count);
        Assert.Equal(inset, lines[0], 9);
        Assert.Equal(expected - inset, lines[1], 9);
    }

    [Fact]
    public void RolledPlate_BlankIsTheNeutralCircumference()
    {
        var shape = new RolledPlateShape { InsideDiameter = 10, Thickness = 0.25, KFactor = 0.5, Length = 24 };

        var drawing = shape.GetDrawing();
        var box = drawing.Program.BoundingBox();

        var expected = System.Math.PI * 2 * (5 + 0.125);
        Assert.Equal(expected, shape.FlatLength, 9);
        Assert.Equal(24, box.Length, 6);
        Assert.Equal(expected, box.Width, 6);
        Assert.Empty(drawing.Bends);
        Assert.Equal(10.5, shape.OutsideDiameter, 9);
    }

    [Fact]
    public void BendLines_AreEtchedOnTheBlank()
    {
        var shape = new ChannelShape();
        shape.SetPreviewDefaults();

        var etches = shape.GetDrawing().Program.Codes
            .OfType<LinearMove>()
            .Count(m => m.Layer == LayerType.Scribe);

        // Each 12" bend line gets a 1" dash at either end.
        Assert.Equal(4, etches);
    }

    [Fact]
    public void Angle_LegNoLongerThanThicknessPlusRadius_DoesNotMakeAShape()
    {
        var shape = new AngleShape { OutsideWidth = 4, OutsideHeight = 0.4, Thickness = 0.125, InsideRadius = 0.3125, KFactor = 0.42, Length = 12 };

        Assert.Throws<InvalidOperationException>(() => shape.GetDrawing());
    }

    [Fact]
    public void KFactorAboveOne_DoesNotMakeAShape()
    {
        var shape = new ChannelShape();
        shape.SetPreviewDefaults();
        shape.KFactor = 1.5;

        Assert.Throws<InvalidOperationException>(() => shape.GetDrawing());
    }

    [Fact]
    public void AngleSideView_IsOneClosedSectionOfTheFormedAngle()
    {
        var shape = new AngleShape();
        shape.SetPreviewDefaults();

        var section = Assert.Single(ShapeBuilder.GetShapes(shape.GetSideView().Entities));

        // Two legs' flats and a quarter annulus at the bend.
        const double t = 0.125, ir = 0.3125, or = ir + t;
        var expected = t * (4 - or) + t * (2 - or) + System.Math.PI / 4 * (or * or - ir * ir);
        Assert.True(section.IsClosed());
        Assert.Equal(expected, section.Area(), 1e-4);
        var box = section.BoundingBox;
        Assert.Equal(4, box.Length, 6);
        Assert.Equal(2, box.Width, 6);
    }

    [Fact]
    public void ChannelSideView_IsOneClosedSectionOfTheFormedChannel()
    {
        var shape = new ChannelShape();
        shape.SetPreviewDefaults();

        var section = Assert.Single(ShapeBuilder.GetShapes(shape.GetSideView().Entities));

        // Two sides' and the bottom's flats and two quarter annuli at the bends.
        const double t = 0.125, ir = 0.3125, or = ir + t;
        var expected = t * (2 - or) * 2 + t * (4 - 2 * or) + System.Math.PI / 2 * (or * or - ir * ir);
        Assert.True(section.IsClosed());
        Assert.Equal(expected, section.Area(), 1e-4);
    }

    [Fact]
    public void SideViews_DimensionTheSizesTheBlankCannotShow()
    {
        var angle = new AngleShape();
        angle.SetPreviewDefaults();
        var rolled = new RolledPlateShape();
        rolled.SetPreviewDefaults();

        Assert.Equal(
            new[] { nameof(AngleShape.OutsideWidth), nameof(AngleShape.OutsideHeight), nameof(AngleShape.Thickness), nameof(AngleShape.InsideRadius) },
            angle.GetSideView().Dimensions.Select(d => d.Property));
        var radius = angle.GetSideView().Dimensions.Single(d => d.Property == nameof(AngleShape.InsideRadius));
        Assert.Equal(0.3125, radius.ArcRadius, 9);
        Assert.Equal("R", radius.Prefix);
        Assert.Equal(
            new[] { nameof(RolledPlateShape.InsideDiameter), nameof(RolledPlateShape.Thickness) },
            rolled.GetSideView().Dimensions.Select(d => d.Property));
    }

    [Fact]
    public void FlatShapes_HaveNoSideView()
    {
        Assert.Null(new RectangleShape { Length = 10, Width = 5 }.GetSideView());
    }

    [Fact]
    public void GeneratedNames_DescribeThePart()
    {
        var angle = new AngleShape();
        angle.SetPreviewDefaults();
        var rolled = new RolledPlateShape();
        rolled.SetPreviewDefaults();

        Assert.Equal("Angle 4x2x0.125 x 12", angle.GenerateName());
        Assert.Equal("Rolled Plate 10 ID x 0.188 x 12", rolled.GenerateName());
    }
}
