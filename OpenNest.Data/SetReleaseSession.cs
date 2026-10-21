using OpenNest;

namespace OpenNest.Data;

public enum SetReleaseGroupOutcome
{
    /// <summary>
    /// The group's nest is on the server, new or merged; <see cref="SetReleaseGroupResult.Record"/>
    /// is its record.
    /// </summary>
    Created,

    /// <summary>The group could not be built; nothing was uploaded for any group.</summary>
    Refused,

    /// <summary>Building or uploading the group failed; <see cref="SetReleaseGroupResult.Error"/> says how.</summary>
    Failed,

    /// <summary>An earlier group stopped the release, or it was canceled, before this group was reached.</summary>
    NotStarted,
}

/// <summary>What happened to one chosen group of a release.</summary>
public sealed class SetReleaseGroupResult
{
    internal SetReleaseGroupResult(SetReleaseGroup group)
    {
        Group = group;
    }

    public SetReleaseGroup Group { get; }

    public SetReleaseGroupOutcome Outcome { get; internal set; } = SetReleaseGroupOutcome.NotStarted;

    /// <summary>
    /// The nest as built, held so a created one can be opened without downloading it again.
    /// Set for a created nest and for one whose upload failed; null otherwise.
    /// </summary>
    public Nest? Nest { get; internal set; }

    /// <summary>The server record of a created or merged nest: its id and the revision the release wrote.</summary>
    public NestRecord? Record { get; internal set; }

    /// <summary>The draft the group merged into, as chosen (its revision before the merge); null for a new nest.</summary>
    public NestRecord? MergedInto { get; internal set; }

    /// <summary>What nesting after release did; null when it was not asked for.</summary>
    public SetReleaseRenest? Renest { get; internal set; }

    /// <summary>Why the group was refused or failed; empty otherwise.</summary>
    public string Error { get; internal set; } = "";

    /// <summary>The plan's warnings plus any the library inserts raised.</summary>
    public IReadOnlyList<string> Warnings { get; internal set; } = Array.Empty<string>();
}

/// <summary>
/// What nesting a group after release did: <see cref="Nested"/> when a layout was kept,
/// and an operator-facing note either way (unplaced parts, or why nothing was kept).
/// </summary>
public sealed record SetReleaseRenest(bool Nested, string Note);

public enum SetReleaseStage
{
    /// <summary>
    /// Downloading the group's drawings (and draft), building its nest and nesting it when
    /// asked; nothing is uploaded yet.
    /// </summary>
    Preparing,

    /// <summary>Uploading the group's nest, or saving the draft it merged into.</summary>
    Uploading,

    /// <summary>Every nest is uploaded; the release record is being posted.</summary>
    Recording,
}

/// <summary>Where a release has got to. <see cref="Group"/> is null while recording.</summary>
public sealed class SetReleaseProgress
{
    public SetReleaseProgress(SetReleaseStage stage, int index, int count, SetReleaseGroup? group)
    {
        Stage = stage;
        Index = index;
        Count = count;
        Group = group;
    }

    public SetReleaseStage Stage { get; }

    /// <summary>Zero-based position of <see cref="Group"/> among the chosen groups.</summary>
    public int Index { get; }

    public int Count { get; }

    public SetReleaseGroup? Group { get; }
}

/// <summary>Every chosen group's outcome, and the release record when all of them were created.</summary>
public sealed class SetReleaseResult
{
    internal SetReleaseResult(IReadOnlyList<SetReleaseGroupResult> groups, bool canceled,
        SetReleaseRecord? release, string releaseError)
    {
        Groups = groups;
        Canceled = canceled;
        Release = release;
        ReleaseError = releaseError;
    }

    /// <summary>The chosen groups in plan order.</summary>
    public IReadOnlyList<SetReleaseGroupResult> Groups { get; }

