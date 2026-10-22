using System.Security.Cryptography;
using System.Text;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.Shapes;
using static OpenNest.Tests.Data.SetReleasePlanTests;

namespace OpenNest.Tests.Data;

public class SetReleaseSessionTests
{
    private const string Server = "http://library.test:8090";

    private readonly FakeSets _sets = new();
    private readonly FakeLibrary _library = new();
    private readonly FakeNests _nests = new();
    private readonly List<SetReleaseGroup> _created = new();

    private DrawingLibraryRecord Stored(string name, string material = "Synthetic Steel", CutType cutType = CutType.Laser,
        Units units = Units.Inches)
    {
        var record = Drawing(name, material: material, cutType: cutType, units: units);
        var bytes = Encoding.UTF8.GetBytes($"package {record.Id}");
        record.PackageSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        _library.Packages[(record.Id, record.Revision)] = (bytes, units);
        return record;
    }

    private SetReleaseSession Session(Func<SetReleaseGroup, CancellationToken, Task<Nest>>? createNest = null,
        Func<Nest, CancellationToken, Task<SetReleaseRenest>>? renest = null) =>
        new(_sets, _library, _nests, Server + "/", ReadPackage, nest => Encoding.UTF8.GetBytes("nest " + nest.Name),
            createNest ?? CreateNest, bytes => _nests.Archives[Encoding.UTF8.GetString(bytes)], renest);

    // A draft on the fake server holding `existing` (copied in from the library) on one placed sheet.
    private (NestRecord Record, Nest Nest) Draft(DrawingLibraryRecord existing, int quantity = 2, int revision = 3)
    {
        var nest = new Nest("Draft 7")
        {
            Units = Units.Inches,
            Material = new Material("Synthetic Steel", "Grade A", 0.28),
            Thickness = 0.25,
            Status = NestStatus.Draft,
        };
        var package = ReadPackage(_library.Packages[(existing.Id, existing.Revision)].Bytes);
        LibraryDrawingInsert.Plan(nest, Server, existing, package, quantity).Apply();
        nest.CreatePlate().Parts.Add(new Part(nest.Drawings.Single()));
        var record = new NestRecord
        {
            Id = Guid.NewGuid(),
            Revision = revision,
            Name = nest.Name,
            Material = "Synthetic Steel",
            MaterialGrade = "Grade A",
            Thickness = 0.25,
            Status = NestStatus.Draft,
        };
        _nests.AddDraft(record, nest);
        return (record, nest);
    }

    private Task<Nest> CreateNest(SetReleaseGroup group, CancellationToken cancellationToken)
    {
        _created.Add(group);
        return Task.FromResult(new Nest($"N{_created.Count}") { Units = group.Units, Material = new Material("Default", "", 0.1) });
    }

    private LibraryPackage ReadPackage(byte[] bytes)
    {
        var (_, units) = _library.Packages.Values.Single(p => p.Bytes.AsSpan().SequenceEqual(bytes));
        var drawing = new RectangleShape { Name = "package name", Length = 4, Width = 2 }.GetDrawing();
        drawing.CutPins.Add(new ContourCutPin(new Vector(4, 2), new LineLeadIn { Length = 0.25 }, new NoLeadOut(),
            new NormalTab { Size = 0.03 }));
        return new LibraryPackage(bytes, drawing, units);
    }

