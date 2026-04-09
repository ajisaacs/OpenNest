using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.Converters;

public class SubProgramExpansionTests
{
    [Fact]
    public void ToGeometry_ExpandsSubProgramCall_WithOffset()
    {
        // Sub-program: a small line relative to (0,0)
        var sub = new Program(Mode.Incremental);
        sub.Codes.Add(new LinearMove(0.5, 0));

        // Main program: call sub at offset (10,20)
        var main = new Program(Mode.Absolute);
        main.SubPrograms[1] = sub;
        main.Codes.Add(new SubProgramCall { Id = 1, Program = sub, Offset = new Vector(10, 20) });

        var geometry = ConvertProgram.ToGeometry(main);

        // The sub-program's line should be offset by (10,20)
        // Sub emits incremental (0.5,0) from current position.
        // Since offset is (10,20), the line goes from (10,20) to (10.5,20).
        Assert.True(geometry.Count > 0);
        var line = geometry.OfType<Line>().FirstOrDefault();
        Assert.NotNull(line);
        Assert.Equal(10.5, line.EndPoint.X, 4);
        Assert.Equal(20, line.EndPoint.Y, 4);
    }

    [Fact]
    public void ToGeometry_MultipleSubProgramCalls_DifferentOffsets()
    {
        var sub = new Program(Mode.Incremental);
        sub.Codes.Add(new LinearMove(1, 0));

        var main = new Program(Mode.Absolute);
        main.SubPrograms[1] = sub;
        main.Codes.Add(new SubProgramCall { Id = 1, Program = sub, Offset = new Vector(0, 0) });
        main.Codes.Add(new SubProgramCall { Id = 1, Program = sub, Offset = new Vector(5, 5) });

        var geometry = ConvertProgram.ToGeometry(main);
        var lines = geometry.OfType<Line>().ToList();

        Assert.Equal(2, lines.Count);
        // First call at (0,0): line from (0,0) to (1,0)
        Assert.Equal(1, lines[0].EndPoint.X, 4);
        Assert.Equal(0, lines[0].EndPoint.Y, 4);
        // Second call at (5,5): line from (5,5) to (6,5)
        Assert.Equal(6, lines[1].EndPoint.X, 4);
        Assert.Equal(5, lines[1].EndPoint.Y, 4);
    }
}