    /// <summary>The nests now on the server, whether or not the release was recorded.</summary>
    public IReadOnlyList<SetReleaseGroupResult> Created =>
        Groups.Where(g => g.Outcome == SetReleaseGroupOutcome.Created).ToList();

    /// <summary>Cancellation stopped the release between groups.</summary>
    public bool Canceled { get; }

    /// <summary>The stored release; null unless every chosen group was created and the post succeeded.</summary>
    public SetReleaseRecord? Release { get; }

    /// <summary>
    /// Why posting the release failed after every nest was created; empty otherwise.
    /// <see cref="SetReleaseSession.RetryReleaseAsync"/> posts the same release again.
    /// </summary>
    public string ReleaseError { get; }

    /// <summary>This result once <see cref="SetReleaseSession.RetryReleaseAsync"/> has recorded <paramref name="release"/>.</summary>
    public SetReleaseResult WithRelease(SetReleaseRecord release) =>
        new(Groups, Canceled, release ?? throw new ArgumentNullException(nameof(release)), "");
}

/// <summary>
/// Releases one or more drawing sets: one nest per chosen group of a <see cref="SetReleasePlan"/>,
/// either new (uploaded as a <see cref="NestStatus.Draft"/>) or an existing draft of the same
/// group that the release merges into, each built through <see cref="LibraryDrawingInsert"/>
/// from the items' exact library revisions, and only then recorded as one release (S7).
/// <para>
/// A merge downloads the draft at the revision it was chosen at, adds the items, and removes
/// every plate, so the whole demand is nested again (M5); it is saved only while the draft is
/// still at that revision. When the caller supplies a renest step, every chosen group, new or
/// merged, is nested before anything is uploaded.
/// </para>
/// <para>
/// Every group is built before any is uploaded, so a refusal (a package that no longer
/// matches its record, units the new nest does not use, an insert refusal) creates
/// nothing. Uploads then run in order; the first failure stops them, and cancellation is
/// honoured between uploads, never during one. Nests already uploaded stay as ordinary
/// nests and are listed in the result; the release is posted only when every chosen group
/// was created. Nothing is rolled back.
/// </para>
/// </summary>
public sealed class SetReleaseSession
{
    private readonly IDrawingSetStore _sets;
    private readonly IDrawingLibrary _library;
    private readonly INestRepository _nests;
    private readonly string _serverUrl;
    private readonly Func<byte[], LibraryPackage> _readPackage;
    private readonly Func<Nest, byte[]> _writeNest;
    private readonly Func<SetReleaseGroup, CancellationToken, Task<Nest>> _createNest;
    private readonly Func<byte[], Nest>? _readNest;
    private readonly Func<Nest, CancellationToken, Task<SetReleaseRenest>>? _renest;
    private bool _started;
    private SetReleaseRecord? _pendingRelease;

    /// <param name="readPackage">Reads a downloaded library package (the desktop uses <c>DrawingPackage.Read</c>).</param>
    /// <param name="writeNest">Serializes a nest archive for upload (the desktop uses <c>NestWriter</c>).</param>
    /// <param name="createNest">
    /// Makes an empty nest for a group, named and set up as File > New would, in the group's
    /// units. The session then sets its material, grade, thickness, customer and status.
    /// </param>
    /// <param name="readNest">Reads a downloaded nest archive (the desktop uses <c>NestReader</c>); required to merge.</param>
    /// <param name="renest">
    /// Nests a group's nest from scratch (the desktop runs Auto Nest headless): every drawing's
    /// required quantity onto plates from the nest's defaults. Null skips nesting after release.
    /// It must leave the nest without plates when it keeps no layout.
    /// </param>
    public SetReleaseSession(IDrawingSetStore sets, IDrawingLibrary library, INestRepository nests, string serverUrl,
        Func<byte[], LibraryPackage> readPackage, Func<Nest, byte[]> writeNest,
        Func<SetReleaseGroup, CancellationToken, Task<Nest>> createNest,
        Func<byte[], Nest>? readNest = null, Func<Nest, CancellationToken, Task<SetReleaseRenest>>? renest = null)
    {
        _readNest = readNest;
        _renest = renest;
        _sets = sets ?? throw new ArgumentNullException(nameof(sets));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _nests = nests ?? throw new ArgumentNullException(nameof(nests));
        _readPackage = readPackage ?? throw new ArgumentNullException(nameof(readPackage));
        _writeNest = writeNest ?? throw new ArgumentNullException(nameof(writeNest));
        _createNest = createNest ?? throw new ArgumentNullException(nameof(createNest));
        if (ServerUrl.Key(serverUrl).Length == 0)
            throw new ArgumentException("A set release needs the nest server URL.", nameof(serverUrl));
        _serverUrl = serverUrl;
    }

