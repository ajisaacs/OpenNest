using OpenNest.CNC.CuttingStrategy;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Shapes;

namespace OpenNest.Server.Tests;

/// <summary>
/// The whole release path: code-built drawings in two materials are packaged into the
/// library, collected into a set, released ×2 through the real clients, and the nests and
/// release record are read back from the production routes.
/// </summary>
public sealed class SetReleaseEndToEndTests
{
    private static Drawing SyntheticDrawing(string name, double length)
    {
        var drawing = new RectangleShape { Name = name, Length = length, Width = 2 }.GetDrawing();
        drawing.CutPins.Add(new ContourCutPin(new Vector(length, 2), new LineLeadIn { Length = 0.25 }, new NoLeadOut(),
            new NormalTab { Size = 0.03 }));
        return drawing;
    }

    private static async Task<DrawingLibraryRecord> Store(RemoteDrawingLibrary library, string name, double length,
        string material, CutType cutType)
    {
        var record = new DrawingLibraryRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            Customer = "Synthetic customer",
            MaterialName = material,
            MaterialGrade = "Grade A",
            Thickness = 0.25,
            Units = Units.Inches,
            CutType = cutType,
            SavedBy = "Synthetic operator",
        };
        return await library.UploadAsync(record, DrawingPackage.Write(SyntheticDrawing(name, length), Units.Inches));
    }

    private static byte[] Write(Nest nest)
    {
        using var stream = new MemoryStream();
        new NestWriter(nest).Write(stream);
        return stream.ToArray();
    }

    private static Nest Read(byte[] archive)
    {
        using var stream = new MemoryStream(archive, writable: false);
        return new NestReader(stream).Read();
    }

    [Fact]
    public async Task Release_MakesOneDraftNestPerMaterialAndRecordsThem()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var url = client.BaseAddress!.ToString();
        using var library = new RemoteDrawingLibrary(client, url);
        using var sets = new RemoteDrawingSetStore(client, url);
        using var nests = new RemoteNestRepository(client, url);
        var a = await Store(library, "SYN-A", 4, "Synthetic Steel", CutType.Laser);
        var b = await Store(library, "SYN-B", 5, "Synthetic Steel", CutType.Laser);
        var c = await Store(library, "SYN-C", 6, "Synthetic Aluminum", CutType.Waterjet);
        var created = await sets.CreateAsync(new DrawingSetRecord
        {
            Id = Guid.NewGuid(),
            Name = "Synthetic kit",
            Customer = "Synthetic set customer",
            Members = new[] { (a, 3), (b, 1), (c, 2) }
                .Select(m => new DrawingSetMember { DrawingId = m.Item1.Id, Quantity = m.Item2 }).ToList(),
        });

        var set = await sets.GetAsync(created.Id);
        var plan = SetReleasePlan.Build(set!, 2, includeArchived: false);
        var number = 0;
        var session = new SetReleaseSession(sets, library, nests, url,
            bytes =>
            {
                var contents = DrawingPackage.Read(bytes);
                return new LibraryPackage(bytes, contents.Drawing, contents.Units, contents.Warnings);
            },
            Write,
            (group, _) => Task.FromResult(new Nest($"Synthetic release {++number}") { Units = group.Units }));

        var result = await session.RunAsync(plan, plan.Groups, "Synthetic operator", "Synthetic release notes");

        Assert.Equal("", result.ReleaseError);
        Assert.Equal(2, result.Created.Count);
        foreach (var group in result.Groups)
        {
            var stored = Read((await nests.GetFileAsync(group.Record!.Id))!);
            Assert.Equal(NestStatus.Draft, (await nests.GetMetadataAsync(group.Record.Id))!.Status);
            Assert.Equal((NestStatus.Draft, group.Group.MaterialName, 0.25, "Synthetic set customer"),
                (stored.Status, stored.Material.Name, stored.Thickness, stored.Customer));
            foreach (var item in group.Group.Items)
            {
                var drawing = stored.Drawings.Single(d => d.Name == item.Drawing.Name);
                Assert.Equal(item.Member.Quantity * 2, drawing.Quantity.Required);
                Assert.Equal((item.Drawing.Id, 1, url.TrimEnd('/')), (drawing.Library!.Id, drawing.Library.Revision, drawing.Library.ServerKey));
                Assert.Single(drawing.CutPins);
            }
        }
        var steel = result.Groups.Single(g => g.Group.MaterialName == "Synthetic Steel");
        Assert.Equal(new[] { "SYN-A", "SYN-B" }, steel.Group.Items.Select(i => i.Drawing.Name));

        var releases = await sets.GetReleasesAsync(created.Id);
        var release = Assert.Single(releases!.Items);
        Assert.Equal((result.Release!.Id, 1, 2, "Synthetic release notes"),
            (release.Id, release.SetRevision, release.Multiplier, release.Notes));
        Assert.Equal(result.Groups.Select(g => g.Record!.Id), release.Nests.Select(n => n.NestId));
        Assert.All(release.Nests, n => Assert.Equal(NestStatus.Draft, n.Nest!.Status));
        Assert.Equal(CutType.Waterjet, release.Nests.Single(n => n.MaterialName == "Synthetic Aluminum").CutType);
        Assert.Equal(release.Id, (await sets.GetReleaseForNestAsync(steel.Record!.Id))!.Id);
    }
}
