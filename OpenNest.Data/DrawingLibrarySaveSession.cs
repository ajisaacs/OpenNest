using System.Security.Cryptography;
using OpenNest;

namespace OpenNest.Data;

/// <summary>What one save to the drawing library did.</summary>
public enum DrawingLibrarySaveOutcome
{
    /// <summary>A new library drawing was stored as revision 1.</summary>
    Created,

    /// <summary>The chosen library drawing gained the next revision.</summary>
    Revised,

    /// <summary>The chosen library drawing already held this exact package and metadata.</summary>
    Unchanged,
}

/// <summary>The saved library revision; the nest drawing's link now points at it.</summary>
public sealed class DrawingLibrarySaveResult
{
    internal DrawingLibrarySaveResult(DrawingLibraryRecord saved, DrawingLibrarySaveOutcome outcome, bool recoveredEarlierSave)
    {
        Saved = saved;
        Outcome = outcome;
        RecoveredEarlierSave = recoveredEarlierSave;
    }

    public DrawingLibraryRecord Saved { get; }

    public DrawingLibrarySaveOutcome Outcome { get; }

    /// <summary>
    /// True when an earlier save whose reply was lost had in fact been stored, and it
    /// differed from this request. That earlier save is the result; this request was not
    /// sent, so the operator must save again to store anything changed since.
    /// </summary>
    public bool RecoveredEarlierSave { get; }
}

/// <summary>
/// Active library drawings that a save could revise (D4): the ones whose normalized
/// customer, name, material and grade, and exact thickness, units and cut type equal the
/// metadata being saved, newest first, plus the drawing's own linked library drawing.
/// </summary>
public sealed class DrawingLibraryDuplicates
{
    internal DrawingLibraryDuplicates(DrawingLibraryRecord metadata, IReadOnlyList<DrawingLibraryRecord> matches,
        DrawingLibraryRecord? linked)
    {
        Metadata = metadata;
        Matches = matches;
        Linked = linked;
        if (linked != null && matches.Any(m => m.Id == linked.Id))
            Default = matches.First(m => m.Id == linked.Id);
        else if (matches.Count == 1)
            Default = matches[0];
    }

    /// <summary>A copy of the metadata these duplicates were found for.</summary>
    public DrawingLibraryRecord Metadata { get; }

    public IReadOnlyList<DrawingLibraryRecord> Matches { get; }

    /// <summary>
    /// The drawing's linked library drawing on this server when it is active, even if its
    /// details no longer match; revising it is offered but is only the default when it matches.
    /// </summary>
    public DrawingLibraryRecord? Linked { get; }

    /// <summary>Every drawing that may be revised: the matches, then the linked drawing if it is not one.</summary>
    public IReadOnlyList<DrawingLibraryRecord> Choices =>
        Linked == null || Matches.Any(m => m.Id == Linked.Id) ? Matches : Matches.Append(Linked).ToList();

    /// <summary>
    /// The drawing to revise by default: the linked drawing when it matches, otherwise the
    /// only match. Null means create a separate drawing when there is no match, or that the
    /// operator must choose between several (<see cref="RequiresChoice"/>).
    /// </summary>
    public DrawingLibraryRecord? Default { get; }

    /// <summary>Several drawings match and none is linked: the operator has to pick one, or a separate drawing.</summary>
    public bool RequiresChoice => Default == null && Matches.Count > 1;

    /// <summary>True when these duplicates were found for exactly <paramref name="metadata"/>'s matching details.</summary>
    public bool IsFor(DrawingLibraryRecord metadata) => SameDuplicateKey(Metadata, metadata);

    /// <summary>
    /// The active library drawings whose duplicate key equals <paramref name="metadata"/>'s,
    /// newest first, reading every page the server filters down to; none when it is invalid.
    /// </summary>
    internal static async Task<List<DrawingLibraryRecord>> FindMatchesAsync(IDrawingLibrary library,
        DrawingLibraryRecord metadata, CancellationToken cancellationToken)
    {
        var matches = new List<DrawingLibraryRecord>();
        if (string.IsNullOrWhiteSpace(metadata.Name) || DrawingLibraryRules.GetMetadataError(metadata) != null)
            return matches;

        // The server matches customer, material and grade by the same normalized key. A
        // blank one means "any" there, so every key is checked again here; the name search
        // narrows by its longest word because LIKE does not collapse whitespace.
        var offset = 0;
        while (true)
        {
            var page = await library.QueryAsync(new DrawingLibraryQuery
            {
                Search = LongestWord(metadata.Name),
                Customer = metadata.Customer,
                MaterialName = metadata.MaterialName,
                MaterialGrade = metadata.MaterialGrade,
                Thickness = metadata.Thickness,
                CutType = metadata.CutType,
                Sort = DrawingSortField.SavedAt,
                Descending = true,
                Offset = offset,
                Limit = DrawingLibraryQuery.MaxLimit,
            }, cancellationToken);
            matches.AddRange(page.Items.Where(r => !r.Archived && SameDuplicateKey(r, metadata)
                && matches.All(m => m.Id != r.Id)));
            offset += page.Items.Count;
            if (page.Items.Count == 0 || offset >= page.Total)
                return matches;
            if (offset >= DrawingLibrarySaveSession.MaxScannedDrawings)
                throw new IOException($"More than {DrawingLibrarySaveSession.MaxScannedDrawings} library drawings share this name's details. Narrow the name or customer before saving.");
        }
    }

