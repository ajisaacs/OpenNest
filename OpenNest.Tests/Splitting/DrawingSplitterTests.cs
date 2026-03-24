using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.Splitting;

public class DrawingSplitterTests
{
    /// <summary>
    /// Helper: creates a Drawing from a rectangular perimeter.
    /// </summary>
    private static Drawing MakeRectangleDrawing(string name, double width, double height)
    {
        var entities = new List<Entity>
        {
            new Line(new Vector(0, 0), new Vector(width, 0)),
            new Line(new Vector(width, 0), new Vector(width, height)),
            new Line(new Vector(width, height), new Vector(0, height)),
            new Line(new Vector(0, height), new Vector(0, 0))
        };
        var pgm = ConvertGeometry.ToProgram(entities);
        return new Drawing(name, pgm);
    }

    [Fact]
    public void Split_Rectangle_Vertical_ProducesTwoPieces()
    {
        var drawing = MakeRectangleDrawing("RECT", 100, 50);
        var splitLines = new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Vertical) };
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        Assert.Equal(2, results.Count);
        Assert.Equal("RECT-1", results[0].Name);
        Assert.Equal("RECT-2", results[1].Name);

        // Each piece should have area close to half the original
        var totalArea = results.Sum(d => d.Area);
        Assert.Equal(drawing.Area, totalArea, 1);
    }

    [Fact]
    public void Split_Rectangle_Horizontal_ProducesTwoPieces()
    {
        var drawing = MakeRectangleDrawing("RECT", 100, 60);
        var splitLines = new List<SplitLine> { new SplitLine(30.0, CutOffAxis.Horizontal) };
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        Assert.Equal(2, results.Count);
        Assert.Equal("RECT-1", results[0].Name);
        Assert.Equal("RECT-2", results[1].Name);
    }

    [Fact]
    public void Split_ThreePieces_NamesSequentially()
    {
        var drawing = MakeRectangleDrawing("PART", 150, 50);
        var splitLines = new List<SplitLine>
        {
            new SplitLine(50.0, CutOffAxis.Vertical),
            new SplitLine(100.0, CutOffAxis.Vertical)
        };
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        Assert.Equal(3, results.Count);
        Assert.Equal("PART-1", results[0].Name);
        Assert.Equal("PART-2", results[1].Name);
        Assert.Equal("PART-3", results[2].Name);
    }

    [Fact]
    public void Split_CopiesDrawingProperties()
    {
        var drawing = MakeRectangleDrawing("PART", 100, 50);
        drawing.Color = System.Drawing.Color.Red;
        drawing.Priority = 5;

        var results = DrawingSplitter.Split(drawing,
            new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Vertical) },
            new SplitParameters());

        Assert.All(results, d =>
        {
            Assert.Equal(System.Drawing.Color.Red, d.Color);
            Assert.Equal(5, d.Priority);
        });
    }

    [Fact]
    public void Split_PiecesNormalizedToOrigin()
    {
        var drawing = MakeRectangleDrawing("PART", 100, 50);
        var results = DrawingSplitter.Split(drawing,
            new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Vertical) },
            new SplitParameters());

        // Each piece's program bounding box should start near (0,0)
        foreach (var d in results)
        {
            var bb = d.Program.BoundingBox();
            Assert.True(bb.X < 1.0, $"Piece {d.Name} not normalized: X={bb.X}");
            Assert.True(bb.Y < 1.0, $"Piece {d.Name} not normalized: Y={bb.Y}");
        }
    }

    [Fact]
    public void Split_WithCutout_AssignsCutoutToCorrectPiece()
    {
        // Rectangle 100x50 with a small square cutout at (20,20)-(30,30)
        var perimeterEntities = new List<Entity>
        {
            new Line(new Vector(0, 0), new Vector(100, 0)),
            new Line(new Vector(100, 0), new Vector(100, 50)),
            new Line(new Vector(100, 50), new Vector(0, 50)),
            new Line(new Vector(0, 50), new Vector(0, 0))
        };
        var cutoutEntities = new List<Entity>
        {
            new Line(new Vector(20, 20), new Vector(30, 20)),
            new Line(new Vector(30, 20), new Vector(30, 30)),
            new Line(new Vector(30, 30), new Vector(20, 30)),
            new Line(new Vector(20, 30), new Vector(20, 20))
        };
        var allEntities = new List<Entity>();
        allEntities.AddRange(perimeterEntities);
        allEntities.AddRange(cutoutEntities);

        var pgm = ConvertGeometry.ToProgram(allEntities);
        var drawing = new Drawing("HOLE", pgm);

        // Split at X=50 — cutout is in the left half
        var results = DrawingSplitter.Split(drawing,
            new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Vertical) },
            new SplitParameters());

        Assert.Equal(2, results.Count);
        // Left piece should have smaller area (has the cutout)
        Assert.True(results[0].Area < results[1].Area,
            "Left piece should have less area due to cutout");
    }

    [Fact]
    public void Split_GridSplit_ProducesFourPieces()
    {
        var drawing = MakeRectangleDrawing("GRID", 100, 100);
        var splitLines = new List<SplitLine>
        {
            new SplitLine(50.0, CutOffAxis.Vertical),
            new SplitLine(50.0, CutOffAxis.Horizontal)
        };
        var results = DrawingSplitter.Split(drawing, splitLines, new SplitParameters());

        Assert.Equal(4, results.Count);
        Assert.Equal("GRID-1", results[0].Name);
        Assert.Equal("GRID-2", results[1].Name);
        Assert.Equal("GRID-3", results[2].Name);
        Assert.Equal("GRID-4", results[3].Name);
    }
}
