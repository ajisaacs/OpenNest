using OpenNest.CNC;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.IO;
using CncProgram = OpenNest.CNC.Program;

namespace OpenNest.Server.Tests;

/// <summary>
/// The whole store path: a code-built drawing is packaged, uploaded through the real
/// client to the production routes, found by its filters, downloaded and read back.
/// </summary>
public sealed class DrawingLibraryEndToEndTests
{
    private static Drawing SyntheticDrawing(string name, double holeX)
    {
        var program = new CncProgram();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(0, 6)));
        program.Codes.Add(new LinearMove(new Vector(10, 6)));
        program.Codes.Add(new LinearMove(new Vector(10, 0)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        program.Codes.Add(new RapidMove(new Vector(holeX, 2)));
        program.Codes.Add(new LinearMove(new Vector(holeX + 2, 2)));
        program.Codes.Add(new LinearMove(new Vector(holeX + 2, 4)));
        program.Codes.Add(new LinearMove(new Vector(holeX, 4)));
        program.Codes.Add(new LinearMove(new Vector(holeX, 2)));
        return new Drawing(name, program)
        {
            Customer = "Synthetic customer",
            Material = new Material("Synthetic Steel", "Grade A"),
        };
    }

    private static DrawingLibraryRecord Metadata(Drawing drawing, CutType cutType, double thickness) => new()
    {
        Id = Guid.NewGuid(),
        Name = drawing.Name,
        Customer = drawing.Customer,
        MaterialName = drawing.Material.Name,
        MaterialGrade = drawing.Material.Grade,
        Thickness = thickness,
        Units = Units.Inches,
        CutType = cutType,
        Notes = "Synthetic library drawing",
        SavedBy = "Synthetic operator",
    };

    [Fact]
    public async Task PackagedDrawing_UploadsFindsAndReadsBackExactly()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var library = new RemoteDrawingLibrary(client, client.BaseAddress!.ToString());
        var drawing = SyntheticDrawing("Synthetic bracket", 2);
        var package = DrawingPackage.Write(drawing, Units.Inches);
        var record = Metadata(drawing, CutType.Laser, 0.25);
        var decoyCut = Metadata(SyntheticDrawing("Synthetic decoy cut", 3), CutType.Waterjet, 0.25);
        var decoyThickness = Metadata(SyntheticDrawing("Synthetic decoy thickness", 4), CutType.Laser, 0.1875);

        var saved = await library.UploadAsync(record, package);
        await library.UploadAsync(decoyCut, DrawingPackage.Write(SyntheticDrawing(decoyCut.Name, 3), Units.Inches));
        await library.UploadAsync(decoyThickness, DrawingPackage.Write(SyntheticDrawing(decoyThickness.Name, 4), Units.Inches));
        var page = await library.QueryAsync(new DrawingLibraryQuery
        {
            CutType = CutType.Laser,
            MaterialName = " synthetic  STEEL",
            MaterialGrade = "grade a",
            Thickness = 0.25,
        });
        var found = Assert.Single(page.Items);
        var downloaded = await library.GetPackageAsync(found.Id);
        var contents = DrawingPackage.Read(downloaded!);

        Assert.Equal(record.Id, found.Id);
        Assert.Equal(1, saved.Revision);
        Assert.Equal(package, downloaded);
        Assert.Equal(Units.Inches, contents.Units);
        Assert.Empty(contents.Warnings);
        Assert.Equal(NestWriter.GetProgramText(drawing.Program), NestWriter.GetProgramText(contents.Drawing.Program));
        Assert.Equal(NestWriter.GetSubProgramsText(drawing.Program), NestWriter.GetSubProgramsText(contents.Drawing.Program));
        // The record owns the name; the canonical package leaves it blank.
        Assert.Equal("Synthetic bracket", found.Name);
        Assert.Equal("", contents.Drawing.Name);
    }

    [Fact]
    public async Task StaleUpdate_ConflictsAndLeavesTheCurrentRevision()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var library = new RemoteDrawingLibrary(client, client.BaseAddress!.ToString());
        var drawing = SyntheticDrawing("Synthetic bracket", 2);
        var record = Metadata(drawing, CutType.Laser, 0.25);
        var original = DrawingPackage.Write(drawing, Units.Inches);
        await library.UploadAsync(record, original);
        var moved = DrawingPackage.Write(SyntheticDrawing(drawing.Name, 5), Units.Inches);
        var current = await library.UpdatePackageAsync(record.Id, record, moved, 1);

        var stale = DrawingPackage.Write(SyntheticDrawing(drawing.Name, 6), Units.Inches);
        var conflict = await Assert.ThrowsAsync<DrawingLibraryConflictException>(() =>
            library.UpdatePackageAsync(record.Id, record, stale, 1));

        Assert.Equal(2, current.Revision);
        Assert.Equal(2, conflict.Current.Revision);
        var after = await library.GetAsync(record.Id);
        Assert.Equal(2, after!.Revision);
        Assert.Equal(current.PackageSha256, after.PackageSha256);
        Assert.Equal(moved, await library.GetPackageAsync(record.Id));
        Assert.Equal(original, await library.GetRevisionPackageAsync(record.Id, 1));
        var history = await library.GetRevisionsAsync(record.Id);
        Assert.Equal(new[] { 2, 1 }, history!.Items.Select(r => r.Revision));
    }

    private static Nest SyntheticNest() => new("Synthetic job")
    {
        Units = Units.Inches,
        Thickness = 0.25,
        Material = new Material("Synthetic Steel", "Grade A"),
    };

    [Fact]
    public async Task SaveSession_CreatesThenDefaultsToTheNextRevisionOfTheDuplicate()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var library = new RemoteDrawingLibrary(client, client.BaseAddress!.ToString());
        var nest = SyntheticNest();
        var drawing = SyntheticDrawing("Synthetic bracket", 2);
        nest.Drawings.Add(drawing);
        var first = new DrawingLibrarySaveSession(library, client.BaseAddress!.ToString(), nest, drawing);
        var metadata = first.Prefill(CutType.Laser, "Synthetic operator");

        var none = await first.FindDuplicatesAsync(metadata);
        var created = await first.SaveAsync(metadata, DrawingPackage.Write(drawing, nest.Units), none.Default);

        // Another nest's copy, moved hole, same details: D4 offers revision 2 by default.
        var otherNest = SyntheticNest();
        var copy = SyntheticDrawing("Synthetic bracket", 5);
        otherNest.Drawings.Add(copy);
        var second = new DrawingLibrarySaveSession(library, client.BaseAddress!.ToString(), otherNest, copy);
        var again = second.Prefill(CutType.Laser, "Synthetic operator");
        var duplicates = await second.FindDuplicatesAsync(again);
        var package = DrawingPackage.Write(copy, otherNest.Units);
        var revised = await second.SaveAsync(again, package, duplicates.Default);

        Assert.Empty(none.Matches);
        Assert.Equal((DrawingLibrarySaveOutcome.Created, 1), (created.Outcome, created.Saved.Revision));
        Assert.Equal(created.Saved.Id, duplicates.Default!.Id);
        Assert.Equal((DrawingLibrarySaveOutcome.Revised, 2), (revised.Outcome, revised.Saved.Revision));
        Assert.Equal((created.Saved.Id, 2), (copy.Library!.Id, copy.Library.Revision));
        Assert.Equal((created.Saved.Id, 1), (drawing.Library!.Id, drawing.Library.Revision));
        Assert.Equal(package, await library.GetPackageAsync(created.Saved.Id));
        Assert.Equal(revised.Saved.PackageSha256, copy.Library.PackageSha256);
    }

    [Fact]
    public async Task SaveSession_StaleChoiceConflictsAndChangesNothing()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var library = new RemoteDrawingLibrary(client, client.BaseAddress!.ToString());
        var nest = SyntheticNest();
        var drawing = SyntheticDrawing("Synthetic bracket", 2);
        nest.Drawings.Add(drawing);
        var session = new DrawingLibrarySaveSession(library, client.BaseAddress!.ToString(), nest, drawing);
        var metadata = session.Prefill(CutType.Laser, "Synthetic operator");
        await session.SaveAsync(metadata, DrawingPackage.Write(drawing, nest.Units), null);
        var duplicates = await session.FindDuplicatesAsync(metadata);
        var moved = DrawingPackage.Write(SyntheticDrawing("Synthetic bracket", 5), Units.Inches);
        var current = await library.UpdatePackageAsync(duplicates.Default!.Id, metadata, moved, 1);

        var conflict = await Assert.ThrowsAsync<DrawingLibraryConflictException>(() =>
            session.SaveAsync(metadata, DrawingPackage.Write(SyntheticDrawing("Synthetic bracket", 6), Units.Inches), duplicates.Default));

        Assert.Equal(2, conflict.Current.Revision);
        Assert.False(session.HasPendingSave);
        Assert.Equal(1, drawing.Library!.Revision);
        Assert.Equal(current.PackageSha256, (await library.GetAsync(current.Id))!.PackageSha256);
        Assert.Equal(moved, await library.GetPackageAsync(current.Id));
    }

    [Fact]
    public async Task ArchiveRestoreAndMetadata_ThroughTheClient()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var library = new RemoteDrawingLibrary(client, client.BaseAddress!.ToString());
        var drawing = SyntheticDrawing("Synthetic bracket", 2);
        var record = Metadata(drawing, CutType.Plasma, 0.5);
        var package = DrawingPackage.Write(drawing, Units.Inches);
        await library.UploadAsync(record, package);

        record.Notes = "Edited notes";
        var edited = await library.UpdateMetadataAsync(record.Id, record, 1);
        var archived = await library.ArchiveAsync(record.Id, edited.Revision);
        var hidden = await library.QueryAsync(new DrawingLibraryQuery());
        var listed = await library.QueryAsync(new DrawingLibraryQuery { IncludeArchived = true });
        var restored = await library.RestoreAsync(record.Id, archived.Revision);
        var duplicate = await Assert.ThrowsAsync<DrawingLibraryConflictException>(() => library.UploadAsync(record, package));

        Assert.Equal("Edited notes", edited.Notes);
        Assert.True(archived.Archived);
        Assert.Empty(hidden.Items);
        Assert.Single(listed.Items);
        Assert.False(restored.Archived);
        Assert.Equal(4, restored.Revision);
        Assert.Equal(4, duplicate.Current.Revision);
        Assert.Equal(package, await library.GetPackageAsync(record.Id));
    }
}
