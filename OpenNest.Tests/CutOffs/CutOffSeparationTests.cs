using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

/// <summary>Which cut-offs split off a piece of the sheet (and so must be cut after every part).</summary>
public class CutOffSeparationTests
{
    [Theory]
    [InlineData(1, CutDirection.AwayFromOrigin)]
    [InlineData(2, CutDirection.TowardOrigin)]
    [InlineData(3, CutDirection.AwayFromOrigin)]
    [InlineData(4, CutDirection.TowardOrigin)]
    public void ContinuousFullSpanLine_SeparatesTheSheet_InEveryQuadrantAndDirection(int quadrant, CutDirection direction)
    {
        var plate = new Plate(60, 120) { Quadrant = quadrant };
        var bounds = plate.BoundingBox(includeParts: false);
        plate.Parts.Add(TestHelpers.MakePartAt(bounds.Left + 10, bounds.Bottom + 10, 10));
        var vertical = new CutOff(new Vector(bounds.Left + 40, 0), CutOffAxis.Vertical);
        var horizontal = new CutOff(new Vector(0, bounds.Bottom + 40), CutOffAxis.Horizontal);
        var crossing = new CutOff(new Vector(bounds.Left + 15, 0), CutOffAxis.Vertical);
        plate.CutOffs.AddRange([vertical, horizontal, crossing]);
        plate.RegenerateCutOffs(new CutOffSettings { CutDirection = direction, Overtravel = 0.5 });

        Assert.True(Separates(plate, vertical));
        Assert.True(Separates(plate, horizontal));
        Assert.False(Separates(plate, crossing)); // Trimmed around the part: two pieces.
    }

    [Fact]
    public void LimitedEdgeStaleOrBrokenLines_DoNotSeparateTheSheet()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(TestHelpers.MakePartAt(10, 10, 10));
        var limited = new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { EndLimit = 30 };
        var onEdge = new CutOff(new Vector(0, 0), CutOffAxis.Vertical);
        var stale = new CutOff(new Vector(50, 0), CutOffAxis.Vertical);
        var split = new CutOff(new Vector(70, 0), CutOffAxis.Vertical);
        plate.CutOffs.AddRange([limited, onEdge, stale, split]);
        plate.RegenerateCutOffs(new CutOffSettings());
        Assert.True(Separates(plate, stale));
        Assert.True(Separates(plate, split));

        stale.Position = new Vector(51, 0); // Moved without regeneration: its program is elsewhere.
        var program = new Program();
        program.Codes.Add(new RapidMove(70, 0));
        program.Codes.Add(new LinearMove(70, 30));
        program.Codes.Add(new RapidMove(70, 30.5));
        program.Codes.Add(new LinearMove(70, 60));
        split.Drawing.Program = program;
        var broken = new Part(split.Drawing);

        Assert.False(Separates(plate, limited));
        Assert.False(Separates(plate, onEdge));
        Assert.False(Separates(plate, stale));
        Assert.False(CutOff.SeparatesSheet(broken, split, plate.BoundingBox(false))); // Two pierces leave a gap.
        Assert.False(CutOff.SeparatesSheet(Part(plate, limited), null, plate.BoundingBox(false)));
    }

    // The automatic remnant layout: a full-length horizontal separator above the parts and strip
    // lines stopping at it. The last line, clear of the parts, frees the corner remnant together
    // with the separator, so it separates in the next round, as does a line from it to the far
    // edge. A strip line trimmed around a part and a line short of the separator do not.
    [Fact]
    public void LinesStoppingOnSeparators_SeparateInLaterRounds()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(TestHelpers.MakePartAt(10, 10, 10));
        var horizontal = new CutOff(new Vector(0, 30), CutOffAxis.Horizontal);
        var remnant = new CutOff(new Vector(70, 0), CutOffAxis.Vertical) { EndLimit = 30 };
        var beyond = new CutOff(new Vector(0, 15), CutOffAxis.Horizontal) { StartLimit = 70 };
        var strip = new CutOff(new Vector(15, 0), CutOffAxis.Vertical) { EndLimit = 30 };
        var shortOf = new CutOff(new Vector(50, 0), CutOffAxis.Vertical) { EndLimit = 20 };
        plate.CutOffs.AddRange([horizontal, remnant, beyond, strip, shortOf]);
        plate.RegenerateCutOffs(new CutOffSettings());

        var separators = CutOff.Separators(plate);

        Assert.Equal(0, separators[Part(plate, horizontal)]);
        Assert.Equal(1, separators[Part(plate, remnant)]);
        Assert.Equal(2, separators[Part(plate, beyond)]);
        Assert.False(Separates(plate, remnant)); // Not across the whole sheet on its own.
        Assert.Equal(3, separators.Count);
        Assert.DoesNotContain(Part(plate, shortOf), separators.Keys);
    }

    // A line trimmed around a part is two segment parts, neither of which spans the sheet.
    private static bool Separates(Plate plate, CutOff cutOff) =>
        TestHelpers.CutOffParts(plate, cutOff) is [var part]
        && CutOff.SeparatesSheet(part, cutOff, plate.BoundingBox(includeParts: false));

    private static Part Part(Plate plate, CutOff cutOff) =>
        Assert.Single(plate.Parts, p => ReferenceEquals(p.BaseDrawing, cutOff.Drawing));
}