    [Fact]
    public async Task Run_CreatesOneDraftNestPerGroupThenRecordsTheRelease()
    {
        var a = Stored("SYN-A");
        var b = Stored("SYN-B");
        var c = Stored("SYN-C", material: "Synthetic Aluminum", cutType: CutType.Waterjet);
        var set = Set((a, 3), (b, 1), (c, 2));
        var plan = SetReleasePlan.Build(set, 2, false);
        var progress = new RecordingProgress();
        var session = Session();

        var result = await session.RunAsync(plan, plan.Groups, "Synthetic operator", "Synthetic notes", progress);

        Assert.False(result.Canceled);
        Assert.Equal("", result.ReleaseError);
        Assert.Equal(2, result.Created.Count);
        Assert.Equal(2, _nests.Uploaded.Count);

        var steel = result.Groups.Single(g => g.Group.MaterialName == "Synthetic Steel");
        var nest = steel.Nest!;
        Assert.Equal(NestStatus.Draft, nest.Status);
        Assert.Equal(("Synthetic Steel", "Grade A", 0.25), (nest.Material.Name, nest.Material.Grade, nest.Thickness));
        Assert.Equal("Synthetic set customer", nest.Customer);
        Assert.Equal(new[] { ("SYN-A", 6), ("SYN-B", 2) },
            nest.Drawings.OrderBy(d => d.Name).Select(d => (d.Name, d.Quantity.Required)));
        Assert.All(nest.Drawings, d => Assert.Equal(Server, d.Library!.ServerKey));
        Assert.Equal(a.Revision, nest.Drawings.Single(d => d.Name == "SYN-A").Library!.Revision);
        // Material matches the new nest by construction, so the library pins are kept.
        Assert.All(nest.Drawings, d => Assert.Single(d.CutPins));
        Assert.Equal(result.Groups.Select(g => g.Record!.Id), _nests.Uploaded.Select(u => u.Record.Id));
        var uploaded = _nests.Uploaded.Single(u => u.Record.Id == steel.Record!.Id).Record;
        Assert.Equal((NestStatus.Draft, "Synthetic Steel", "N2"), (uploaded.Status, uploaded.Material, uploaded.Name));

        var release = Assert.Single(_sets.Posted);
        Assert.Same(result.Release, _sets.Stored.Single());
        var line = Assert.Single(release.Sets);
        Assert.Equal((set.Id, 4, 2, "Synthetic operator", "Synthetic notes"),
            (line.SetId, line.SetRevision, line.Quantity, release.ReleasedBy, release.Notes));
        Assert.Equal(result.Groups.Select(g => g.Record!.Id), release.Nests.Select(n => n.NestId));
        Assert.All(release.Nests, n => Assert.Equal((SetReleaseNestKind.Created, (int?)null), (n.Kind, n.RevisionBefore)));
        Assert.Equal(result.Groups.Select(g => g.Record!.Revision), release.Nests.Select(n => n.RevisionAfter));
        var steelItems = release.Nests.Single(n => n.NestId == steel.Record!.Id).Items;
        Assert.Equal(new[] { (a.Id, a.Revision, 6), (b.Id, b.Revision, 2) },
            steelItems.OrderBy(i => i.Quantity).Reverse().Select(i => (i.DrawingId, i.DrawingRevision, i.Quantity)));
        Assert.All(steelItems, i => Assert.Equal(set.Id, i.SetId));
        var aluminum = release.Nests.Single(n => n.MaterialName == "Synthetic Aluminum");
        Assert.Equal(("Grade A", 0.25, Units.Inches, CutType.Waterjet),
            (aluminum.MaterialGrade, aluminum.Thickness, aluminum.Units, aluminum.CutType));

        Assert.Equal(new[]
        {
            (SetReleaseStage.Preparing, 0), (SetReleaseStage.Preparing, 1),
            (SetReleaseStage.Uploading, 0), (SetReleaseStage.Uploading, 1), (SetReleaseStage.Recording, 2),
        }, progress.Reports.Select(p => (p.Stage, p.Index)));
        Assert.False(session.HasPendingRelease);
    }

