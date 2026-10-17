using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

public class DrawingPackageTests
{
    [Fact]
    public void Package_RoundTripsTheDrawingAndUnits()
    {
        var drawing = DrawingPackageFixture.Build();

        var contents = DrawingPackage.Read(DrawingPackage.Write(drawing, Units.Millimeters));

        Assert.Empty(contents.Warnings);
        Assert.Equal(Units.Millimeters, contents.Units);
        var copy = contents.Drawing;
        Assert.NotSame(drawing, copy);
        Assert.Equal(NestWriter.GetProgramText(drawing.Program), NestWriter.GetProgramText(copy.Program));
        Assert.Equal(NestWriter.GetSubProgramsText(drawing.Program), NestWriter.GetSubProgramsText(copy.Program));
        Assert.Equal(drawing.Name, copy.Name);
        Assert.Equal(drawing.Customer, copy.Customer);
        Assert.Equal((drawing.Material.Name, drawing.Material.Grade, drawing.Material.Density),
            (copy.Material.Name, copy.Material.Grade, copy.Material.Density));
        Assert.Equal(drawing.Source.Offset, copy.Source.Offset);
        Assert.Equal(drawing.SourceEntities.Select(e => (e.Id, e.Type, e.Layer?.Name)),
            copy.SourceEntities.Select(e => (e.Id, e.Type, e.Layer?.Name)));
        Assert.True(drawing.SuppressedEntityIds.SetEquals(copy.SuppressedEntityIds));
        var bend = Assert.Single(copy.Bends);
        Assert.Equal((new Vector(0, 3), new Vector(10, 3), "UP 90"), (bend.StartPoint, bend.EndPoint, bend.NoteText));
        // A library drawing has no nest demand of its own.
        Assert.Equal(0, copy.Quantity.Required);
    }

    [Fact]
    public void Package_DoesNotChangeTheDrawing()
    {
        var drawing = DrawingPackageFixture.Build();
        var program = NestWriter.GetProgramText(drawing.Program);

        DrawingPackage.Write(drawing, Units.Inches);

        Assert.Equal(12, drawing.Quantity.Required);
        Assert.Equal(program, NestWriter.GetProgramText(drawing.Program));
        Assert.Equal("Synthetic bracket", drawing.Name);
    }

    [Fact]
    public void Package_IsCanonical_SameContentSameBytes()
    {
        // Source entity ids are content, so the same drawing instance is written twice.
        var drawing = DrawingPackageFixture.Build();
        var first = DrawingPackage.Write(drawing, Units.Inches);
        drawing.Quantity.Required = 40;
        Thread.Sleep(2100); // ZIP times have two-second resolution; prove they are not recorded.

        var second = DrawingPackage.Write(drawing, Units.Inches);

        Assert.Equal(Sha(first), Sha(second));
        Assert.Equal(first, DrawingPackage.Write(DrawingPackage.Read(first).Drawing, Units.Inches));
        using var archive = new ZipArchive(new MemoryStream(first));
        Assert.All(archive.Entries, e => Assert.Equal(new DateTime(1980, 1, 1), e.LastWriteTime.DateTime));
        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("bestfits/", StringComparison.Ordinal));
    }

    [Fact]
    public void Package_ChangedContentOrUnits_ChangesBytes()
    {
        var drawing = DrawingPackageFixture.Build();
        var baseline = Sha(DrawingPackage.Write(drawing, Units.Inches));

        Assert.NotEqual(baseline, Sha(DrawingPackage.Write(drawing, Units.Millimeters)));
        drawing.Customer = "Other customer";
        var renamed = Sha(DrawingPackage.Write(drawing, Units.Inches));
        Assert.NotEqual(baseline, renamed);
        drawing.Bends[0].EndPoint = new Vector(10, 3.5);
        Assert.NotEqual(renamed, Sha(DrawingPackage.Write(drawing, Units.Inches)));
    }

    [Fact]
    public void Write_RejectsUndefinedUnits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawingPackage.Write(DrawingPackageFixture.Build(), (Units)9));
    }

    [Fact]
    public void Read_RefusesNestsThatAreNotOneDrawingWithoutPlates()
    {
        var empty = new Nest("empty");
        var two = new Nest("two");
        two.Drawings.Add(DrawingPackageFixture.Build());
        var second = DrawingPackageFixture.Build();
        second.Name = "Second";
        two.Drawings.Add(second);
        // The writer omits an empty plate, so the plate carries a part.
        var withPlate = new Nest("plate");
        var placed = DrawingPackageFixture.Build();
        withPlate.Drawings.Add(placed);
        var plate = new Plate(48, 96);
        plate.Parts.Add(new Part(placed));
        withPlate.Plates.Add(plate);

        Assert.Throws<InvalidDataException>(() => DrawingPackage.Read(Archive(empty)));
        Assert.Throws<InvalidDataException>(() => DrawingPackage.Read(Archive(two)));
        Assert.Throws<InvalidDataException>(() => DrawingPackage.Read(Archive(withPlate)));
    }

    [Theory]
    [InlineData("Feet")]
    [InlineData("1")]
    [InlineData("Inches,Millimeters")]
    [InlineData("")]
    public void Read_RefusesUnsupportedUnits(string units)
    {
        var package = EditNestJson(DrawingPackage.Write(DrawingPackageFixture.Build(), Units.Inches),
            root => root["units"] = units);

        Assert.Throws<InvalidDataException>(() => DrawingPackage.Read(package));
    }

    [Fact]
    public void Read_RefusesMalformedArchives()
    {
        var valid = DrawingPackage.Write(DrawingPackageFixture.Build(), Units.Inches);
        var noNestJson = Rewrite(valid, name => name == "nest.json" ? null : name);
        var badJson = Rewrite(valid, name => name, (name, bytes) => name == "nest.json" ? Encoding.UTF8.GetBytes("{") : bytes);

        Assert.Throws<InvalidDataException>(() => DrawingPackage.Read(Encoding.UTF8.GetBytes("not a zip")));
        Assert.Throws<InvalidDataException>(() => DrawingPackage.Read(noNestJson));
        Assert.Throws<InvalidDataException>(() => DrawingPackage.Read(badJson));
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static byte[] Archive(Nest nest)
    {
        using var stream = new MemoryStream();
        new NestWriter(nest).Write(stream);
        return stream.ToArray();
    }

    private static byte[] EditNestJson(byte[] package, Action<JsonObject> edit) =>
        Rewrite(package, name => name, (name, bytes) =>
        {
            if (name != "nest.json") return bytes;
            var root = JsonNode.Parse(Encoding.UTF8.GetString(bytes))!.AsObject();
            edit(root);
            return Encoding.UTF8.GetBytes(root.ToJsonString());
        });

    private static byte[] Rewrite(byte[] package, Func<string, string?> rename, Func<string, byte[], byte[]>? transform = null)
    {
        using var source = new ZipArchive(new MemoryStream(package));
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var name = rename(entry.FullName);
                if (name is null) continue;
                using var input = entry.Open();
                using var copy = new MemoryStream();
                input.CopyTo(copy);
                var bytes = transform is null ? copy.ToArray() : transform(entry.FullName, copy.ToArray());
                using var stream = target.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }
        return output.ToArray();
    }
}
