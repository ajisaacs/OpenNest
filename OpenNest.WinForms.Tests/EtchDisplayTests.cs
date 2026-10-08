using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Geometry;
using Program = OpenNest.CNC.Program;

namespace OpenNest.WinForms.Tests;

public class EtchDisplayTests
{
    private static readonly Color EtchColor = Color.Lime;
    private static readonly Color CutColor = Color.FromArgb(48, 48, 48);
    private static readonly Color PartColor = Color.FromArgb(216, 49, 49);

    [Theory]
    [InlineData(false, false, Mode.Absolute)]
    [InlineData(false, true, Mode.Absolute)]
    [InlineData(true, false, Mode.Absolute)]
    [InlineData(true, true, Mode.Absolute)]
    [InlineData(false, false, Mode.Incremental)]
    [InlineData(false, true, Mode.Incremental)]
    [InlineData(true, false, Mode.Incremental)]
    [InlineData(true, true, Mode.Incremental)]
    public void PlateSeparatesEtchFromCutWithoutChangingProgram(bool leads, bool selected, Mode mode)
        => StaTestThread.Run(() =>
        {
            var program = MixedProgram();
            program.Mode = mode;
            var before = Snapshot(program);
            var drawing = new Drawing("Synthetic etch display", program) { Color = PartColor };
            var part = new Part(drawing, new Vector(10, 10)) { HasManualLeadIns = leads };
            using var view = new PlateView(ColorSchemeRegistry.Get("Workshop"));
            view.Matrix = new Matrix();
            var layout = LayoutPart.Create(part, view);
            layout.IsSelected = selected;
            using var image = new Bitmap(130, 130);
            using var g = Graphics.FromImage(image);
            g.Clear(Color.White);
            layout.Draw(g);

            var renderedFill = image.GetPixel(20, 20);
            if (!selected)
                Assert.Equal(PartColor.ToArgb(), renderedFill.ToArgb());
            else
                Assert.NotEqual(PartColor.ToArgb(), renderedFill.ToArgb());
            Assert.True(Luminance(CutColor) < Luminance(renderedFill));
            Assert.True(Distinctness(EtchColor, renderedFill) >= 15);
            // A closed etch is not a hole; an actual closed cut still is.
            Assert.Equal(image.GetPixel(20, 20), image.GetPixel(40, 40));
            Assert.Equal(Color.White.ToArgb(), image.GetPixel(90, 90).ToArgb());
            Assert.Equal(EtchColor.ToArgb(), image.GetPixel(30, 40).ToArgb());
            Assert.Equal(EtchColor.ToArgb(), image.GetPixel(40, 70).ToArgb());
            Assert.Equal(EtchColor.ToArgb(), image.GetPixel(75, 40).ToArgb());
            Assert.Equal(CutColor.ToArgb(), image.GetPixel(10, 50).ToArgb());
            Assert.Equal(CutColor.ToArgb(), image.GetPixel(80, 90).ToArgb());
            // Suppression must not leave a visible cut or etch stroke.
            Assert.Equal(image.GetPixel(20, 20), image.GetPixel(40, 90));
            Assert.Equal(before, Snapshot(program));
        }, TimeSpan.FromMinutes(1), "Etch display test timed out.");

    [Fact]
    public void ActualCutWinsWhenEtchOccupiesTheSameLine() => StaTestThread.Run(() =>
    {
        var program = MixedProgram();
        program.MoveTo(20, 60);
        program.Codes.Add(new LinearMove(40, 60) { Layer = LayerType.Cut });
        using var view = new PlateView(ColorSchemeRegistry.Get("Workshop"));
        view.Matrix = new Matrix();
        var layout = LayoutPart.Create(new Part(new Drawing("Overlaid cut", program)), view);
        using var image = new Bitmap(120, 120);
        using var g = Graphics.FromImage(image);
        layout.Draw(g);
        Assert.Equal(CutColor.ToArgb(), image.GetPixel(30, 60).ToArgb());
    }, TimeSpan.FromMinutes(1), "Overlaid cut test timed out.");

    [Fact]
    public void ThumbnailKeepsEtchOutOfFillAndUsesDedicatedColor()
    {
        var program = MixedProgram();
        using var fill = new SolidBrush(Color.FromArgb(184, 207, 224));
        using var image = (Bitmap)program.GetImage(new System.Drawing.Size(110, 110), Pens.Black, fill);
        Assert.Equal(image.GetPixel(15, 95), image.GetPixel(35, 75));
        Assert.Equal(0, image.GetPixel(85, 25).A);
        Assert.Equal(ColorScheme.Default.EtchColor.ToArgb(), image.GetPixel(25, 75).ToArgb());
        Assert.Equal(Color.Black.ToArgb(), image.GetPixel(75, 25).ToArgb());
    }