    /// <summary>Every nest was created but the release was not recorded; <see cref="RetryReleaseAsync"/> may post it.</summary>
    public bool HasPendingRelease => _pendingRelease != null;

    /// <summary>
    /// Releases <paramref name="groups"/>, all from <paramref name="plan"/>. A group named in
    /// <paramref name="mergeInto"/> merges into that draft (as read, at that revision) instead
    /// of making a new nest. Call once per session.
    /// </summary>
    public async Task<SetReleaseResult> RunAsync(SetReleasePlan plan, IReadOnlyCollection<SetReleaseGroup> groups,
        string releasedBy, string notes, IProgress<SetReleaseProgress>? progress = null,
        CancellationToken cancellationToken = default, IReadOnlyDictionary<SetReleaseGroup, NestRecord>? mergeInto = null)
    {
        mergeInto ??= new Dictionary<SetReleaseGroup, NestRecord>();
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0)
            throw new ArgumentException("Choose at least one group to release.", nameof(groups));
        if (groups.Any(g => g is null || !plan.Groups.Contains(g)) || groups.Distinct().Count() != groups.Count)
            throw new ArgumentException("Every group must come from the plan, once.", nameof(groups));
        if (groups.FirstOrDefault(g => !g.CanRelease) is { } refused)
            throw new ArgumentException(refused.Refusal, nameof(groups));
        var textError = DrawingLibraryRules.TextError("releasedBy", releasedBy ?? "", DrawingLibraryRules.MaxTextLength)
            ?? DrawingLibraryRules.TextError("notes", notes ?? "", DrawingLibraryRules.MaxNotesLength);
        if (textError is not null)
            throw new ArgumentException(textError);
        if (mergeInto.Keys.Any(g => !groups.Contains(g)))
            throw new ArgumentException("Every merged group must be one of the groups released.", nameof(mergeInto));
        if (mergeInto.Values.Any(d => d is null || d.Id == Guid.Empty || d.Revision < 1))
            throw new ArgumentException("Merge only into a draft read from the server.", nameof(mergeInto));
        if (mergeInto.Values.Select(d => d.Id).Distinct().Count() != mergeInto.Count)
            throw new ArgumentException("Two groups cannot merge into the same nest.", nameof(mergeInto));
        if (mergeInto.Count > 0 && _readNest is null)
            throw new InvalidOperationException("Merging into a draft needs a nest reader.");
        if (_started)
            throw new InvalidOperationException("This release session has already run.");
        _started = true;

        var results = plan.Groups.Where(groups.Contains).Select(g => new SetReleaseGroupResult(g)
        {
            MergedInto = mergeInto.TryGetValue(g, out var draft) ? draft : null,
        }).ToList();

        // Build every nest first: a refusal here leaves nothing on the server.
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            progress?.Report(new SetReleaseProgress(SetReleaseStage.Preparing, i, results.Count, result.Group));
            bool ready;
            try
            {
                ready = await PrepareAsync(plan, result, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DiscardBuilt(results);
                return new SetReleaseResult(results, canceled: true, null, "");
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException
                or System.Text.Json.JsonException or TimeoutException)
            {
                result.Outcome = SetReleaseGroupOutcome.Failed;
                result.Error = $"Could not prepare the {Describe(result.Group)} nest: {ex.Message}";
                ready = false;
            }

            if (!ready)
            {
                DiscardBuilt(results);
                return new SetReleaseResult(results, canceled: false, null, "");
            }
        }

