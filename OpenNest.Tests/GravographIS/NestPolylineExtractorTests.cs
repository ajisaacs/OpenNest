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

    [Fact]
    public void ExtractPartLayered_SplitsContinuousChainAtLayerChange()
    {
        // A single continuous chain (no rapid) that switches from Scribe to Cut
        // must be split into two layer-uniform polylines sharing the seam vertex,
        // so the post can emit a tool-change pause between engrave and cut.
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new LinearMove(1, 0) { Layer = LayerType.Scribe });
        program.Codes.Add(new LinearMove(2, 0) { Layer = LayerType.Scribe });
        program.Codes.Add(new LinearMove(2, 1) { Layer = LayerType.Cut });
        program.Codes.Add(new LinearMove(3, 1) { Layer = LayerType.Cut });

        var drawing = new Drawing("Mixed", program);
        var part = new Part(drawing, new Vector(0, 0));

        var polylines = new NestPolylineExtractor().ExtractPartLayered(part);

        Assert.Equal(2, polylines.Count);

        Assert.Equal(LayerType.Scribe, polylines[0].Layer);
        Assert.Equal(new[] { new Vector(0, 0), new Vector(1, 0), new Vector(2, 0) }, polylines[0].Points);

        Assert.Equal(LayerType.Cut, polylines[1].Layer);
        Assert.Equal(new[] { new Vector(2, 0), new Vector(2, 1), new Vector(3, 1) }, polylines[1].Points);
    }

    [Fact]
    public void ExtractPartLayered_UniformChain_IsSinglePolyline()
    {
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new LinearMove(1, 0));
        program.Codes.Add(new LinearMove(1, 1));

        var part = new Part(new Drawing("Cut", program), new Vector(0, 0));

        var polylines = new NestPolylineExtractor().ExtractPartLayered(part);

        Assert.Single(polylines);
        Assert.Equal(LayerType.Cut, polylines[0].Layer);
    }
}
