using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.Splitting;

public class SplitIntegrationTest
{
    [Fact]
    public void Split_SpikeGroove_NoContinuityGaps()
    {
        var drawing = new RectangleShape { Name = "TEST", Length = 100, Width = 50 }.GetDrawing();

        var sl = new SplitLine(50.0, CutOffAxis.Vertical);
        sl.FeaturePositions.Add(12.5);
        sl.FeaturePositions.Add(37.5);

        var parameters = new SplitParameters
        {
            Type = SplitType.SpikeGroove,
            GrooveDepth = 0.625,
            SpikeDepth = 0.75,
            SpikeWeldGap = 0.125,
            SpikeAngle = 45,
            SpikePairCount = 2
        };

        var results = DrawingSplitter.Split(drawing, new List<SplitLine> { sl }, parameters);
        Assert.Equal(2, results.Count);

        foreach (var piece in results)
        {
            // Get cut entities only (no rapids)
            var pieceEntities = ConvertProgram.ToGeometry(piece.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid).ToList();

            // Check that consecutive entity endpoints connect (no gaps)
            for (var i = 0; i < pieceEntities.Count - 1; i++)
            {
                var end = GetEndPoint(pieceEntities[i]);
                var start = GetStartPoint(pieceEntities[i + 1]);
                var gap = end.DistanceTo(start);
                Assert.True(gap < 0.01,
                    $"Gap of {gap:F6} between entities {i} and {i + 1} in {piece.Name}");
            }

            // Area should be non-zero
            Assert.True(piece.Area > 0, $"{piece.Name} has zero area");
        }
    }

    [Fact]
    public void Split_SpikeGroove_Horizontal_NoContinuityGaps()
    {
        var drawing = new RectangleShape { Name = "TEST", Length = 100, Width = 50 }.GetDrawing();

        var sl = new SplitLine(25.0, CutOffAxis.Horizontal);
        sl.FeaturePositions.Add(25.0);
        sl.FeaturePositions.Add(75.0);

        var parameters = new SplitParameters
        {
            Type = SplitType.SpikeGroove,
            GrooveDepth = 0.625,
            SpikeDepth = 0.75,
            SpikeWeldGap = 0.125,
            SpikeAngle = 45,
            SpikePairCount = 2
        };

        var results = DrawingSplitter.Split(drawing, new List<SplitLine> { sl }, parameters);
        Assert.Equal(2, results.Count);

        foreach (var piece in results)
        {
            var pieceEntities = ConvertProgram.ToGeometry(piece.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid).ToList();

            for (var i = 0; i < pieceEntities.Count - 1; i++)
            {
                var end = GetEndPoint(pieceEntities[i]);
                var start = GetStartPoint(pieceEntities[i + 1]);
                var gap = end.DistanceTo(start);
                Assert.True(gap < 0.01,
                    $"Gap of {gap:F6} between entities {i} and {i + 1} in {piece.Name}");
            }

            Assert.True(piece.Area > 0, $"{piece.Name} has zero area");
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