    [Fact]
    public void PlateRendererKeepsEtchesInNumberedPartsAndPlacementPreviews() => StaTestThread.Run(() =>
    {
        using var view = new PlateView(ColorSchemeRegistry.Get("Workshop"))
        {
            Size = new System.Drawing.Size(250, 130),
            DrawBounds = false,
        };
        view.Matrix = new Matrix();
        view.Plate.Parts.Add(new Part(new Drawing("Placed", MixedProgram())));
        view.Previews.SetActiveParts(new List<Part>
        {
            new(new Drawing("Preview", MixedProgram()), new Vector(120, 0)),
        });
        using var image = new Bitmap(250, 130);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.White);
        view.Renderer.DrawParts(g);
        Assert.Equal(EtchColor.ToArgb(), image.GetPixel(20, 30).ToArgb());
        Assert.Equal(EtchColor.ToArgb(), image.GetPixel(140, 30).ToArgb());
        Assert.Equal(CutColor.ToArgb(), image.GetPixel(0, 50).ToArgb());
    }, TimeSpan.FromMinutes(1), "Plate preview test timed out.");

    [Fact]
    public void WorkshopIsAnAdditionalPaletteNotAReplacementForSavedSchemes()
    {
        var scheme = ColorSchemeRegistry.Get("Workshop");
        Assert.Equal("Workshop", scheme.Name);
        Assert.Equal("Classic", ColorSchemeRegistry.Get("Classic").Name);
        Assert.Equal("Pastel", ColorSchemeRegistry.Get("Pastel").Name);
        Assert.Equal("Dark", ColorSchemeRegistry.Get("Dark").Name);
        Assert.NotEmpty(scheme.PartColors);
        Assert.True(scheme.UseGoldenAngleColors);
        Assert.Equal(PartColor, scheme.PartColors[0]);
        Assert.Equal(Color.FromArgb(242, 242, 242), scheme.LayoutFillColor);
        Assert.True(scheme.SelectedPartColor.IsEmpty);
        Assert.All(Enumerable.Range(0, 1000).Select(PartColorPalette.GoldenAngle), color =>
        {
            Assert.True(Distinctness(color, EtchColor) >= 15, "Etch must stay distinguishable from generated fills.");
            Assert.True(Distinctness(color, CutColor) >= 15, "Cuts must stay distinguishable from generated fills.");
        });
    }

    [Fact]
    public void SchemeRoundTripAndOldFilesPreserveOutlineFallback()
    {
        var workshop = ColorSchemeRegistry.Get("Workshop");
        var restored = ColorSchemeSerializer.Deserialize(ColorSchemeSerializer.Serialize(workshop));
        Assert.Equal(CutColor.ToArgb(), restored.PartOutlineColor.ToArgb());
        Assert.Equal(EtchColor.ToArgb(), restored.EtchColor.ToArgb());
        Assert.True(restored.SelectedPartColor.IsEmpty);
        Assert.True(restored.UseGoldenAngleColors);
        Assert.Equal(workshop.PartColors.Select(c => c.ToArgb()), restored.PartColors.Select(c => c.ToArgb()));
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(ColorSchemeSerializer.Serialize(ColorSchemeRegistry.Get("Classic")))!.AsObject();
        legacy.Remove("etchColor");
        legacy.Remove("partOutlineColor");
        legacy.Remove("selectedPartColor");
        legacy.Remove("useGoldenAngleColors");
        var oldScheme = ColorSchemeSerializer.Deserialize(legacy.ToJsonString());
        Assert.True(oldScheme.PartOutlineColor.IsEmpty);
        Assert.True(oldScheme.SelectedPartColor.IsEmpty);
        Assert.False(oldScheme.UseGoldenAngleColors);
        Assert.Equal(System.Windows.Forms.ControlPaint.Dark(Color.Coral), oldScheme.GetPartOutlineColor(Color.Coral));
        Assert.Equal(new ColorScheme().EtchColor.ToArgb(), oldScheme.EtchColor.ToArgb());
    }

    [Fact]
    public void PaletteChangeRepaintsExistingPartWithoutMovingIt() => StaTestThread.Run(() =>
    {
        var scheme = ColorSchemeSerializer.Deserialize(ColorSchemeSerializer.Serialize(ColorSchemeRegistry.Get("Classic")));
        var drawing = new Drawing("Recolor", MixedProgram()) { Color = Color.Coral };
        using var view = new PlateView(scheme);
        view.Matrix = new Matrix();
        var layout = LayoutPart.Create(new Part(drawing), view);
        var path = layout.Path;
        scheme.PartOutlineColor = CutColor;
        scheme.EtchColor = EtchColor;
        drawing.Color = Color.FromArgb(184, 207, 224);
        using var image = new Bitmap(120, 120);
        using var g = Graphics.FromImage(image);
        layout.Draw(g);
        Assert.Same(path, layout.Path);
        Assert.Equal(drawing.Color.ToArgb(), image.GetPixel(10, 10).ToArgb());
        Assert.Equal(CutColor.ToArgb(), image.GetPixel(0, 50).ToArgb());
        Assert.Equal(EtchColor.ToArgb(), image.GetPixel(20, 30).ToArgb());
    }, TimeSpan.FromMinutes(1), "Scheme repaint test timed out.");

    [Fact]
    public void LayerChangesAndSubprogramsDoNotCreateConnectingStrokes()
    {
        var program = new Program();
        program.MoveTo(10, 10);
        program.Codes.Add(new LinearMove(20, 10) { Layer = LayerType.Cut });
        program.Codes.Add(new LinearMove(30, 10) { Layer = LayerType.Scribe });
        program.Codes.Add(new LinearMove(40, 10) { Layer = LayerType.Cut });
        program.Codes.Add(new LinearMove(50, 10) { Layer = LayerType.Leadin });
        program.Codes.Add(new LinearMove(60, 10) { Layer = LayerType.Leadout });
        var child = new Program(Mode.Incremental);
        child.MoveTo(2, 3);
        child.Codes.Add(new LinearMove(6, 0) { Layer = LayerType.Scribe });
        program.Codes.Add(new SubProgramCall(child, 0) { Offset = new Vector(70, 20) });
        var before = Snapshot(program);
        program.GetDisplayPaths(new Vector(5, 5), out var cuts, out var leads, out var etches);
        using (cuts)
        using (leads)
        using (etches)
        using (var pen = new Pen(Color.Black, 1))
        {
            Assert.True(cuts.IsOutlineVisible(20, 15, pen));
            Assert.True(cuts.IsOutlineVisible(40, 15, pen));
            Assert.False(cuts.IsOutlineVisible(30, 15, pen));
            Assert.True(etches.IsOutlineVisible(30, 15, pen));
            Assert.False(etches.IsOutlineVisible(40, 15, pen));
            Assert.True(leads.IsOutlineVisible(50, 15, pen));
            Assert.True(leads.IsOutlineVisible(60, 15, pen));
            Assert.False(cuts.IsOutlineVisible(50, 15, pen));
            Assert.True(etches.IsOutlineVisible(80, 28, pen));
            Assert.False(etches.IsOutlineVisible(60, 21, pen));
        }
        Assert.Equal(before, Snapshot(program));
    }

    private static double Contrast(Color a, Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        return (System.Math.Max(first, second) + 0.05) / (System.Math.Min(first, second) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : System.Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static double Distinctness(Color a, Color b) => System.Math.Sqrt(
        (a.R - (double)b.R) * (a.R - b.R)
        + (a.G - (double)b.G) * (a.G - b.G)
        + (a.B - (double)b.B) * (a.B - b.B));

    private static Program MixedProgram()
    {
        var program = new Program();
        AddSquare(program, 0, 0, 100, LayerType.Cut);
        AddSquare(program, 20, 20, 20, LayerType.Scribe);
        AddSquare(program, 70, 70, 20, LayerType.Cut);
        program.MoveTo(20, 60);
        program.Codes.Add(new LinearMove(40, 60) { Layer = LayerType.Scribe });
        program.MoveTo(65, 30);
        program.Codes.Add(new ArcMove(65, 30, 60, 30) { Layer = LayerType.Scribe });
        program.MoveTo(20, 80);
        program.Codes.Add(new LinearMove(40, 80) { Layer = LayerType.Scribe, Suppressed = true });
        return program;
    }

    private static void AddSquare(Program program, double x, double y, double size, LayerType layer)
    {
        program.MoveTo(x, y);
        foreach (var end in new[] { new Vector(x + size, y), new Vector(x + size, y + size), new Vector(x, y + size), new Vector(x, y) })
            program.Codes.Add(new LinearMove(end) { Layer = layer });
    }

    private static string[] Snapshot(Program program) => program.Codes.Select(code => code switch
    {
        ArcMove arc => $"arc:{arc.Layer}:{arc.Suppressed}:{arc.EndPoint.X:R}:{arc.EndPoint.Y:R}:{arc.CenterPoint.X:R}:{arc.CenterPoint.Y:R}:{arc.Rotation}",
        LinearMove line => $"line:{line.Layer}:{line.Suppressed}:{line.EndPoint.X:R}:{line.EndPoint.Y:R}",
        Motion motion => $"{motion.Type}:{motion.Suppressed}:{motion.EndPoint.X:R}:{motion.EndPoint.Y:R}",
        _ => code.ToString()!
    }).Prepend(program.Mode.ToString()).ToArray();
}
