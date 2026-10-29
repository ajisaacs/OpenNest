using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CutOffs;

public class CutOffSerializationTests
{
    [Fact]
    public void RoundTrip_SegmentsKeepTheirOwnPlacesInTheSequence()
    {
        var nest = new Nest();
        var drawing = TestHelpers.MakeSquareDrawing();
        nest.Drawings.Add(drawing);
        var lower = new Part(drawing, new Vector(10, 10));
        var upper = new Part(drawing, new Vector(10, 30));
        var plate = TestHelpers.MakePlate(60, 120, lower, upper);
        nest.Plates.Add(plate);
        var cutOff = new CutOff(new Vector(15, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutOff);
        plate.RegenerateCutOffs(new CutOffSettings());
        var segments = CutOffSegments.Of(plate, cutOff).Select(s => s.Part).ToArray();
        plate.Parts.Reorder([segments[0], lower, segments[1], upper, segments[2]]);

        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        using (var reader = new StreamReader(archive.GetEntry("nest.json")!.Open()))
        using (var json = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd()))
        {
            var saved = Assert.Single(Assert.Single(json.RootElement.GetProperty("plates").EnumerateArray())
                .GetProperty("cutOffs").EnumerateArray());
            Assert.Equal(0, saved.GetProperty("sequence").GetInt32());
            Assert.Equal(new[] { 0, 2, 4 }, saved.GetProperty("segments").EnumerateArray().Select(e => e.GetInt32()));
        }
        stream.Position = 0;
        var loaded = new NestReader(stream).Read();

        var loadedPlate = Assert.Single(loaded.Plates);
        var loadedCutOff = Assert.Single(loadedPlate.CutOffs);
        var loadedSegments = CutOffSegments.Of(loadedPlate, loadedCutOff);
        Assert.Equal(3, loadedSegments.Count);
        Assert.Equal(new[] { true, false, true, false, true }, loadedPlate.Parts.Select(p => p.BaseDrawing.IsCutOff));
        Assert.Equal(new[] { 0, 2, 4 }, loadedSegments.Select(s => loadedPlate.Parts.IndexOf(s.Part)));
        Assert.True(loadedSegments[0].End < loadedSegments[1].Start && loadedSegments[1].End < loadedSegments[2].Start);
    }

    [Fact]
    public void RoundTrip_GapsAreKeptAndWrittenOnlyWhenPresent()
    {
        var nest = new Nest();
        var plate = TestHelpers.MakePlate(48, 96);
        nest.Plates.Add(plate);
        var gapped = new CutOff(new Vector(0, 20), CutOffAxis.Horizontal) { Gaps = [new CutOffGap(35, 68.6)] };
        var plain = new CutOff(new Vector(35, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(gapped);
        plate.CutOffs.Add(plain);
        plate.RegenerateCutOffs(new CutOffSettings());

        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest) { KeepEmptyPlates = true }.Write(stream));
        stream.Position = 0;
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        using (var reader = new StreamReader(archive.GetEntry("nest.json")!.Open()))
        using (var json = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd()))
        {
            var saved = Assert.Single(json.RootElement.GetProperty("plates").EnumerateArray())
                .GetProperty("cutOffs").EnumerateArray().ToArray();
            var gap = Assert.Single(saved[0].GetProperty("gaps").EnumerateArray());
            Assert.Equal(35, gap.GetProperty("start").GetDouble());
            Assert.Equal(68.6, gap.GetProperty("end").GetDouble());
            Assert.False(saved[1].TryGetProperty("gaps", out _));
        }
        stream.Position = 0;
        var loaded = Assert.Single(new NestReader(stream).Read().Plates);

