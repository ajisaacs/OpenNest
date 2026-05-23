using OpenNest;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Posts.GravographIS;

namespace OpenNest.Tests.GravographIS;

public class NestPolylineExtractorTests
{
    [Fact]
    public void ExtractPart_IncrementalProgram_ProducesAbsoluteCoordinates()
    {
        // 1x1 square in G91 (incremental) mode — the form OpenNest's UI writes
        // to .nest files. Without absolute-mode handling the extractor plotted
        // each EndPoint as if it were absolute, producing a 2x2 diamond.
        var program = new Program(Mode.Incremental);
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(1, 0));
        program.Codes.Add(new LinearMove(0, 1));
        program.Codes.Add(new LinearMove(-1, 0));
        program.Codes.Add(new LinearMove(0, -1));

        var drawing = new Drawing("Square 1x1", program);
        var part = new Part(drawing, new Vector(0.25, 46.75));

        var polylines = new NestPolylineExtractor().ExtractPart(part);

        Assert.Single(polylines);
        var poly = polylines[0];
        Assert.Equal(5, poly.Count);
        Assert.Equal(new Vector(0.25, 46.75), poly[0]);
        Assert.Equal(new Vector(1.25, 46.75), poly[1]);
        Assert.Equal(new Vector(1.25, 47.75), poly[2]);
        Assert.Equal(new Vector(0.25, 47.75), poly[3]);
        Assert.Equal(new Vector(0.25, 46.75), poly[4]);
    }
}
