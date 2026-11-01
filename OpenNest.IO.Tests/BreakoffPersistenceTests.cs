using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest.Breakoffs;
using static OpenNest.IO.Tests.NestArchiveJson;

namespace OpenNest.IO.Tests;

public class BreakoffPersistenceTests
{
    private static Drawing WithBreakoff()
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.Breakoffs.Add(new Breakoff
        {
            EdgeIds = [Guid.NewGuid(), Guid.NewGuid()],
            TabWidth = 0.05,
            Flip = true,
            TabCenters = [0.025, 1.5, 3.2],
        });
        return drawing;
    }

    [Fact]
    public void Breakoffs_RoundTripEveryField()
    {
        var drawing = WithBreakoff();
        var original = drawing.Breakoffs[0];

        var (restored, warnings) = RoundTrip(ArchiveBytes(drawing));

        Assert.Empty(warnings);
        var breakoff = Assert.Single(Assert.Single(restored.Drawings).Breakoffs);
        Assert.Equal(original.EdgeIds, breakoff.EdgeIds);
        Assert.Equal((0.05, true), (breakoff.TabWidth, breakoff.Flip));
        Assert.Equal(original.TabCenters, breakoff.TabCenters);
    }

    [Fact]
    public void BreakCutMoves_KeepTheirLayer()
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.Program.MoveTo(4, 5);
        drawing.Program.Codes.Add(new OpenNest.CNC.LinearMove(1, 0) { Layer = OpenNest.CNC.LayerType.Break });
        drawing.Program.Codes.Add(new OpenNest.CNC.ArcMove(1, 1, 0, 1) { Layer = OpenNest.CNC.LayerType.Break });

        var (restored, _) = RoundTrip(ArchiveBytes(drawing));

        var moves = Assert.Single(restored.Drawings).Program.Codes.OfType<OpenNest.CNC.Motion>()
            .Where(m => m is OpenNest.CNC.LinearMove { Layer: OpenNest.CNC.LayerType.Break }
                or OpenNest.CNC.ArcMove { Layer: OpenNest.CNC.LayerType.Break });
        Assert.Equal(2, moves.Count());
    }

    [Fact]
    public void NoBreakoffs_AreOmittedFromTheFile()
    {
        var archive = ArchiveBytes(DrawingPackageFixture.Build());

        Assert.False(NestJson(archive)["drawings"]![0]!.AsObject().ContainsKey("breakoffs"));
        Assert.Empty(Assert.Single(RoundTrip(archive).Nest.Drawings).Breakoffs);
    }

    [Fact]
    public void UnreadableBreakoff_IsDroppedWithAWarning_AndTheDrawingLoads()
    {
        var archive = ArchiveBytes(WithBreakoff());
        var json = NestJson(archive);
        json["drawings"]![0]!["breakoffs"]![0]!["tabWidth"] = 0;

        var (restored, warnings) = RoundTrip(ReplaceNestJson(archive, json));

        Assert.Empty(Assert.Single(restored.Drawings).Breakoffs);
        Assert.Contains(warnings, w => w.Contains("breakoff 1 could not be read"));
    }
}