        Assert.Equal(new[] { new CutOffGap(35, 68.6) }, loaded.CutOffs[0].Gaps);
        Assert.Empty(loaded.CutOffs[1].Gaps);
        Assert.Equal(2, CutOffSegments.Of(loaded, loaded.CutOffs[0]).Count);
    }

    [Fact]
    public void RoundTrip_CutOffsPreserved()
    {
        var nest = new Nest();
        nest.Name = "test";
        nest.DateCreated = DateTime.Now;
        nest.DateLastModified = DateTime.Now;

        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        var drawing = new Drawing("part1", pgm);
        nest.Drawings.Add(drawing);

        var plate = new Plate(100, 50);
        plate.Parts.Add(new Part(drawing));
        plate.CutOffs.Add(new CutOff(new Vector(62.0, 24.0), CutOffAxis.Vertical));
        plate.CutOffs.Add(new CutOff(new Vector(48.0, 30.0), CutOffAxis.Horizontal));
        plate.RegenerateCutOffs(new CutOffSettings());
        nest.Plates.Add(plate);

        using var stream = new MemoryStream();
        var writer = new NestWriter(nest);
        writer.Write(stream);

        stream.Position = 0;
        var reader = new NestReader(stream);
        var loaded = reader.Read();

        Assert.Single(loaded.Plates);
        var loadedPlate = loaded.Plates[0];

        Assert.Equal(2, loadedPlate.CutOffs.Count);
        Assert.Equal(CutOffAxis.Vertical, loadedPlate.CutOffs[0].Axis);
        Assert.Equal(62.0, loadedPlate.CutOffs[0].Position.X, 5);
        Assert.Equal(24.0, loadedPlate.CutOffs[0].Position.Y, 5);
        Assert.Equal(CutOffAxis.Horizontal, loadedPlate.CutOffs[1].Axis);

        Assert.Single(loadedPlate.Parts.Where(p => !p.BaseDrawing.IsCutOff));
        Assert.Single(loaded.Drawings);
    }

    [Fact]
    public void NestWriter_SkipsCutOffPartsInPartsList()
    {
        var nest = new Nest();
        nest.Name = "test";
        nest.DateCreated = DateTime.Now;
        nest.DateLastModified = DateTime.Now;

        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        var drawing = new Drawing("part1", pgm);
        nest.Drawings.Add(drawing);

        var plate = new Plate(100, 50);
        plate.Parts.Add(new Part(drawing));
        plate.CutOffs.Add(new CutOff(new Vector(50, 25), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        nest.Plates.Add(plate);

        Assert.Equal(2, plate.Parts.Count);

        using var stream = new MemoryStream();
        var writer = new NestWriter(nest);
        writer.Write(stream);

        stream.Position = 0;
        var reader = new NestReader(stream);
        var loaded = reader.Read();

        Assert.Single(loaded.Plates[0].Parts.Where(p => !p.BaseDrawing.IsCutOff));
    }

    [Fact]
    public void RoundTrip_LimitsPreserved()
    {
        var nest = new Nest();
        nest.Name = "test";
        nest.DateCreated = DateTime.Now;
        nest.DateLastModified = DateTime.Now;

        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        var drawing = new Drawing("part1", pgm);
        nest.Drawings.Add(drawing);

        var plate = new Plate(100, 50);
        plate.Parts.Add(new Part(drawing));
        plate.CutOffs.Add(
            new CutOff(new Vector(85, 30), CutOffAxis.Horizontal) { EndLimit = 85.0 }
        );
        plate.CutOffs.Add(
            new CutOff(new Vector(85, 30), CutOffAxis.Vertical) { StartLimit = 30.0 }
        );
        plate.RegenerateCutOffs(new CutOffSettings());
        nest.Plates.Add(plate);

        using var stream = new MemoryStream();
        var writer = new NestWriter(nest);
        writer.Write(stream);

        stream.Position = 0;
        var reader = new NestReader(stream);
        var loaded = reader.Read();

        var loadedPlate = loaded.Plates[0];
        Assert.Equal(85.0, loadedPlate.CutOffs[0].EndLimit);
        Assert.Null(loadedPlate.CutOffs[0].StartLimit);
        Assert.Equal(30.0, loadedPlate.CutOffs[1].StartLimit);
        Assert.Null(loadedPlate.CutOffs[1].EndLimit);
    }
}
