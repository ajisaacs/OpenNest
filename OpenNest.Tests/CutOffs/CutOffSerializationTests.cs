using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CutOffs;

public class CutOffSerializationTests
{
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
        plate.CutOffs.Add(new CutOff(new Vector(85, 30), CutOffAxis.Horizontal) { EndLimit = 85.0 });
        plate.CutOffs.Add(new CutOff(new Vector(85, 30), CutOffAxis.Vertical) { StartLimit = 30.0 });
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
