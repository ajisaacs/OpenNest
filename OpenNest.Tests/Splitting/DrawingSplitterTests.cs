using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.Splitting;

public class DrawingSplitterTests
{
    [Fact]
    public void Split_Rectangle_Vertical_ProducesTwoPieces()
    {
        var drawing = new RectangleShape { Name = "RECT", Length = 100, Width = 50 }.GetDrawing();
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
        var drawing = new RectangleShape { Name = "RECT", Length = 100, Width = 60 }.GetDrawing();
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
        var drawing = new RectangleShape { Name = "PART", Length = 150, Width = 50 }.GetDrawing();
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
        var drawing = new RectangleShape { Name = "PART", Length = 100, Width = 50 }.GetDrawing();
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
        var drawing = new RectangleShape { Name = "PART", Length = 100, Width = 50 }.GetDrawing();
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
        var drawing = new RectangleShape { Name = "GRID", Length = 100, Width = 100 }.GetDrawing();
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

    [Fact]
    public void Split_Square_Vertical_PieceWidthsSumToOriginal()
    {
        var drawing = new RectangleShape { Name = "SQ", Length = 100, Width = 100 }.GetDrawing();
        var splitLines = new List<SplitLine> { new SplitLine(40.0, CutOffAxis.Vertical) };
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        Assert.Equal(2, results.Count);

        var bb1 = results[0].Program.BoundingBox();
        var bb2 = results[1].Program.BoundingBox();

        // Piece lengths should sum to original length
        Assert.Equal(100.0, bb1.Length + bb2.Length, 1);

        // Both pieces should have the same width as the original
        Assert.Equal(100.0, bb1.Width, 1);
        Assert.Equal(100.0, bb2.Width, 1);
    }

    [Fact]
    public void Split_Square_Horizontal_PieceHeightsSumToOriginal()
    {
        var drawing = new RectangleShape { Name = "SQ", Length = 100, Width = 100 }.GetDrawing();
        var splitLines = new List<SplitLine> { new SplitLine(60.0, CutOffAxis.Horizontal) };
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        Assert.Equal(2, results.Count);

        var bb1 = results[0].Program.BoundingBox();
        var bb2 = results[1].Program.BoundingBox();

        // Piece widths should sum to original width
        Assert.Equal(100.0, bb1.Width + bb2.Width, 1);

        // Both pieces should have the same length as the original
        Assert.Equal(100.0, bb1.Length, 1);
        Assert.Equal(100.0, bb2.Length, 1);
    }

    [Fact]
    public void Split_Square_Vertical_AreaPreserved()
    {
        var drawing = new RectangleShape { Name = "SQ", Length = 100, Width = 100 }.GetDrawing();
        var originalArea = drawing.Area;
        var splitLines = new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Vertical) };
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        var totalArea = results.Sum(d => d.Area);
        Assert.Equal(originalArea, totalArea, 1);
    }

    [Fact]
    public void Split_Square_Vertical_PiecesAreClosedPerimeters()
    {
        var drawing = new RectangleShape { Name = "SQ", Length = 100, Width = 100 }.GetDrawing();
        var splitLines = new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Vertical) };
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        foreach (var piece in results)
        {
            var entities = ConvertProgram.ToGeometry(piece.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid).ToList();

            Assert.True(entities.Count >= 4, $"{piece.Name} should have at least 4 entities for a rectangle");

            // First entity start should connect to last entity end (closed shape)
            var firstStart = GetStartPoint(entities[0]);
            var lastEnd = GetEndPoint(entities[^1]);
            var closingGap = firstStart.DistanceTo(lastEnd);
            Assert.True(closingGap < 0.01,
                $"{piece.Name} is not closed: gap of {closingGap:F6} between last end and first start");

            // Consecutive entities should connect
            for (var i = 0; i < entities.Count - 1; i++)
            {
                var end = GetEndPoint(entities[i]);
                var start = GetStartPoint(entities[i + 1]);
                var gap = end.DistanceTo(start);
                Assert.True(gap < 0.01,
                    $"Gap of {gap:F6} between entities {i} and {i + 1} in {piece.Name}");
            }
        }
    }

    [Fact]
    public void Split_Square_Horizontal_PiecesAreClosedPerimeters()
    {
        var drawing = new RectangleShape { Name = "SQ", Length = 100, Width = 100 }.GetDrawing();
        var splitLines = new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Horizontal) };
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        foreach (var piece in results)
        {
            var entities = ConvertProgram.ToGeometry(piece.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid).ToList();

            Assert.True(entities.Count >= 4, $"{piece.Name} should have at least 4 entities for a rectangle");

            var firstStart = GetStartPoint(entities[0]);
            var lastEnd = GetEndPoint(entities[^1]);
            var closingGap = firstStart.DistanceTo(lastEnd);
            Assert.True(closingGap < 0.01,
                $"{piece.Name} is not closed: gap of {closingGap:F6} between last end and first start");

            for (var i = 0; i < entities.Count - 1; i++)
            {
                var end = GetEndPoint(entities[i]);
                var start = GetStartPoint(entities[i + 1]);
                var gap = end.DistanceTo(start);
                Assert.True(gap < 0.01,
                    $"Gap of {gap:F6} between entities {i} and {i + 1} in {piece.Name}");
            }
        }
    }

    [Fact]
    public void Split_Square_AsymmetricSplit_PieceDimensionsMatchSplitPosition()
    {
        var drawing = new RectangleShape { Name = "SQ", Length = 100, Width = 100 }.GetDrawing();
        var splitLines = new List<SplitLine> { new SplitLine(30.0, CutOffAxis.Vertical) };
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        Assert.Equal(2, results.Count);

        var bb1 = results[0].Program.BoundingBox();
        var bb2 = results[1].Program.BoundingBox();

        // Left piece should be 30 long, right piece should be 70 long
        Assert.Equal(30.0, bb1.Length, 1);
        Assert.Equal(70.0, bb2.Length, 1);
    }

    [Fact]
    public void Split_CircleHole_NotOnSplitLine_PreservedInCorrectPiece()
    {
        // Rectangle 100x50 with a circle hole at (20, 25) radius 3
        // Split vertically at x=50 — hole is entirely in the left piece
        var perimeterEntities = new List<Entity>
        {
            new Line(new Vector(0, 0), new Vector(100, 0)),
            new Line(new Vector(100, 0), new Vector(100, 50)),
            new Line(new Vector(100, 50), new Vector(0, 50)),
            new Line(new Vector(0, 50), new Vector(0, 0))
        };
        var hole = new Circle(new Vector(20, 25), 3);
        var allEntities = new List<Entity>();
        allEntities.AddRange(perimeterEntities);
        allEntities.Add(hole);

        var pgm = ConvertGeometry.ToProgram(allEntities);
        var drawing = new Drawing("CIRC", pgm);

        var results = DrawingSplitter.Split(drawing,
            new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Vertical) },
            new SplitParameters());

        Assert.Equal(2, results.Count);

        // Left piece should have the hole — verify by checking it has arc entities
        var leftEntities = ConvertProgram.ToGeometry(results[0].Program)
            .Where(e => e.Layer != SpecialLayers.Rapid).ToList();
        var leftArcs = leftEntities.OfType<Arc>().ToList();

        // Decomposed circle = 2 arcs. Both should be present.
        Assert.True(leftArcs.Count >= 2,
            $"Left piece should have at least 2 arcs (full circle), but has {leftArcs.Count}");

        // Right piece should have no arcs (hole is on the left)
        var rightEntities = ConvertProgram.ToGeometry(results[1].Program)
            .Where(e => e.Layer != SpecialLayers.Rapid).ToList();
        var rightArcs = rightEntities.OfType<Arc>().ToList();
        Assert.Equal(0, rightArcs.Count);
    }

    [Fact]
    public void Split_DxfRoundTrip_CircleHolePreserved()
    {
        // Two semicircular arcs (decomposed circle) must survive DXF write→reimport.
        // Regression: GeometryOptimizer.TryJoinArcs merged them into a single arc
        // because it incorrectly handled the wrap-around case (π→2π written as π→0°).
        var perimeterEntities = new List<Entity>
        {
            new Line(new Vector(0, 0), new Vector(100, 0)),
            new Line(new Vector(100, 0), new Vector(100, 50)),
            new Line(new Vector(100, 50), new Vector(0, 50)),
            new Line(new Vector(0, 50), new Vector(0, 0))
        };
        var hole = new Circle(new Vector(20, 25), 3);
        var allEntities = new List<Entity>();
        allEntities.AddRange(perimeterEntities);
        allEntities.Add(hole);

        var pgm = ConvertGeometry.ToProgram(allEntities);
        var drawing = new Drawing("CIRC", pgm);
        drawing.Bends = new List<OpenNest.Bending.Bend>();

        // Split — the circle gets decomposed into two arcs
        var results = DrawingSplitter.Split(drawing,
            new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Vertical) },
            new SplitParameters());

        Assert.Equal(2, results.Count);

        // Write left piece to DXF and re-import
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "split_roundtrip_test.dxf");
        try
        {
            var writer = new OpenNest.IO.SplitDxfWriter();
            writer.Write(tempPath, results[0]);

            var reimportResult = OpenNest.IO.Dxf.Import(tempPath);

            var afterArcs = reimportResult.Entities.OfType<Arc>().Count();
            var afterCircles = reimportResult.Entities.OfType<Circle>().Count();

            Assert.True(afterArcs + afterCircles * 2 >= 2,
                $"After DXF round-trip: {afterArcs} arcs, {afterCircles} circles (expected 2+ for full hole)");
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }

    [Fact]
    public void Split_RectangleWithSpanningSlot_ProducesDisconnectedStrips()
    {
        // 255x55 outer rectangle with a 235x35 interior slot centered at (10,10)-(245,45).
        // 4 vertical splits at x = 55, 110, 165, 220.
        //
        // Expected: regions R2/R3/R4 are entirely "over" the slot horizontally, so the
        // surviving material in each is two physically disjoint strips (upper + lower).
        // R1 and R5 each have a solid edge that connects the top and bottom strips, so
        // they remain single (notched) pieces.
        //
        // Total output drawings: 1 (R1) + 2 (R2) + 2 (R3) + 2 (R4) + 1 (R5) = 8.
        var outerEntities = new List<Entity>
        {
            new Line(new Vector(0, 0), new Vector(255, 0)),
            new Line(new Vector(255, 0), new Vector(255, 55)),
            new Line(new Vector(255, 55), new Vector(0, 55)),
            new Line(new Vector(0, 55), new Vector(0, 0))
        };
        var slotEntities = new List<Entity>
        {
            new Line(new Vector(10, 10), new Vector(245, 10)),
            new Line(new Vector(245, 10), new Vector(245, 45)),
            new Line(new Vector(245, 45), new Vector(10, 45)),
            new Line(new Vector(10, 45), new Vector(10, 10))
        };
        var allEntities = new List<Entity>();
        allEntities.AddRange(outerEntities);
        allEntities.AddRange(slotEntities);

        var drawing = new Drawing("SLOT", ConvertGeometry.ToProgram(allEntities));
        var originalArea = drawing.Area;

        var splitLines = new List<SplitLine>
        {
            new SplitLine(55.0, CutOffAxis.Vertical),
            new SplitLine(110.0, CutOffAxis.Vertical),
            new SplitLine(165.0, CutOffAxis.Vertical),
            new SplitLine(220.0, CutOffAxis.Vertical)
        };

        var results = DrawingSplitter.Split(drawing, splitLines, new SplitParameters { Type = SplitType.Straight });

        // R1 (0..55)   → 1 notched piece,  height 55
        // R2 (55..110) → upper strip + lower strip, each height 10
        // R3 (110..165)→ upper strip + lower strip, each height 10
        // R4 (165..220)→ upper strip + lower strip, each height 10
        // R5 (220..255)→ 1 notched piece,  height 55
        Assert.Equal(8, results.Count);

        // Area preservation: sum of all output areas equals (outer − slot).
        var totalArea = results.Sum(d => d.Area);
        Assert.Equal(originalArea, totalArea, 1);

        // Box.Length = X-extent, Box.Width = Y-extent.
        // Exactly 6 strips (Y-extent ~10mm) from the three middle regions, and
        // exactly 2 notched pieces (Y-extent 55mm) from R1 and R5.
        var strips = results
            .Where(d => System.Math.Abs(d.Program.BoundingBox().Width - 10.0) < 0.5)
            .ToList();
        var notched = results
            .Where(d => System.Math.Abs(d.Program.BoundingBox().Width - 55.0) < 0.5)
            .ToList();

        Assert.Equal(6, strips.Count);
        Assert.Equal(2, notched.Count);

        // Each piece should form a closed perimeter (no dangling edges, no gaps).
        foreach (var piece in results)
        {
            var entities = ConvertProgram.ToGeometry(piece.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid).ToList();

            Assert.True(entities.Count >= 3, $"{piece.Name} must have at least 3 edges");

            for (var i = 0; i < entities.Count; i++)
            {
                var end = GetEndPoint(entities[i]);
                var nextStart = GetStartPoint(entities[(i + 1) % entities.Count]);
                var gap = end.DistanceTo(nextStart);
                Assert.True(gap < 0.01,
                    $"{piece.Name} gap of {gap:F4} between edge {i} end and edge {(i + 1) % entities.Count} start");
            }
        }
    }

    [Fact]
    public void Split_DxfFile_WithSpanningSlot_HasNoCutLinesThroughCutout()
    {
        // Real DXF regression: 255x55 plate with a centered slot cutout, split into
        // five columns. Exercises the same path as the synthetic
        // Split_RectangleWithSpanningSlot_ProducesDisconnectedStrips test but through
        // the full DXF import pipeline.
        var path = Path.Combine(AppContext.BaseDirectory, "Splitting", "TestData", "split_test.dxf");
        Assert.True(File.Exists(path), $"Test DXF not found: {path}");

        var imported = OpenNest.IO.Dxf.Import(path);
        var profile = new OpenNest.Geometry.ShapeProfile(imported.Entities);

        // Normalize to origin so the split line positions are predictable.
        var bb = profile.Perimeter.BoundingBox;
        var offsetX = -bb.X;
        var offsetY = -bb.Y;
        foreach (var e in profile.Perimeter.Entities) e.Offset(offsetX, offsetY);
        foreach (var cutout in profile.Cutouts)
            foreach (var e in cutout.Entities) e.Offset(offsetX, offsetY);

        var allEntities = new List<Entity>();
        allEntities.AddRange(profile.Perimeter.Entities);
        foreach (var cutout in profile.Cutouts) allEntities.AddRange(cutout.Entities);

        var drawing = new Drawing("SPLITTEST", ConvertGeometry.ToProgram(allEntities));
        var originalArea = drawing.Area;

        // Part is ~255x55 with an interior slot. Split into 5 columns (55mm each).
        var splitLines = new List<SplitLine>
        {
            new SplitLine(55.0, CutOffAxis.Vertical),
            new SplitLine(110.0, CutOffAxis.Vertical),
            new SplitLine(165.0, CutOffAxis.Vertical),
            new SplitLine(220.0, CutOffAxis.Vertical)
        };

        var results = DrawingSplitter.Split(drawing, splitLines, new SplitParameters { Type = SplitType.Straight });

        // Area must be preserved within tolerance (floating-point coords in the DXF).
        var totalArea = results.Sum(d => d.Area);
        Assert.Equal(originalArea, totalArea, 0);

        // At least one region must yield more than one physical strip — that's the
        // whole point of the fix: a cutout that spans a region disconnects it.
        Assert.True(results.Count > splitLines.Count + 1,
            $"Expected more than {splitLines.Count + 1} pieces (some regions split into strips), got {results.Count}");

        // Every output drawing must resolve into fully-closed shapes (outer loop
        // and any hole loops), with no dangling geometry. A piece that contains
        // a cutout will have its entities span more than one connected loop.
        foreach (var piece in results)
        {
            var entities = ConvertProgram.ToGeometry(piece.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid).ToList();

            Assert.True(entities.Count >= 3, $"{piece.Name} has only {entities.Count} entities");

            var shapes = OpenNest.Geometry.ShapeBuilder.GetShapes(entities);
            Assert.NotEmpty(shapes);

            foreach (var shape in shapes)
            {
                Assert.True(shape.IsClosed(),
                    $"{piece.Name} contains an open chain of {shape.Entities.Count} entities");
            }
        }
    }

    private static Vector GetStartPoint(Entity entity)
    {
        return entity switch
        {
            Line l => l.StartPoint,
            Arc a => a.StartPoint(),
            _ => new Vector(0, 0)
        };
    }

    private static Vector GetEndPoint(Entity entity)
    {
        return entity switch
        {
            Line l => l.EndPoint,
            Arc a => a.EndPoint(),
            _ => new Vector(0, 0)
        };
    }
}
