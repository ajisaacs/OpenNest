using OpenNest.Geometry;
using OpenNest.Labels;

namespace OpenNest.Tests.Labels;

public class LabelPreviewTests
{
    [Fact]
    public void PreviewText_IsTheNameAsItIsEtched()
    {
        Assert.Equal("4980A01PT103", PartLabels.PreviewText("4980 A01 pt103"));
        Assert.Equal("1234", PartLabels.PreviewText("1234"));
        Assert.Equal("", PartLabels.PreviewText(null));
    }

    [Fact]
    public void Layout_FitsTextInAWideBox_AndKeepsItInside()
    {
        var box = new LabelBox(new Vector(5, 5), 4, 0.5, System.Math.PI / 6);

        var strokes = PartLabels.Layout(box, "1234", out var fits);

        Assert.True(fits);
        Assert.NotEmpty(strokes!);
        foreach (var point in strokes!.SelectMany(s => s.Points()))
        {
            var local = box.ToLocal(point);
            Assert.InRange(local.X, -2 - 1e-9, 2 + 1e-9);
            Assert.InRange(local.Y, -0.25 - 1e-9, 0.25 + 1e-9);
        }
    }

    [Fact]
    public void Layout_InATallSkinnyBox_ShrinksTheTextAndSaysItWillNotEtch()
    {
        var box = new LabelBox(new Vector(0, 0), 0.25, 2, 0);

        var strokes = PartLabels.Layout(box, "4980A01PT103", out var fits);

        Assert.False(fits);
        Assert.NotEmpty(strokes!);
        var height = strokes!.SelectMany(s => s.Points()).Max(p => p.Y) - strokes.SelectMany(s => s.Points()).Min(p => p.Y);
        Assert.True(height < 1, $"Text {height} tall was not shrunk.");
    }

    [Fact]
    public void Layout_AgreesWithWhatPlanCuttingEtches()
    {
        var drawing = new Drawing("12345") { LabelBox = new LabelBox(new Vector(1, 1), 0.3, 0.25, 0) };

        PartLabels.Layout(drawing.LabelBox, PartLabels.Text(drawing), out var fits);

        Assert.False(fits);
        Assert.Null(PartLabels.Strokes(drawing, 0));
    }
}
