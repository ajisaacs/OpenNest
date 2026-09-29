using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CutOffs;

public class CutOffLeadClearanceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Leads_DoNotReplaceRecessedOutlineWithConvexHull(bool horizontal, bool curved)
    {
        // Like P260805-10 plate 13: the main top edge is recessed 0.25 below small shoulders.
        var program = new Program();
        program.MoveTo(0, 0);
        foreach (var point in new[]
        {
            new Vector(40, 0), new Vector(40, 20.25), new Vector(38, 20.25),
            new Vector(38, 20), new Vector(2, 20), new Vector(2, 20.25),
            new Vector(0, 20.25), Vector.Zero,
        })
            program.Codes.Add(new LinearMove(point));
        var drawing = new Drawing("recessed-top", program);
        drawing.Quantity.Required = 1;
        var nest = new Nest("cutoff-leads");
        nest.Drawings.Add(drawing);
        var plate = nest.CreatePlate();
        plate.Size = new Size(100, 100);
        var part = new Part(drawing);
        if (horizontal)
            part.Rotate(System.Math.PI / 2);
        part.Location = new Vector(50, 10);
        plate.Parts.Add(part);
        var settings = new CutOffSettings();
        var cutoff = new CutOff(horizontal ? new Vector(0, 35) : new Vector(75, 0),
            horizontal ? CutOffAxis.Horizontal : CutOffAxis.Vertical);
        plate.CutOffs.Add(cutoff);
        plate.RegenerateCutOffs(settings);
        var cleanCut = NestWriter.GetProgramText(cutoff.Drawing.Program);
        AssertClearance(plate, horizontal);

        part.ApplyLeadIns(new CuttingParameters
        {
            ExternalLeadIn = curved ? new ArcLeadIn { Radius = 0.25 } : new LineLeadIn { Length = 0.25 },
            ExternalLeadOut = curved ? new ArcLeadOut { Radius = 0.25 } : new LineLeadOut { Length = 0.25 },
        }, new Vector(-2, -2));
        Assert.Contains(OpenNest.Converters.ConvertProgram.ToGeometry(part.Program), e => e.Layer == SpecialLayers.Leadin);
        Assert.Contains(OpenNest.Converters.ConvertProgram.ToGeometry(part.Program), e => e.Layer == SpecialLayers.Leadout);
        var originalProgram = part.Program;
        var originalText = NestWriter.GetProgramText(part.Program);
        var originalLocation = part.Location;
        var originalRotation = part.Rotation;
        var originalBounds = part.BoundingBox;
        var nested = drawing.Quantity.Nested;

        var perimeter = Assert.IsType<Shape>(Plate.BuildPerimeterCache(plate)[part]);
        Assert.True(perimeter.IsClosed());
        for (var iteration = 0; iteration < 2; iteration++)
        {
            plate.RegenerateCutOffs(settings);
            Assert.Equal(cleanCut, NestWriter.GetProgramText(cutoff.Drawing.Program));
            AssertClearance(plate, horizontal);
            Assert.Same(originalProgram, part.Program);
            Assert.Equal(originalText, NestWriter.GetProgramText(part.Program));
            Assert.Equal(originalLocation, part.Location);
            Assert.Equal(originalRotation, part.Rotation);
            Assert.Equal(originalBounds, part.BoundingBox);
            Assert.Equal(nested, drawing.Quantity.Nested);
            Assert.Equal(1, drawing.Quantity.Required);
        }

        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        var reader = new NestReader(stream);
        var loaded = reader.Read();
        Assert.Empty(reader.Warnings);
        var loadedPlate = Assert.Single(loaded.Plates);
        Assert.Equal(cleanCut, NestWriter.GetProgramText(Assert.Single(loadedPlate.CutOffs).Drawing.Program));
        AssertClearance(loadedPlate, horizontal);
    }

    private static void AssertClearance(Plate plate, bool horizontal)
    {
        var cutPart = Assert.Single(plate.Parts.Where(p => p.BaseDrawing.IsCutOff));
        var codes = cutPart.Program.Codes;
        Assert.Equal(4, codes.Count);
        // Independent material oracle: the unrotated outline is a union of three rectangles.
        // Undo the test's exact quarter-turn, not a production geometry/offset operation.
        Vector Local(Vector point)
        {
            point += cutPart.Location - new Vector(50, 10);
            return horizontal ? new Vector(point.Y, -point.X) : point;
        }
        var rectangles = new[] { (0.0, 40.0, 0.0, 20.0), (0.0, 2.0, 20.0, 20.25), (38.0, 40.0, 20.0, 20.25) };
        var topGap = double.PositiveInfinity;
        for (var i = 0; i < codes.Count; i += 2)
        {
            var start = Local(Assert.IsType<RapidMove>(codes[i]).EndPoint);
            var end = Local(Assert.IsType<LinearMove>(codes[i + 1]).EndPoint);
            var minX = System.Math.Min(start.X, end.X);
            var maxX = System.Math.Max(start.X, end.X);
            var minY = System.Math.Min(start.Y, end.Y);
            var maxY = System.Math.Max(start.Y, end.Y);
            foreach (var (left, right, bottom, top) in rectangles)
            {
                var dx = System.Math.Max(0, System.Math.Max(left - maxX, minX - right));
                var dy = System.Math.Max(0, System.Math.Max(bottom - maxY, minY - top));
                Assert.True(System.Math.Sqrt(dx * dx + dy * dy) >= 0.02 - 1e-9);
            }
            if (minY > 20)
                topGap = System.Math.Min(topGap, minY - 20);
        }
        Assert.InRange(topGap, 0.02, 0.021);
    }

    [Fact]
    public void OpenContour_WithLeadIn_StillUsesConservativeFallback()
    {
        var program = new Program();
        program.MoveTo(-1, 0);
        program.Codes.Add(new LinearMove(0, 0) { Layer = LayerType.Leadin });
        program.LineTo(10, 0);
        program.LineTo(10, 10);
        var plate = new Plate(100, 100);
        var part = new Part(new Drawing("open", program), new Vector(20, 20));
        plate.Parts.Add(part);
        var perimeter = Assert.IsType<Polygon>(Plate.BuildPerimeterCache(plate)[part]);
        Assert.True(perimeter.Vertices.Count >= 3);
        var cutoff = new CutOff(new Vector(25, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutoff);
        plate.RegenerateCutOffs(new CutOffSettings());
        var codes = cutoff.Drawing.Program.Codes;
        Assert.Equal(4, codes.Count);
        Assert.True(Assert.IsType<LinearMove>(codes[1]).EndPoint.Y <= 19.98);
        Assert.True(Assert.IsType<RapidMove>(codes[2]).EndPoint.Y >= 25.02);
    }
}
