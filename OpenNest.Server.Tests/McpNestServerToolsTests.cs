extern alias mcp;

using mcp::OpenNest.Mcp.Database;
using mcp::OpenNest.Mcp.Tools;
using OpenNest.Data;
using OpenNest.Desktop;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Shapes;

namespace OpenNest.Server.Tests;

/// <summary>
/// The MCP nest server tools against the in-process server: sets made from library drawings
/// by name, a release planned and made into nested, server-named Draft nests on the best of
/// several sheet sizes, a merge into a draft, a guarded re-nest and undo.
/// </summary>
public sealed class McpNestServerToolsTests
{
    private sealed class Server : IDisposable
    {
        public Server()
        {
            Factory = new ServerFactory();
            Client = Factory.CreateClient();
            Url = Client.BaseAddress!.ToString();
            Library = new RemoteDrawingLibrary(Client, Url);
            Sets = new RemoteDrawingSetStore(Client, Url);
            Nests = new RemoteNestRepository(Client, Url);
            var directory = Path.Combine(Factory.DirectoryPath, "desktop");
            Directory.CreateDirectory(directory);
            var userSettings = Path.Combine(directory, "user-settings.json");
            var machines = new MachineSettingsSource(
                new SharedSettingsSource(() => new NestStorageSettings(), _ => throw new InvalidOperationException("File mode")),
                new MachineUserSettingsFile(userSettings), Path.Combine(directory, "Machines"),
                Path.Combine(directory, "defaults.json"), Path.Combine(directory, "clamps.json"),
                Path.Combine(directory, "grid.json"));
            var newNests = new NewNestSource(machines, new NewNestUserSettingsFile(userSettings), () => Nests);
            Context = new NestServerContext(Url, Library, Sets, Nests, group => newNests.CreateAsync(group),
                () => "Rectangles", null, "Synthetic operator");
            Tools = new NestServerTools(Context);
        }

        public ServerFactory Factory { get; }
        public HttpClient Client { get; }
        public string Url { get; }
        public RemoteDrawingLibrary Library { get; }
        public RemoteDrawingSetStore Sets { get; }
        public RemoteNestRepository Nests { get; }
        public NestServerContext Context { get; }
        public NestServerTools Tools { get; }

        public async Task<DrawingLibraryRecord> StoreAsync(string name, double length, string material)
        {
            var drawing = new RectangleShape { Name = name, Length = length, Width = 2 }.GetDrawing();
            return await Library.UploadAsync(new DrawingLibraryRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                Customer = "Synthetic customer",
                MaterialName = material,
                MaterialGrade = "Grade A",
                Thickness = 0.25,
                Units = Units.Inches,
                CutType = CutType.Laser,
                SavedBy = "Synthetic operator",
            }, DrawingPackage.Write(drawing, Units.Inches));
        }

        public async Task<Nest> ReadAsync(Guid id)
        {
            using var stream = new MemoryStream((await Nests.GetFileAsync(id))!, writable: false);
            return new NestReader(stream).Read();
        }