    private static string LongestWord(string name) =>
        name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .OrderByDescending(word => word.Length)
            .FirstOrDefault() ?? "";

    internal static bool SameDuplicateKey(DrawingLibraryRecord a, DrawingLibraryRecord b) =>
        SharedListNames.Key(a.Customer) == SharedListNames.Key(b.Customer)
        && SharedListNames.Key(a.Name) == SharedListNames.Key(b.Name)
        && SharedListNames.Key(a.MaterialName) == SharedListNames.Key(b.MaterialName)
        && SharedListNames.Key(a.MaterialGrade) == SharedListNames.Key(b.MaterialGrade)
        && DrawingLibraryRules.NormalizeThickness(a.Thickness) == DrawingLibraryRules.NormalizeThickness(b.Thickness)
        && a.Units == b.Units
        && a.CutType == b.CutType;
}

/// <summary>
/// Saves one nest drawing to the server drawing library, as a new library drawing or as
/// the next revision of a chosen one (D4), and on success points the drawing's library
/// link at the saved revision. The nest's copy is otherwise unchanged (D2).
/// <para>
/// A new drawing's id is allocated before the upload. When a save fails without a
/// definite answer it stays pending: the next save first checks whether it was stored,
/// adopts it if so, and otherwise retries a new drawing under the same id. A pending save
/// is never written onto a different library drawing.
/// </para>
/// </summary>
public sealed class DrawingLibrarySaveSession
{
    /// <summary>Most library rows the duplicate check reads before giving up.</summary>
    public const int MaxScannedDrawings = 10 * DrawingLibraryQuery.MaxLimit;

    private readonly IDrawingLibraryStore _library;
    private PendingSave? _pending;