    [Fact]
    public async Task Run_OnlyChosenGroups_AreReleased()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1), (Stored("SYN-B", cutType: CutType.Plasma), 1)), 1, false);
        var chosen = plan.Groups.Single(g => g.CutType == CutType.Plasma);

        var result = await Session().RunAsync(plan, new[] { chosen }, "", "");

        Assert.Same(chosen, Assert.Single(result.Groups).Group);
        Assert.Equal(CutType.Plasma, Assert.Single(Assert.Single(_sets.Posted).Nests).CutType);
    }

    [Fact]
    public async Task Run_PackageThatNoLongerMatchesItsRecord_RefusesBeforeAnythingIsUploaded()
    {
        var a = Stored("SYN-A");
        var b = Stored("SYN-B", material: "Synthetic Aluminum");
        // The drawing moved on after the set was read: the bytes no longer hash to the record.
        var (bytes, units) = _library.Packages[(b.Id, b.Revision)];
        _library.Packages[(b.Id, b.Revision)] = (bytes.Append((byte)1).ToArray(), units);
        var plan = SetReleasePlan.Build(Set((a, 1), (b, 1)), 1, false);

        var result = await Session().RunAsync(plan, plan.Groups, "", "");

        var refused = result.Groups.Single(g => g.Group.MaterialName == "Synthetic Aluminum");
        Assert.Equal(SetReleaseGroupOutcome.Refused, refused.Outcome);
        Assert.Contains("does not match the library record", refused.Error);
        Assert.Equal(SetReleaseGroupOutcome.NotStarted, result.Groups.Single(g => g != refused).Outcome);
        Assert.All(result.Groups, g => Assert.Null(g.Nest));
        Assert.Empty(_nests.Uploaded);
        Assert.Empty(_sets.Posted);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task Run_MissingRevision_RefusesTheGroup()
    {
        var a = Stored("SYN-A");
        _library.Packages.Clear();
        var plan = SetReleasePlan.Build(Set((a, 1)), 1, false);

        var result = await Session().RunAsync(plan, plan.Groups, "", "");

        Assert.Equal(SetReleaseGroupOutcome.Refused, result.Groups[0].Outcome);
        Assert.Contains("no longer in the library", result.Groups[0].Error);
        Assert.Empty(_nests.Uploaded);
    }

    [Fact]
    public async Task Run_FactoryNestInOtherUnits_RefusesTheGroup()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1)), 1, false);
        var session = Session((_, _) => Task.FromResult(new Nest("N1") { Units = Units.Millimeters }));

        var result = await session.RunAsync(plan, plan.Groups, "", "");

        Assert.Equal(SetReleaseGroupOutcome.Refused, result.Groups[0].Outcome);
        Assert.Contains("is in millimeters, not inches", result.Groups[0].Error);
        Assert.Empty(_nests.Uploaded);
    }

    [Fact]
    public async Task Run_DownloadFailure_FailsBeforeAnythingIsUploaded()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1)), 1, false);
        _library.Failure = new IOException("Synthetic network failure.");

        var result = await Session().RunAsync(plan, plan.Groups, "", "");

        Assert.Equal(SetReleaseGroupOutcome.Failed, result.Groups[0].Outcome);
        Assert.Contains("Synthetic network failure.", result.Groups[0].Error);
        Assert.Empty(_nests.Uploaded);
        Assert.Empty(_sets.Posted);
    }

    [Fact]
    public async Task Run_UploadFailurePartWay_ReportsCreatedNestsAndRecordsNoRelease()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1), (Stored("SYN-B", material: "Synthetic Aluminum"), 1),
            (Stored("SYN-C", cutType: CutType.Plasma), 1)), 1, false);
        _nests.FailUploadNumber = 2;

        var result = await Session().RunAsync(plan, plan.Groups, "", "");

        Assert.Equal(new[] { SetReleaseGroupOutcome.Created, SetReleaseGroupOutcome.Failed, SetReleaseGroupOutcome.NotStarted },
            result.Groups.Select(g => g.Outcome));
        Assert.Equal(_nests.Uploaded[0].Record.Id, Assert.Single(result.Created).Record!.Id);
        Assert.Contains("Synthetic upload failure.", result.Groups[1].Error);
        Assert.Contains("It was not saved.", result.Groups[1].Error);
        Assert.NotNull(result.Groups[1].Nest);
        Assert.Null(result.Groups[2].Nest);
        Assert.Empty(_sets.Posted);
        Assert.Null(result.Release);
        Assert.False(result.Canceled);
    }

    [Fact]
    public async Task Run_UploadStoredBeforeItsReplyWasLost_CountsAsCreated()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1)), 1, false);
        _nests.FailUploadNumber = 1;
        _nests.StoreFailedUpload = true;

        var result = await Session().RunAsync(plan, plan.Groups, "", "");

        Assert.Equal(SetReleaseGroupOutcome.Created, result.Groups[0].Outcome);
        Assert.Equal(_nests.Uploaded[0].Record.Id, Assert.Single(Assert.Single(_sets.Posted).Nests).NestId);
    }

    [Fact]
    public async Task Run_UploadFailureThatCannotBeChecked_SaysTheNestMayExist()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1)), 1, false);
        _nests.FailUploadNumber = 1;
        _nests.MetadataFailure = new HttpRequestException("Synthetic offline.");

        var result = await Session().RunAsync(plan, plan.Groups, "", "");

        Assert.Equal(SetReleaseGroupOutcome.Failed, result.Groups[0].Outcome);
        Assert.Contains("may have been saved as 'N1'", result.Groups[0].Error);
    }

    [Fact]
    public async Task Run_CanceledDuringAnUpload_FinishesItThenStops()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1), (Stored("SYN-B", material: "Synthetic Aluminum"), 1)), 1, false);
        using var cancel = new CancellationTokenSource();
        var progress = new RecordingProgress(p =>
        {
            if (p.Stage == SetReleaseStage.Uploading)
                cancel.Cancel();
        });

        var result = await Session().RunAsync(plan, plan.Groups, "", "", progress, cancel.Token);

        Assert.True(result.Canceled);
        Assert.Equal(new[] { SetReleaseGroupOutcome.Created, SetReleaseGroupOutcome.NotStarted }, result.Groups.Select(g => g.Outcome));
        Assert.Single(_nests.Uploaded);
        Assert.False(_nests.UploadTokens.Single().CanBeCanceled);
        Assert.Empty(_sets.Posted);
    }

    [Fact]
    public async Task Run_CanceledWhilePreparing_CreatesNothing()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1)), 1, false);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var result = await Session().RunAsync(plan, plan.Groups, "", "", null, cancel.Token);

        Assert.True(result.Canceled);
        Assert.Equal(SetReleaseGroupOutcome.NotStarted, result.Groups[0].Outcome);
        Assert.Empty(_nests.Uploaded);
    }

    [Fact]
    public async Task Run_ReleasePostFailure_KeepsTheNestsAndRetriesUnderTheSameId()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1)), 1, false);
        _sets.Failures.Enqueue(new IOException("Synthetic post failure."));
        var session = Session();

        var result = await session.RunAsync(plan, plan.Groups, "", "");

        Assert.Null(result.Release);
        Assert.Contains("Synthetic post failure.", result.ReleaseError);
        Assert.Single(result.Created);
        Assert.True(session.HasPendingRelease);

        var stored = await session.RetryReleaseAsync();

        Assert.Equal(2, _sets.Posted.Count);
        Assert.Equal(_sets.Posted[0].Id, _sets.Posted[1].Id);
        Assert.Equal(_sets.Posted[0].Id, stored.Id);
        Assert.False(session.HasPendingRelease);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RetryReleaseAsync());
    }

    [Fact]
    public async Task Run_ReleaseAlreadyStoredUnderItsId_IsAdopted()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1)), 1, false);
        _sets.ConflictWithPosted = true;

        var result = await Session().RunAsync(plan, plan.Groups, "", "");

        Assert.Equal(_sets.Posted.Single().Id, result.Release!.Id);
        Assert.Equal("", result.ReleaseError);
    }

    [Fact]
    public async Task Run_RejectsGroupsItCannotRelease()
    {
        var a = Stored("SYN-A");
        var plan = SetReleasePlan.Build(Set((a, 1), (Stored("SYN-A"), 1), (Stored("SYN-B", cutType: CutType.Plasma), 1)), 1, false);
        var other = SetReleasePlan.Build(Set((a, 1)), 1, false);
        var refused = plan.Groups.Single(g => !g.CanRelease);
        var session = Session();

        await Assert.ThrowsAsync<ArgumentException>(() => session.RunAsync(plan, Array.Empty<SetReleaseGroup>(), "", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => session.RunAsync(plan, new[] { refused }, "", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => session.RunAsync(plan, other.Groups, "", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => session.RunAsync(plan, new[] { plan.Groups[0], plan.Groups[0] }, "", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => session.RunAsync(plan, new[] { plan.Groups[0] }, "", "bad\0notes"));
        Assert.Empty(_created);

        var releasable = plan.Groups.Where(g => g.CanRelease).ToList();
        await session.RunAsync(plan, releasable, "", "");
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(plan, releasable, "", ""));
    }

    [Fact]
    public async Task Run_OfTwoSets_MergesIntoADraft_ClearsItsSheets_NestsIt_AndRecordsTheMerge()
    {
        var a = Stored("SYN-A");
        var b = Stored("SYN-B");
        var first = Set((a, 1), (b, 1));
        var second = Set((a, 2));
        var (draft, _) = Draft(a);
        var plan = SetReleasePlan.Build(new[] { new SetReleasePlanLine(first, 2), new SetReleasePlanLine(second, 1) }, false);
        var group = Assert.Single(plan.Groups);
        var platesSeen = -1;
        var session = Session(renest: (nest, _) =>
        {
            platesSeen = nest.Plates.Count;
            nest.CreatePlate().Parts.Add(new Part(nest.Drawings.First()));
            return Task.FromResult(new SetReleaseRenest(true, "2 copies did not fit."));
        });

        var result = await session.RunAsync(plan, plan.Groups, "Synthetic operator", "", mergeInto: new Dictionary<SetReleaseGroup, NestRecord> { [group] = draft });

        Assert.Equal("", result.ReleaseError);
        var merged = Assert.Single(result.Created);
        Assert.Same(draft, merged.MergedInto);
        Assert.Equal((draft.Id, 4), (merged.Record!.Id, merged.Record.Revision));
        Assert.Empty(_created);
        Assert.Empty(_nests.Uploaded);
        Assert.Equal(new[] { (draft.Id, 3) }, _nests.Merges.Select(m => (m.Id, m.ExpectedRevision)));
        Assert.Equal(0, platesSeen);
        var nestAfter = merged.Nest!;
        Assert.Equal(new[] { ("SYN-A", 6), ("SYN-B", 2) }, nestAfter.Drawings.OrderBy(d => d.Name).Select(d => (d.Name, d.Quantity.Required)));
        Assert.Single(nestAfter.Plates);
        Assert.Equal((true, "2 copies did not fit."), (merged.Renest!.Nested, merged.Renest.Note));
        Assert.Contains("2 copies did not fit.", merged.Warnings);

        var release = Assert.Single(_sets.Posted);
        Assert.Equal(new[] { (first.Id, 2), (second.Id, 1) }, release.Sets.Select(l => (l.SetId, l.Quantity)));
        var nestLine = Assert.Single(release.Nests);
        Assert.Equal((SetReleaseNestKind.Merged, (int?)3, 4), (nestLine.Kind, nestLine.RevisionBefore, nestLine.RevisionAfter));
        Assert.Equal(new[] { ((Guid?)first.Id, a.Id, 2), (second.Id, a.Id, 2), (first.Id, b.Id, 2) },
            nestLine.Items.Select(i => (i.SetId, i.DrawingId, i.Quantity)));
    }

    [Fact]
    public async Task Run_MergeIntoADraftWithAnotherRevisionOfADrawing_IsRefusedBeforeAnythingIsSaved()
    {
        var older = Stored("SYN-A");
        var (draft, _) = Draft(older);
        var newer = Stored("SYN-A");
        newer.Id = older.Id;
        newer.Revision = older.Revision + 1;
        _library.Packages[(newer.Id, newer.Revision)] = (Encoding.UTF8.GetBytes($"package {newer.Id} r{newer.Revision}"), Units.Inches);
        newer.PackageSha256 = Convert.ToHexString(SHA256.HashData(_library.Packages[(newer.Id, newer.Revision)].Bytes)).ToLowerInvariant();
        var plan = SetReleasePlan.Build(Set((newer, 1), (Stored("SYN-NEW"), 1)), 1, false);

        var result = await Session().RunAsync(plan, plan.Groups, "", "",
            mergeInto: new Dictionary<SetReleaseGroup, NestRecord> { [plan.Groups[0]] = draft });

        var group = Assert.Single(result.Groups);
        Assert.Equal(SetReleaseGroupOutcome.Refused, group.Outcome);
        Assert.Contains("Update Drawings", group.Error);
        Assert.Empty(_nests.Merges);
        Assert.Empty(_sets.Posted);
    }

    [Fact]
    public async Task Run_MergeIntoADraftSavedMeanwhile_FailsThatGroup_AndRecordsNoRelease()
    {
        var a = Stored("SYN-A");
        var (draft, nest) = Draft(a);
        var plan = SetReleasePlan.Build(Set((a, 1)), 1, false);
        _nests.SaveBeforeNextMerge = true;

        var result = await Session().RunAsync(plan, plan.Groups, "", "",
            mergeInto: new Dictionary<SetReleaseGroup, NestRecord> { [plan.Groups[0]] = draft });

        var group = Assert.Single(result.Groups);
        Assert.Equal(SetReleaseGroupOutcome.Failed, group.Outcome);
        Assert.Contains("Someone saved it since you chose it (now revision 4)", group.Error);
        Assert.Empty(_sets.Posted);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task Run_MergeChecks_RefuseAWrongTargetBeforeStarting()
    {
        var a = Stored("SYN-A");
        var (draft, _) = Draft(a);
        var plan = SetReleasePlan.Build(Set((a, 1), (Stored("SYN-B", material: "Synthetic Aluminum"), 1)), 1, false);
        var noReader = new SetReleaseSession(_sets, _library, _nests, Server, ReadPackage, n => Array.Empty<byte>(), CreateNest);

        await Assert.ThrowsAsync<ArgumentException>(() => Session().RunAsync(plan, new[] { plan.Groups[0] }, "", "",
            mergeInto: new Dictionary<SetReleaseGroup, NestRecord> { [plan.Groups[1]] = draft }));
        await Assert.ThrowsAsync<ArgumentException>(() => Session().RunAsync(plan, plan.Groups, "", "",
            mergeInto: new Dictionary<SetReleaseGroup, NestRecord> { [plan.Groups[0]] = draft, [plan.Groups[1]] = draft }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => noReader.RunAsync(plan, plan.Groups, "", "",
            mergeInto: new Dictionary<SetReleaseGroup, NestRecord> { [plan.Groups[0]] = draft }));
        // A draft of the aluminum group's material is refused once downloaded.
        var wrong = await Session().RunAsync(plan, plan.Groups, "", "",
            mergeInto: new Dictionary<SetReleaseGroup, NestRecord> { [plan.Groups.Single(g => g.MaterialName == "Synthetic Aluminum")] = draft });
        Assert.Contains(wrong.Groups, g => g.Outcome == SetReleaseGroupOutcome.Refused && g.Error.Contains("is not Synthetic Aluminum"));
        Assert.Empty(_nests.Merges);
    }

    [Fact]
    public async Task Run_WhenNestingAfterReleaseFails_StillReleasesTheNestUnnested()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1)), 1, false);
        var session = Session(renest: (nest, _) =>
        {
            nest.CreatePlate();
            throw new InvalidOperationException("Synthetic engine failure.");
        });

        var result = await session.RunAsync(plan, plan.Groups, "", "");

        var created = Assert.Single(result.Created);
        Assert.Equal((false, "Auto Nest failed: Synthetic engine failure."), (created.Renest!.Nested, created.Renest.Note));
        Assert.Empty(created.Nest!.Plates);
        Assert.Single(_sets.Posted);
    }

    [Fact]
    public async Task Run_CanceledWhileNestingAfterRelease_UploadsNothing()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1), (Stored("SYN-B", cutType: CutType.Plasma), 1)), 1, false);
        using var cancel = new CancellationTokenSource();
        var progress = new RecordingProgress();
        var session = Session(renest: (_, token) =>
        {
            cancel.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new SetReleaseRenest(true, ""));
        });

        var result = await session.RunAsync(plan, plan.Groups, "", "", progress, cancel.Token);

        Assert.True(result.Canceled);
        Assert.Equal(new[] { (SetReleaseStage.Preparing, 0), (SetReleaseStage.Nesting, 0) },
            progress.Reports.Select(p => (p.Stage, p.Index)));
        Assert.All(result.Groups, g => Assert.Equal(SetReleaseGroupOutcome.NotStarted, g.Outcome));
        Assert.Empty(_nests.Uploaded);
        Assert.Empty(_sets.Posted);
    }

    [Fact]
    public async Task FindDrafts_AsksForDraftsOfTheGroup()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1)), 1, false);

        await SetReleasePlan.FindDraftsAsync(_nests, plan.Groups[0]);

        var query = Assert.Single(_nests.Queries);
        Assert.Equal((NestStatus.Draft, "Synthetic Steel", "Grade A", 0.25), (query.Status, query.Material, query.MaterialGrade, query.Thickness));
    }

    [Fact]
    public async Task Run_OfDrawingsOnTheirOwn_ChecksTheServer_BuildsTheirNests_AndRecordsThemWithoutASet()
    {
        var a = Stored("SYN-A");
        var b = Stored("SYN-B");
        var c = Stored("SYN-C", material: "Synthetic Aluminum");
        var set = Set((a, 2));
        var plan = SetReleasePlan.Build(new[] { new SetReleasePlanLine(set, 3) },
            new[] { new SetReleaseDrawingLine(a, 5), new SetReleaseDrawingLine(b, 1), new SetReleaseDrawingLine(c, 40) }, false);

        var result = await Session().RunAsync(plan, plan.Groups, "Synthetic operator", "");

        Assert.Equal("", result.ReleaseError);
        Assert.Equal(1, _sets.DrawingReleaseChecks);
        var steel = result.Groups.Single(g => g.Group.MaterialName == "Synthetic Steel").Nest!;
        Assert.Equal(new[] { ("SYN-A", 11), ("SYN-B", 1) }, steel.Drawings.OrderBy(d => d.Name).Select(d => (d.Name, d.Quantity.Required)));
        var release = Assert.Single(_sets.Posted);
        Assert.Equal((set.Id, 3), (release.Sets.Single().SetId, release.Sets.Single().Quantity));
        Assert.Equal(new[] { (a.Id, 5), (b.Id, 1), (c.Id, 40) }, release.Drawings.Select(d => (d.DrawingId, d.Quantity)));
        Assert.Equal(new[] { ((Guid?)set.Id, a.Id, 6), (null, a.Id, 5), (null, b.Id, 1) },
            release.Nests.Single(n => n.MaterialName == "Synthetic Steel").Items.Select(i => (i.SetId, i.DrawingId, i.Quantity)));
        Assert.Equal(new[] { ((Guid?)null, c.Id, 40) },
            release.Nests.Single(n => n.MaterialName == "Synthetic Aluminum").Items.Select(i => (i.SetId, i.DrawingId, i.Quantity)));
        Assert.Null(SetReleaseRules.GetError(release));
    }

    [Fact]
    public async Task Run_OfSetsOnly_DoesNotAskAboutDrawingsOnTheirOwn()
    {
        var plan = SetReleasePlan.Build(Set((Stored("SYN-A"), 1)), 1, false);

        await Session().RunAsync(plan, plan.Groups, "", "");

        Assert.Equal(0, _sets.DrawingReleaseChecks);
        Assert.Empty(_sets.Posted.Single().Drawings);
    }

    [Fact]
    public async Task Run_OnAServerWithoutDrawingsOnTheirOwn_RefusesBeforeAnythingIsBuilt()
    {
        _sets.DrawingReleaseFailure = new ServerTooOldException("releasing library drawings on their own");
        var plan = SetReleasePlan.Build(Array.Empty<SetReleasePlanLine>(), new[] { new SetReleaseDrawingLine(Stored("SYN-A"), 2) }, false);

        var result = await Session().RunAsync(plan, plan.Groups, "", "");

        var group = Assert.Single(result.Groups);
        Assert.Equal(SetReleaseGroupOutcome.Refused, group.Outcome);
        Assert.Contains("does not support releasing library drawings on their own", group.Error);
        Assert.Empty(_created);
        Assert.Empty(_nests.Uploaded);
        Assert.Empty(_sets.Posted);

        _sets.DrawingReleaseFailure = new IOException("Synthetic outage");
        var failed = await Session().RunAsync(plan, plan.Groups, "", "");
        Assert.Equal(SetReleaseGroupOutcome.Failed, failed.Groups.Single().Outcome);
        Assert.Contains("Synthetic outage", failed.Groups.Single().Error);
        Assert.Empty(_nests.Uploaded);
    }

    private sealed class RecordingProgress(Action<SetReleaseProgress>? onReport = null) : IProgress<SetReleaseProgress>
    {
        public List<SetReleaseProgress> Reports { get; } = new();

        public void Report(SetReleaseProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }

    private sealed class FakeLibrary : IDrawingLibrary
    {
        public Dictionary<(Guid, int), (byte[] Bytes, Units Units)> Packages { get; } = new();
        public Exception? Failure { get; set; }

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure != null)
                throw Failure;
            return Task.FromResult(Packages.TryGetValue((id, revision), out var package) ? package.Bytes : null);
        }

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeNests : INestRepository
    {
        private readonly Dictionary<Guid, NestRecord> _stored = new();

        public List<(byte[] File, NestRecord Record)> Uploaded { get; } = new();
        public List<CancellationToken> UploadTokens { get; } = new();
        public int FailUploadNumber { get; set; }
        public bool StoreFailedUpload { get; set; }
        public Exception? MetadataFailure { get; set; }

        public Task<NestRecord> UploadAsync(byte[] file, NestRecord record, CancellationToken ct = default)
        {
            UploadTokens.Add(ct);
            var failing = UploadTokens.Count == FailUploadNumber;
            var stored = new NestRecord
            {
                Id = record.Id,
                Revision = 1,
                Name = record.Name,
                Status = record.Status,
                Material = record.Material,
            };
            if (!failing || StoreFailedUpload)
            {
                Uploaded.Add((file, record));
                _stored[record.Id] = stored;
            }

            return failing ? throw new IOException("Synthetic upload failure.") : Task.FromResult(stored);
        }

        public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken ct = default)
        {
            if (MetadataFailure != null)
                throw MetadataFailure;
            return Task.FromResult(_stored.TryGetValue(id, out var record) ? record : null);
        }

        /// <summary>Nests a test reader returns, keyed by their (fake) archive text.</summary>
        public Dictionary<string, Nest> Archives { get; } = new();
        private readonly Dictionary<(Guid, int), byte[]> _revisions = new();
        public List<(Guid Id, int ExpectedRevision)> Merges { get; } = new();
        public List<NestQuery> Queries { get; } = new();
        public bool SaveBeforeNextMerge { get; set; }

        public void AddDraft(NestRecord record, Nest nest)
        {
            var key = $"draft {record.Id} r{record.Revision}";
            Archives[key] = nest;
            _revisions[(record.Id, record.Revision)] = Encoding.UTF8.GetBytes(key);
            _stored[record.Id] = record;
        }

        public Task<byte[]?> GetRevisionFileAsync(Guid id, int revisionNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(_revisions.TryGetValue((id, revisionNumber), out var bytes) ? bytes : null);

        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] nestFile, NestRecord record, int expectedRevision,
            CancellationToken cancellationToken = default)
        {
            var current = _stored[id];
            if (SaveBeforeNextMerge)
            {
                SaveBeforeNextMerge = false;
                current = _stored[id] = new NestRecord { Id = id, Name = current.Name, Revision = current.Revision + 1, Status = NestStatus.Draft };
                _revisions[(id, current.Revision)] = Encoding.UTF8.GetBytes("someone else's save");
            }
            if (current.Revision != expectedRevision)
                throw new IOException("Could not update the nest: HTTP 409 Conflict.");
            Merges.Add((id, expectedRevision));
            var saved = new NestRecord { Id = id, Name = record.Name, Revision = current.Revision + 1, Status = record.Status };
            _stored[id] = saved;
            _revisions[(id, saved.Revision)] = nestFile;
            return Task.FromResult(saved);
        }

        public Task<NestPage> QueryAsync(NestQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult(new NestPage { Items = Array.Empty<NestRecord>(), Limit = query.Limit });
        }

        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_stored.TryGetValue(id, out var record) && _revisions.TryGetValue((id, record.Revision), out var bytes) ? bytes : null);

        public Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeSets : IDrawingSetStore
    {
        public List<SetReleaseRecord> Posted { get; } = new();
        public List<SetReleaseRecord> Stored { get; } = new();
        public Queue<Exception> Failures { get; } = new();
        public bool ConflictWithPosted { get; set; }

        public Task<SetReleaseRecord> PostReleaseAsync(SetReleaseRecord release, CancellationToken cancellationToken = default)
        {
            Posted.Add(release);
            if (Failures.TryDequeue(out var failure))
                throw failure;
            var stored = new SetReleaseRecord
            {
                Id = release.Id,
                Sets = release.Sets.Select(s => new SetReleaseLine
                {
                    SetId = s.SetId,
                    SetRevision = s.SetRevision,
                    Quantity = s.Quantity,
                    SetName = "Synthetic kit",
                }).ToList(),
                Drawings = release.Drawings,
                Nests = release.Nests,
            };
            if (ConflictWithPosted)
                throw new SetReleasePostConflictException(stored);
            Stored.Add(stored);
            return Task.FromResult(stored);
        }

        public Task<DrawingSetPage> QueryAsync(DrawingSetQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DrawingSetRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DrawingSetPage?> GetRevisionsAsync(Guid id, int offset = 0, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DrawingSetRecord?> GetRevisionAsync(Guid id, int revision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DrawingSetPage?> GetSetsContainingAsync(Guid drawingId, int offset = 0, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DrawingSetRecord> CreateAsync(DrawingSetRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DrawingSetRecord> UpdateAsync(Guid id, DrawingSetRecord record, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DrawingSetRecord> ArchiveAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DrawingSetRecord> RestoreAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SetReleaseRecord?> GetReleaseAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SetReleasePage> QueryReleasesAsync(Guid? setId = null, Guid? nestId = null, Guid? drawingId = null, int offset = 0, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Thrown by the support check; null plays a server that records drawings on their own.</summary>
        public Exception? DrawingReleaseFailure { get; set; }

        public int DrawingReleaseChecks { get; private set; }

        public Task EnsureDrawingReleasesSupportedAsync(CancellationToken cancellationToken = default)
        {
            DrawingReleaseChecks++;
            return DrawingReleaseFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }
        public Task<SetReleaseRecord> UndoReleaseAsync(Guid id, SetReleaseUndoRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