        for (var i = 0; i < results.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                DiscardBuilt(results);
                return new SetReleaseResult(results, canceled: true, null, "");
            }

            progress?.Report(new SetReleaseProgress(SetReleaseStage.Uploading, i, results.Count, results[i].Group));
            if (!await UploadAsync(results[i]))
            {
                DiscardBuilt(results);
                return new SetReleaseResult(results, canceled: false, null, "");
            }
        }

        _pendingRelease = new SetReleaseRecord
        {
            Id = Guid.NewGuid(),
            Sets = plan.Lines.Select(l => new SetReleaseLine
            {
                SetId = l.Set.Id,
                SetRevision = l.Set.Revision,
                Quantity = l.Quantity,
            }).ToList(),
            Notes = notes ?? "",
            ReleasedBy = releasedBy ?? "",
            Nests = results.Select(r => new SetReleaseNest
            {
                NestId = r.Record!.Id,
                Kind = r.MergedInto is null ? SetReleaseNestKind.Created : SetReleaseNestKind.Merged,
                RevisionBefore = r.MergedInto?.Revision,
                RevisionAfter = r.Record.Revision,
                Items = r.Group.Items.SelectMany(i => i.Sources.Select(source => new SetReleaseNestItem
                {
                    SetId = source.SetId,
                    DrawingId = i.Drawing.Id,
                    DrawingRevision = i.Drawing.Revision,
                    Quantity = source.Quantity,
                })).ToList(),
                MaterialName = r.Group.MaterialName,
                MaterialGrade = r.Group.MaterialGrade,
                Thickness = r.Group.Thickness,
                Units = r.Group.Units,
                CutType = r.Group.CutType,
            }).ToList(),
        };
        progress?.Report(new SetReleaseProgress(SetReleaseStage.Recording, results.Count, results.Count, null));
        try
        {
            return new SetReleaseResult(results, canceled: false, await PostPendingAsync(CancellationToken.None), "");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or System.Text.Json.JsonException
            or OperationCanceledException)
        {
            return new SetReleaseResult(results, canceled: false, null,
                $"Every nest was created, but the release was not recorded: {ex.Message}");
        }
    }

    /// <summary>
    /// Posts the release again after <see cref="SetReleaseResult.ReleaseError"/>, under the
    /// same id, so a post that was in fact stored is not recorded twice.
    /// </summary>
    public Task<SetReleaseRecord> RetryReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingRelease == null)
            throw new InvalidOperationException("There is no unrecorded release to post.");
        return PostPendingAsync(cancellationToken);
    }

    // Builds the group's nest in memory, new or merged, and nests it when asked; false when
    // the group is refused.
    private async Task<bool> PrepareAsync(SetReleasePlan plan, SetReleaseGroupResult result, CancellationToken cancellationToken)
    {
        var group = result.Group;
        Nest nest;
        if (result.MergedInto is { } draft)
        {
            var archive = await _nests.GetRevisionFileAsync(draft.Id, draft.Revision, cancellationToken);
            if (archive == null)
                return Refuse(result, $"Draft '{draft.Name}' revision {draft.Revision} is no longer on the server. Choose again.");
            nest = await Task.Run(() => _readNest!(archive), cancellationToken);
            NestRecordFactory.ApplyMetadata(nest, draft);
            if (draft.Status != NestStatus.Draft || draft.Deleted)
                return Refuse(result, $"'{draft.Name}' is not a draft, so the release cannot merge into it.");
            if (nest.Units != group.Units)
                return Refuse(result, $"Draft '{draft.Name}' is in {Name(nest.Units)}, not {Name(group.Units)}.");
            if (SharedListNames.Key(nest.Material?.Name) != SharedListNames.Key(group.MaterialName)
                || SharedListNames.Key(nest.Material?.Grade) != SharedListNames.Key(group.MaterialGrade)
                || DrawingLibraryRules.NormalizeThickness(nest.Thickness) != group.Thickness)
                return Refuse(result, $"Draft '{draft.Name}' is not {Describe(group)}.");
            if (nest.Drawings.FirstOrDefault(d => d.Library is { } link && link.CutType != group.CutType) is { } other)
                return Refuse(result, $"Draft '{draft.Name}' holds '{other.Name}' for {other.Library!.CutType.ToString().ToLowerInvariant()} "
                    + $"cutting, not {group.CutType.ToString().ToLowerInvariant()}.");

            // M5: a merge renests the whole demand, so the draft's layout goes.
            nest.Plates.Clear();
            nest.UpdateDrawingQuantities();
        }
        else
        {
            nest = await _createNest(group, cancellationToken)
                ?? throw new InvalidOperationException("The nest factory returned no nest.");
            if (nest.Units != group.Units)
                return Refuse(result, $"The new nest for {Describe(group)} is in {Name(nest.Units)}, not {Name(group.Units)}.");

            var current = nest.Material;
            nest.Material = current != null && SharedListNames.Key(current.Name) == SharedListNames.Key(group.MaterialName)
                && SharedListNames.Key(current.Grade) == SharedListNames.Key(group.MaterialGrade)
                    ? new Material(group.MaterialName, group.MaterialGrade, current.Density)
                    : new Material(group.MaterialName, group.MaterialGrade);
            nest.Thickness = group.Thickness;
            nest.Status = NestStatus.Draft;
            if (plan.Customer.Length > 0)
                nest.Customer = plan.Customer;
        }

        var warnings = group.Warnings.ToList();
        foreach (var item in group.Items)
        {
            var record = item.Drawing;
            var bytes = await _library.GetRevisionPackageAsync(record.Id, record.Revision, cancellationToken);
            if (bytes == null)
                return Refuse(result, $"'{record.Name}' revision {record.Revision} is no longer in the library. Reload the set.");

            var package = await Task.Run(() => _readPackage(bytes), cancellationToken);
            var insert = LibraryDrawingInsert.Plan(nest, _serverUrl, record, package, item.Quantity);
            if (insert.IsRefused)
                return Refuse(result, insert.Refusal);

            warnings.AddRange(insert.Warnings);
            insert.Apply();
        }

        if (_renest != null)
        {
            try
            {
                result.Renest = await _renest(nest, cancellationToken)
                    ?? throw new InvalidOperationException("The renest step returned nothing.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The nest is still released; the operator runs Auto Nest on it.
                nest.Plates.Clear();
                nest.UpdateDrawingQuantities();
                result.Renest = new SetReleaseRenest(false, $"Auto Nest failed: {ex.Message}");
            }
            if (result.Renest.Note.Length > 0)
                warnings.Add(result.Renest.Note);
        }

        result.Nest = nest;
        result.Warnings = warnings;
        return true;
    }

    // True when the nest is on the server. A failed upload is checked by its id, which no
    // other client can hold, so a nest stored before its reply was lost is still reported.
    private async Task<bool> UploadAsync(SetReleaseGroupResult result)
    {
        if (result.MergedInto is { } draft)
            return await SaveMergeAsync(result, draft);
        var nest = result.Nest!;
        var id = Guid.NewGuid();
        Exception failure;
        try
        {
            var archive = await Task.Run(() => _writeNest(nest));
            var saved = await _nests.UploadAsync(archive, NestRecordFactory.FromNest(nest, id, archive.LongLength),
                CancellationToken.None);
            if (saved.Id == id)
                return Created(result, saved);
            failure = new IOException($"The nest server stored it under a different id ({saved.Id}).");
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        string stored;
        try
        {
            if (await _nests.GetMetadataAsync(id, CancellationToken.None) is { } found && found.Id == id)
                return Created(result, found);
            stored = "It was not saved.";
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or System.Text.Json.JsonException
            or OperationCanceledException)
        {
            stored = $"It may have been saved as '{nest.Name}' (id {id}); check Open from Database before releasing again.";
        }

        result.Outcome = SetReleaseGroupOutcome.Failed;
        result.Error = $"Could not upload the {Describe(result.Group)} nest: {failure.Message} {stored}";
        return false;
    }

    // True when the merged draft is saved. The save is guarded by the revision the draft was
    // chosen at; a failed one counts as saved only when the server holds exactly these bytes.
    private async Task<bool> SaveMergeAsync(SetReleaseGroupResult result, NestRecord draft)
    {
        var nest = result.Nest!;
        var archive = await Task.Run(() => _writeNest(nest));
        Exception failure;
        try
        {
            var saved = await _nests.UpdateFileAsync(draft.Id, archive, NestRecordFactory.FromNest(nest, draft.Id, archive.LongLength),
                draft.Revision, CancellationToken.None);
            if (saved.Id == draft.Id && saved.Revision > draft.Revision)
                return Created(result, saved);
            failure = new IOException($"The nest server did not save a new revision of '{draft.Name}'.");
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        string detail;
        try
        {
            var current = await _nests.GetMetadataAsync(draft.Id, CancellationToken.None);
            if (current is { } found && found.Revision > draft.Revision
                && await _nests.GetFileAsync(draft.Id, CancellationToken.None) is { } stored && stored.AsSpan().SequenceEqual(archive))
                return Created(result, found);
            detail = current == null
                ? "It was deleted."
                : current.Revision != draft.Revision
                    ? $"Someone saved it since you chose it (now revision {current.Revision}); it was left as they saved it."
                    : "It was not changed.";
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or System.Text.Json.JsonException
            or OperationCanceledException)
        {
            detail = $"It may have been saved; check its history before releasing again.";
        }

        result.Outcome = SetReleaseGroupOutcome.Failed;
        result.Error = $"Could not merge into draft '{draft.Name}': {failure.Message} {detail}";
        return false;
    }

    private async Task<SetReleaseRecord> PostPendingAsync(CancellationToken cancellationToken)
    {
        var release = _pendingRelease!;
        SetReleaseRecord stored;
        try
        {
            stored = await _sets.PostReleaseAsync(release, cancellationToken);
        }
        catch (SetReleasePostConflictException conflict) when (conflict.Current.Id == release.Id)
        {
            // An earlier post whose reply was lost was stored.
            stored = conflict.Current;
        }

        _pendingRelease = null;
        return stored;
    }

    private static bool Created(SetReleaseGroupResult result, NestRecord record)
    {
        result.Outcome = SetReleaseGroupOutcome.Created;
        result.Record = record;
        return true;
    }

    private static bool Refuse(SetReleaseGroupResult result, string reason)
    {
        result.Outcome = SetReleaseGroupOutcome.Refused;
        result.Error = reason;
        return false;
    }

    // Only nests on the server, or whose upload failed, are kept for the caller to show.
    private static void DiscardBuilt(IEnumerable<SetReleaseGroupResult> results)
    {
        foreach (var result in results.Where(r => r.Outcome is SetReleaseGroupOutcome.NotStarted or SetReleaseGroupOutcome.Refused))
            result.Nest = null;
    }

    private static string Describe(SetReleaseGroup group) =>
        string.IsNullOrWhiteSpace(group.MaterialGrade)
            ? $"{group.MaterialName} {group.Thickness:0.####} {group.CutType.ToString().ToLowerInvariant()}"
            : $"{group.MaterialName} {group.MaterialGrade} {group.Thickness:0.####} {group.CutType.ToString().ToLowerInvariant()}";

    private static string Name(Units units) => units.ToString().ToLowerInvariant();
}