    public DrawingLibrarySaveSession(IDrawingLibraryStore library, string serverUrl, Nest nest, Drawing drawing)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        Nest = nest ?? throw new ArgumentNullException(nameof(nest));
        Drawing = drawing ?? throw new ArgumentNullException(nameof(drawing));
        ServerKey = ServerUrl.Key(serverUrl);
        if (ServerKey.Length == 0)
            throw new ArgumentException("A library save needs the nest server URL.", nameof(serverUrl));
    }

    public Nest Nest { get; }

    public Drawing Drawing { get; }

    /// <summary>The normalized server URL saved links are bound to.</summary>
    public string ServerKey { get; }

    /// <summary>An earlier save failed without a definite answer; the next save checks it first.</summary>
    public bool HasPendingSave => _pending != null;

    /// <summary>
    /// Library details to start from: the drawing's name, its customer (or the nest's), the
    /// nest's material and grade (or the drawing's when the nest has none), the nest's
    /// thickness and units, the linked revision's cut type and notes when the drawing
    /// came from this server's library, otherwise <paramref name="lastCutType"/>, and the
    /// drawing's own nesting settings.
    /// </summary>
    public DrawingLibraryRecord Prefill(CutType lastCutType, string savedBy)
    {
        var link = Drawing.Library is { } l && l.ServerKey == ServerKey ? l : null;
        var material = !string.IsNullOrWhiteSpace(Nest.Material?.Name) ? Nest.Material : Drawing.Material;
        return new DrawingLibraryRecord
        {
            Name = Drawing.Name ?? "",
            Customer = !string.IsNullOrWhiteSpace(Drawing.Customer) ? Drawing.Customer : Nest.Customer ?? "",
            MaterialName = material?.Name ?? "",
            MaterialGrade = material?.Grade ?? "",
            Thickness = DrawingLibraryRules.NormalizeThickness(double.IsFinite(Nest.Thickness) && Nest.Thickness > 0 ? Nest.Thickness : 0),
            Units = Nest.Units,
            CutType = link?.CutType ?? (Enum.IsDefined(lastCutType) ? lastCutType : CutType.Laser),
            Notes = link?.Notes ?? "",
            Settings = DrawingNestingSettings.FromDrawing(Drawing),
            SavedBy = savedBy ?? "",
        };
    }

    /// <summary>
    /// Finds the active library drawings <paramref name="metadata"/> duplicates, reading
    /// every page the server filters down to, and the drawing's linked library drawing.
    /// </summary>
    public async Task<DrawingLibraryDuplicates> FindDuplicatesAsync(DrawingLibraryRecord metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var snapshot = Copy(metadata);
        var matches = await DrawingLibraryDuplicates.FindMatchesAsync(_library, snapshot, cancellationToken);

        DrawingLibraryRecord? linked = null;
        if (Drawing.Library is { } link && link.ServerKey == ServerKey
            && await _library.GetAsync(link.Id, cancellationToken) is { Archived: false } current && current.Id == link.Id)
            linked = matches.FirstOrDefault(m => m.Id == current.Id) ?? current;

        return new DrawingLibraryDuplicates(snapshot, matches, linked);
    }

    /// <summary>
    /// Saves <paramref name="package"/> (the drawing packaged in the nest's units) with
    /// <paramref name="metadata"/>: as the next revision of <paramref name="revise"/> at the
    /// revision it was listed with, or as a new library drawing when it is null. A stale
    /// revision raises <see cref="DrawingLibraryConflictException"/> and changes nothing.
    /// </summary>
    public async Task<DrawingLibrarySaveResult> SaveAsync(DrawingLibraryRecord metadata, byte[] package,
        DrawingLibraryRecord? revise, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(package);
        var error = DrawingLibraryRules.GetMetadataError(metadata) ?? DrawingLibraryRules.GetPackageError(package);
        if (error != null)
            throw new ArgumentException(error, nameof(metadata));
        if (metadata.Units != Nest.Units)
            throw new ArgumentException("A library drawing is saved in its nest's units.", nameof(metadata));
        if (revise != null && (revise.Id == Guid.Empty || revise.Revision < 1))
            throw new ArgumentException("Choose a listed library drawing to revise.", nameof(revise));

        var reuseId = Guid.Empty;
        if (_pending != null)
        {
            var earlier = _pending;
            var stored = await FindStoredAsync(earlier, cancellationToken);
            if (stored != null)
            {
                _pending = null;
                var sameRequest = earlier.Matches(metadata, package, revise);
                return Finish(stored.Value.Record, stored.Value.Outcome, recoveredEarlierSave: !sameRequest);
            }

            // Not stored: a new drawing may be retried, but only ever under the id it was given.
            reuseId = earlier.ReviseId == Guid.Empty ? earlier.Id : Guid.Empty;
            _pending = null;
        }

        var request = Copy(metadata);
        var pending = revise == null
            ? new PendingSave(reuseId != Guid.Empty ? reuseId : Guid.NewGuid(), Guid.Empty, 0, request, package)
            : new PendingSave(revise.Id, revise.Id, revise.Revision, request, package);
        request.Id = pending.Id;
        request.Revision = 0;

        DrawingLibraryRecord saved;
        _pending = pending;
        try
        {
            saved = revise == null
                ? await _library.UploadAsync(request, package, cancellationToken)
                : await _library.UpdatePackageAsync(revise.Id, request, package, revise.Revision, cancellationToken);
        }
        catch (Exception ex) when (ex is DrawingLibraryConflictException or ServerTooOldException or ArgumentException)
        {
            // A definite refusal: nothing was stored. Any other failure may have been stored
            // before its reply was lost, so it stays pending for the next save to check.
            _pending = null;
            throw;
        }

        if (saved.Id != pending.Id || saved.Revision < 1 || !pending.SameStored(saved))
            throw new IOException("The nest server returned a different library drawing than was saved. Save again to check what was stored.");
        _pending = null;
        var outcome = revise == null ? DrawingLibrarySaveOutcome.Created
            : saved.Revision == revise.Revision ? DrawingLibrarySaveOutcome.Unchanged
            : DrawingLibrarySaveOutcome.Revised;
        return Finish(saved, outcome, recoveredEarlierSave: false);
    }

    // The revision a pending save would have produced, when the server holds it with exactly
    // the pending package and metadata; null when it was not stored.
    private async Task<(DrawingLibraryRecord Record, DrawingLibrarySaveOutcome Outcome)?> FindStoredAsync(
        PendingSave pending, CancellationToken cancellationToken)
    {
        var current = await _library.GetAsync(pending.Id, cancellationToken);
        if (pending.ReviseId == Guid.Empty)
        {
            if (current == null)
                return null;
            var first = await RevisionAsync(current, 1, cancellationToken);
            if (first == null || !pending.SameStored(first))
            {
                _pending = null;
                throw new IOException("Another library drawing already uses the id of the save that failed. Close this dialog and save again.");
            }
            return (first, DrawingLibrarySaveOutcome.Created);
        }

        if (current == null)
        {
            _pending = null;
            throw new IOException("The library drawing being revised no longer exists on the nest server.");
        }

        if (current.Revision == pending.ExpectedRevision)
            return pending.SameStored(current) ? (current, DrawingLibrarySaveOutcome.Unchanged) : null;

        var next = current.Revision > pending.ExpectedRevision
            ? await RevisionAsync(current, pending.ExpectedRevision + 1, cancellationToken)
            : null;
        if (next != null && pending.SameStored(next))
            return (next, DrawingLibrarySaveOutcome.Revised);

        // Someone else saved the next revision; the pending save can never be stored now.
        _pending = null;
        throw new DrawingLibraryConflictException(current);
    }

    private async Task<DrawingLibraryRecord?> RevisionAsync(DrawingLibraryRecord current, int revision,
        CancellationToken cancellationToken)
    {
        if (revision < 1 || revision > current.Revision)
            return null;
        // Revisions are listed newest first, so revision r of n sits at offset n - r.
        var page = await _library.GetRevisionsAsync(current.Id, current.Revision - revision, 1, cancellationToken);
        var record = page?.Items.FirstOrDefault();
        return record != null && record.Id == current.Id && record.Revision == revision ? record : null;
    }

    private DrawingLibrarySaveResult Finish(DrawingLibraryRecord saved, DrawingLibrarySaveOutcome outcome,
        bool recoveredEarlierSave)
    {
        Drawing.Library = new DrawingLibraryLink(ServerKey, saved.Id, saved.Revision, saved.CutType,
            saved.Thickness, saved.MaterialName, saved.MaterialGrade, saved.Units, saved.Notes,
            saved.PackageSha256.ToLowerInvariant());
        return new DrawingLibrarySaveResult(saved, outcome, recoveredEarlierSave);
    }

    private static DrawingLibraryRecord Copy(DrawingLibraryRecord record) => new()
    {
        Id = record.Id,
        Revision = record.Revision,
        Name = record.Name,
        Customer = record.Customer,
        MaterialName = record.MaterialName,
        MaterialGrade = record.MaterialGrade,
        Thickness = record.Thickness,
        Units = record.Units,
        CutType = record.CutType,
        Notes = record.Notes,
        Settings = record.Settings,
        Archived = record.Archived,
        PackageSha256 = record.PackageSha256,
        PackageSize = record.PackageSize,
        SavedAt = record.SavedAt,
        SavedBy = record.SavedBy,
    };

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class PendingSave
    {
        public PendingSave(Guid id, Guid reviseId, int expectedRevision, DrawingLibraryRecord metadata, byte[] package)
        {
            Id = id;
            ReviseId = reviseId;
            ExpectedRevision = expectedRevision;
            Metadata = metadata;
            Package = package.ToArray();
            PackageSha256 = Sha256(Package);
        }

        public Guid Id { get; }

        /// <summary>The drawing being revised, or empty for a new drawing.</summary>
        public Guid ReviseId { get; }

        public int ExpectedRevision { get; }

        public DrawingLibraryRecord Metadata { get; }

        public byte[] Package { get; }

        public string PackageSha256 { get; }

        /// <summary>True when the server's <paramref name="record"/> holds exactly this package and these details.</summary>
        public bool SameStored(DrawingLibraryRecord record) =>
            string.Equals(record.PackageSha256, PackageSha256, StringComparison.OrdinalIgnoreCase)
            && record.Name == Metadata.Name && record.Customer == Metadata.Customer
            && record.MaterialName == Metadata.MaterialName && record.MaterialGrade == Metadata.MaterialGrade
            && record.Thickness == DrawingLibraryRules.NormalizeThickness(Metadata.Thickness)
            && record.Units == Metadata.Units && record.CutType == Metadata.CutType && record.Notes == Metadata.Notes
            && Equals(record.Settings, Metadata.Settings);

        /// <summary>True when a new request asks for exactly this save again.</summary>
        public bool Matches(DrawingLibraryRecord metadata, byte[] package, DrawingLibraryRecord? revise) =>
            (revise == null ? ReviseId == Guid.Empty : ReviseId == revise.Id && ExpectedRevision == revise.Revision)
            && Package.AsSpan().SequenceEqual(package)
            && metadata.Name == Metadata.Name && metadata.Customer == Metadata.Customer
            && metadata.MaterialName == Metadata.MaterialName && metadata.MaterialGrade == Metadata.MaterialGrade
            && DrawingLibraryRules.NormalizeThickness(metadata.Thickness) == DrawingLibraryRules.NormalizeThickness(Metadata.Thickness)
            && metadata.Units == Metadata.Units && metadata.CutType == Metadata.CutType && metadata.Notes == Metadata.Notes
            && Equals(metadata.Settings, Metadata.Settings);
    }
}
