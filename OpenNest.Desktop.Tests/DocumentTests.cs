using OpenNest.IO;
using OpenNest.Undo;

namespace OpenNest.Desktop.Tests;

public sealed class DocumentTests : IDisposable
{
    private readonly string directory =
        Directory.CreateTempSubdirectory("opennest-desktop-document-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void SaveAs_WritesAReadableNestNamedAfterTheFile()
    {
        var document = new Document { Nest = TestNests.OneSquare() };
        var path = Path.Combine(directory, "Renamed Job.nest");

        document.SaveAs(path);

        Assert.Equal("Renamed Job", document.Name);
        Assert.Equal(path, document.LastSavePath);
        Assert.True(document.HasSavePath);
        var read = new NestReader(path).Read();
        Assert.Equal("Renamed Job", read.Name);
        Assert.Single(read.Plates);
        Assert.Single(read.Plates[0].Parts);
    }

    [Fact]
    public void SaveAs_MarksTheCurrentHistoryStateSaved()
    {
        var document = new Document { Nest = TestNests.OneSquare() };
        document.History.Reset(new NestUndoState(NestSnapshot.Capture(document.Nest)));
        document.Nest.Plates[0].Parts[0].Offset(1, 0);
        document.History.Commit("Move", new NestUndoState(NestSnapshot.Capture(document.Nest)));
        Assert.False(document.History.IsAtSavePoint);

        document.SaveAs(Path.Combine(directory, "saved.nest"));

        Assert.True(document.History.IsAtSavePoint);
    }

    [Fact]
    public void Serialize_RoundTripsThroughTheNestReader()
    {
        var document = new Document { Nest = TestNests.OneSquare() };

        using var stream = new MemoryStream(document.Serialize());
        var read = new NestReader(stream).Read();

        Assert.Equal(document.Nest.Plates[0].Parts.Count, read.Plates[0].Parts.Count);
    }
}