        public void Dispose()
        {
            Nests.Dispose();
            Sets.Dispose();
            Library.Dispose();
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private static DrawingQuantityInput Member(string drawing, int quantity) => new() { Drawing = drawing, Quantity = quantity };

    [Fact]
    public async Task Sets_AreMadeFromDrawingNames_AndSavedOverTheirCurrentRevision()
    {
        using var server = new Server();
        var a = await server.StoreAsync("SYN-A", 4, "Synthetic Steel");
        await server.StoreAsync("SYN-B", 5, "Synthetic Steel");

        var created = await server.Tools.CreateSet("Synthetic kit", new[] { Member("syn-a", 3), Member("SYN-B", 1) },
            "Synthetic set customer", "Synthetic notes");
        Assert.StartsWith("Created set.", created);
        var set = (await server.Sets.QueryAsync(new DrawingSetQuery { Search = "Synthetic kit" })).Items.Single();
        Assert.Contains($"id {set.Id}", created);
        Assert.Contains("x3 SYN-A", created);

        Assert.StartsWith("Error: A set named 'Synthetic kit' already exists",
            await server.Tools.CreateSet("synthetic kit", new[] { Member("SYN-A", 1) }));
        Assert.StartsWith("Error: No library drawing is named 'SYN-Z'",
            await server.Tools.CreateSet("Other kit", new[] { Member("SYN-Z", 1) }));
        Assert.StartsWith("Error:", await server.Tools.CreateSet("Other kit", new[] { Member("SYN-A", 1), Member(a.Id.ToString(), 2) }));

        var updated = await server.Tools.UpdateSet("Synthetic kit", notes: "Changed notes",
            members: new[] { Member(a.Id.ToString(), 5) });
        Assert.StartsWith("Saved revision 2.", updated);
        var stored = (await server.Sets.GetAsync(set.Id))!;
        Assert.Equal(("Changed notes", "Synthetic set customer", 5), (stored.Notes, stored.Customer, stored.Members.Single().Quantity));

        Assert.StartsWith("Archived 'Synthetic kit'", await server.Tools.ArchiveSet(set.Id.ToString()));
        Assert.True((await server.Sets.GetAsync(set.Id))!.Archived);
        Assert.Contains("already archived", await server.Tools.ArchiveSet("Synthetic kit"));
        Assert.StartsWith("Restored 'Synthetic kit'", await server.Tools.RestoreSet("Synthetic kit"));
        Assert.False((await server.Sets.GetAsync(set.Id))!.Archived);

        var found = await server.Tools.SearchDrawings(search: "SYN", material: "Synthetic Steel");
        Assert.StartsWith("2 of 2 drawings", found);
        Assert.Contains("In 1 set(s):", await server.Tools.GetDrawing("SYN-A"));
    }

    [Fact]
    public async Task MakeNests_ReleasesNestedServerNamedDrafts_MergesRenestsAndUndoes()
    {
        using var server = new Server();
        var a = await server.StoreAsync("SYN-A", 4, "Synthetic Steel");
        var b = await server.StoreAsync("SYN-B", 5, "Synthetic Steel");
        var c = await server.StoreAsync("SYN-C", 6, "Synthetic Aluminum");
        Assert.StartsWith("Created set.", await server.Tools.CreateSet("Synthetic kit",
            new[] { Member("SYN-A", 3), Member("SYN-B", 1), Member("SYN-C", 2) }, "Synthetic set customer"));
        var kit = new[] { new ReleaseSetInput { Set = "Synthetic kit", Quantity = 2 } };
        var steelKey = SetReleasePlan.GroupKey(a);
        var aluminumKey = SetReleasePlan.GroupKey(c);

        var plan = await server.Tools.PlanRelease(kit);
        Assert.Contains($"Group {steelKey}: can be released", plan);
        Assert.Contains($"Group {aluminumKey}: can be released", plan);
        Assert.Contains("SYN-A x 6", plan);
        Assert.Contains("Drafts to merge into: none", plan);
        Assert.Empty((await server.Nests.ListAsync()));

        Assert.StartsWith("Error: Sheet size 'big'", await server.Tools.MakeNests(kit, sheetSizes: new[] { "big" }));
        Assert.StartsWith("Error: Unknown engine", await server.Tools.MakeNests(kit, engine: "No such engine"));
        Assert.Empty((await server.Nests.ListAsync()));

        var made = await server.Tools.MakeNests(kit, notes: "Synthetic job", sheetSizes: new[] { "12x12", "48x48" });
        var release = (await server.Sets.QueryReleasesAsync()).Items.Single();
        Assert.StartsWith($"Release {release.Id} recorded (2 nest(s)).", made);
        Assert.Equal("Synthetic job", release.Notes);
        Assert.Contains("Sheet sizes tried (chose ", made);
        var year = DateTime.Now.Year;
        var names = new HashSet<string>();
        foreach (var released in release.Nests)
        {
            var record = (await server.Nests.GetMetadataAsync(released.NestId))!;
            names.Add(record.Name);
            Assert.Equal(NestStatus.Draft, record.Status);
            Assert.Contains($"Nest {record.Name} | id {record.Id} | revision 1 | new", made);
            var nest = await server.ReadAsync(record.Id);
            Assert.NotEmpty(nest.Plates);
            Assert.All(nest.Drawings, d => Assert.Equal(0, d.Quantity.Remaining));
            Assert.All(nest.Plates, p => Assert.Contains(SheetSizes.Format(p.Size), new[] { "12x12", "48x48" }));
        }
        Assert.Equal(new HashSet<string> { NestNames.Format(year, 1), NestNames.Format(year, 2) }, names);
        var steelId = release.Nests.Single(n => n.MaterialName == "Synthetic Steel").NestId;
        var steelName = (await server.Nests.GetMetadataAsync(steelId))!.Name;

        // A drawing on its own merges into the steel draft, which the plan offers.
        Assert.Contains($"Drafts to merge into (optional): {steelName} id {steelId} rev 1",
            await server.Tools.PlanRelease(drawings: new[] { Member("SYN-A", 1) }));
        var merged = await server.Tools.MakeNests(drawings: new[] { Member("SYN-A", 1) },
            merge: new[] { new MergeInput { Group = steelKey, Draft = steelName } });
        Assert.Contains($"Nest {steelName} | id {steelId} | revision 2 | merged into revision 1", merged);
        Assert.Equal(7, (await server.ReadAsync(steelId)).Drawings.Single(d => d.Name == "SYN-A").Quantity.Required);
        var mergeRelease = (await server.Sets.QueryReleasesAsync(drawingId: a.Id)).Items.Single();

        // Too small a sheet leaves parts unplaced, so nothing is saved.
        var tooSmall = await server.Tools.RenestNest(steelName, new[] { "3x3" });
        Assert.Contains("Not saved", tooSmall);
        Assert.Equal(2, (await server.Nests.GetMetadataAsync(steelId))!.Revision);
        Assert.Contains("Not saved (save=false)", await server.Tools.RenestNest(steelId.ToString(), new[] { "48x48" }, save: false));
        Assert.Equal(2, (await server.Nests.GetMetadataAsync(steelId))!.Revision);

        var renested = await server.Tools.RenestNest(steelName, new[] { "48x48" });
        Assert.Contains("Saved revision 3", renested);
        var resized = await server.ReadAsync(steelId);
        Assert.All(resized.Plates, p => Assert.Equal("48x48", SheetSizes.Format(p.Size)));
        Assert.All(resized.Drawings, d => Assert.Equal(0, d.Quantity.Remaining));
        Assert.Equal(NestStatus.Draft, resized.Status);
        var summary = await server.Tools.NestSummary(steelName);
        Assert.Contains("revision 3 | Draft", summary);
        Assert.Contains("SYN-A: 7 / 7", summary);
        Assert.Contains("SYN-B: 2 / 2", summary);
        // The merged draft changed after the merge, so undo is refused until it is skipped.
        var refused = await server.Tools.UndoRelease(mergeRelease.Id.ToString());
        Assert.StartsWith("Error: nothing was undone", refused);
        Assert.Contains(steelId.ToString(), refused);
        Assert.StartsWith("Error:", await server.Tools.UndoRelease(release.Id.ToString()));
        var undone = await server.Tools.UndoRelease(release.Id.ToString(), new[] { steelId.ToString() });
        Assert.StartsWith("Undone.", undone);
        var aluminumId = release.Nests.Single(n => n.MaterialName == "Synthetic Aluminum").NestId;
        Assert.Null(await server.Nests.GetMetadataAsync(aluminumId));
        Assert.Equal(3, (await server.Nests.GetMetadataAsync(steelId))!.Revision);
        Assert.Contains("undo: Deleted", await server.Tools.GetRelease(release.Id.ToString()));
        _ = b;
    }

    [Fact]
    public async Task MakeNests_RefusesEveryGroupWhenOneCannotBeReleased()
    {
        using var server = new Server();
        await server.StoreAsync("SYN-A", 4, "Synthetic Steel");
        var unassigned = await server.StoreAsync("SYN-U", 4, "");

        var result = await server.Tools.MakeNests(drawings: new[] { Member("SYN-A", 1), Member(unassigned.Id.ToString(), 1) });

        Assert.StartsWith("Error: nothing was released.", result);
        Assert.Contains("no material or thickness", result);
        Assert.Empty(await server.Nests.ListAsync());
        var partial = await server.Tools.MakeNests(drawings: new[] { Member("SYN-A", 1), Member(unassigned.Id.ToString(), 1) },
            skipRefusedGroups: true, nestAfterRelease: false);
        Assert.Contains("recorded (1 nest(s))", partial);
        Assert.Contains("not released:", partial);
        Assert.Single(await server.Nests.ListAsync());
    }

    [Fact]
    public void SheetSizes_ParseWidthByLength_AndRejectOthers()
    {
        var sizes = SheetSizes.Parse(new[] { "60x120", " 72 X 144 ", "60*120", "12.5x48" }, out var error);
        Assert.Null(error);
        Assert.Equal(new[] { "60x120", "72x144", "12.5x48" }, sizes.Select(SheetSizes.Format));
        foreach (var bad in new[] { "60", "0x10", "-1x5", "ax5", "1x2x3", "NaNx4" })
        {
            Assert.Null(SheetSizes.Parse(new[] { bad }, out error));
            Assert.Contains(bad, error);
        }
        Assert.Empty(SheetSizes.Parse(null, out _));
    }

    [Fact]
    public void SheetTrials_RankKeptThenUnplacedThenSheetsThenArea()
    {
        var choice = SheetChoice.Single(new Size(60, 120));
        SheetTrial Trial(bool kept, int unplaced, int sheets, double area) => new(choice, kept, unplaced, sheets, area, 0, "");
        var trials = new List<SheetTrial>
        {
            Trial(false, 0, 0, 0),
            Trial(true, 0, 3, 3 * 7200),
            Trial(true, 1, 1, 7200),
            Trial(true, 0, 1, 72 * 144),
            Trial(true, 0, 1, 7200),
        };
        trials.Sort(SheetTrial.Compare);
        Assert.Equal(new[] { (true, 0, 1, 7200.0), (true, 0, 1, 72 * 144.0), (true, 0, 3, 3 * 7200.0), (true, 1, 1, 7200.0), (false, 0, 0, 0.0) },
            trials.Select(t => (t.Kept, t.Unplaced, t.Sheets, t.SheetArea)));
    }

    [Fact]
    public async Task Tools_ReportWhenThereIsNoNestServer()
    {
        var tools = new NestServerTools(NestServerContextWithoutServer());
        Assert.StartsWith("Error: OpenNest is not in Database mode", await tools.SearchSets());
    }

    // File mode, as storage.json says when the desktop has no nest server.
    private static NestServerContext NestServerContextWithoutServer() => NestServerContext.FromStorage(new NestStorageSettings());
}
