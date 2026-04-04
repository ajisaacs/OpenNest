using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Tests.Fill;

public class PatternTilerTests
{
    private static Drawing MakeSquareDrawing(double size)
    {
        var pgm = new Program();
        pgm.Codes.Add(new LinearMove(size, 0));
        pgm.Codes.Add(new LinearMove(size, size));
        pgm.Codes.Add(new LinearMove(0, size));
        pgm.Codes.Add(new LinearMove(0, 0));
        return new Drawing("square", pgm);
    }

    [Fact]
    public void Tile_SinglePart_FillsGrid()
    {
        var drawing = MakeSquareDrawing(10);
        var cell = new List<Part> { Part.CreateAtOrigin(drawing) };
        var plateSize = new Size(30, 20);
        var partSpacing = 0.0;

        var result = PatternTiler.Tile(cell, plateSize, partSpacing);

        Assert.Equal(6, result.Count);

        foreach (var part in result)
        {
            Assert.True(part.BoundingBox.Right <= plateSize.Length + 0.001);
            Assert.True(part.BoundingBox.Top <= plateSize.Width + 0.001);
            Assert.True(part.BoundingBox.Left >= -0.001);
            Assert.True(part.BoundingBox.Bottom >= -0.001);
        }
    }

    [Fact]
    public void Tile_TwoParts_TilesUnitCell()
    {
        var drawing = MakeSquareDrawing(10);
        var partA = Part.CreateAtOrigin(drawing);
        var partB = Part.CreateAtOrigin(drawing);
        partB.Offset(10, 0);

        var cell = new List<Part> { partA, partB };
        var plateSize = new Size(40, 20);
        var partSpacing = 0.0;

        var result = PatternTiler.Tile(cell, plateSize, partSpacing);

        Assert.Equal(8, result.Count);
    }

    [Fact]
    public void Tile_WithSpacing_ReducesCount()
    {
        var drawing = MakeSquareDrawing(10);
        var cell = new List<Part> { Part.CreateAtOrigin(drawing) };
        var plateSize = new Size(30, 20);
        var partSpacing = 2.0;

        var result = PatternTiler.Tile(cell, plateSize, partSpacing);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Tile_EmptyCell_ReturnsEmpty()
    {
        var result = PatternTiler.Tile(new List<Part>(), new Size(100, 100), 0);
        Assert.Empty(result);
    }

    [Fact]
    public void Tile_NonSquarePlate_CorrectAxes()
    {
        var drawing = MakeSquareDrawing(10);
        var cell = new List<Part> { Part.CreateAtOrigin(drawing) };
        var plateSize = new Size(50, 10);

        var result = PatternTiler.Tile(cell, plateSize, 0);

        Assert.Equal(5, result.Count);

        var maxRight = result.Max(p => p.BoundingBox.Right);
        var maxTop = result.Max(p => p.BoundingBox.Top);
        Assert.True(maxRight <= 10.001);
        Assert.True(maxTop <= 50.001);
    }

    [Fact]
    public void Tile_CellLargerThanPlate_ReturnsEmpty()
    {
        var drawing = MakeSquareDrawing(50);
        var cell = new List<Part> { Part.CreateAtOrigin(drawing) };
        var plateSize = new Size(30, 30);

        var result = PatternTiler.Tile(cell, plateSize, 0);

        Assert.Empty(result);
    }
}
