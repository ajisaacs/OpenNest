using System.Security.Cryptography;

namespace OpenNest.Data;

/// <summary>A drawing to import into a set: its library details, package and quantity per set.</summary>
public sealed class DrawingSetImportItem
{
    public DrawingSetImportItem(string label, DrawingLibraryRecord metadata, byte[] package, long quantity)
    {
        Label = label ?? "";
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Package = package ?? throw new ArgumentNullException(nameof(package));
        Quantity = quantity;
    }

    /// <summary>How the operator knows the drawing, such as "PT01 (items 3, 7)".</summary>
    public string Label { get; }

    /// <summary>The details a new library drawing gets; customer, name, material, grade, thickness, units and cut type are its identity (D4).</summary>
    public DrawingLibraryRecord Metadata { get; }

    /// <summary>The canonical drawing package, compared by geometry with the library's.</summary>
    public byte[] Package { get; }

    public long Quantity { get; }
}

/// <summary>What an import does with one drawing.</summary>
public enum DrawingSetImportAction
{
    /// <summary>An active library drawing with the same identity holds the same geometry: it is used as it is.</summary>
    Reuse,

    /// <summary>One active library drawing has the same identity but other geometry: it gains the next revision.</summary>
    Revise,

    /// <summary>No active library drawing has this identity: a new one is uploaded.</summary>
    Create,

    /// <summary>The drawing cannot be imported; <see cref="DrawingSetImportEntry.Reason"/> says why.</summary>
    Skip,
}

/// <summary>One planned drawing: the action and the library drawing it uses or revises.</summary>
public sealed class DrawingSetImportEntry
{
    internal DrawingSetImportEntry(DrawingSetImportItem item, DrawingSetImportAction action,
        DrawingLibraryRecord? existing, string reason, Guid newId = default)
    {
        Item = item;
        Action = action;
        Existing = existing;
        Reason = reason;
        NewId = newId;
    }

    public DrawingSetImportItem Item { get; }

    public DrawingSetImportAction Action { get; }

    /// <summary>The library drawing reused or revised, as listed when planned.</summary>
    public DrawingLibraryRecord? Existing { get; }

    /// <summary>Why a skipped drawing is left out; empty otherwise.</summary>
    public string Reason { get; }

    /// <summary>The id a created drawing is uploaded under, allocated when planned.</summary>
    public Guid NewId { get; }

    /// <summary>The operator-facing account of the action, such as "New revision 4 (geometry changed)".</summary>
    public string Describe() => Action switch
    {
        DrawingSetImportAction.Reuse => $"In the library (revision {Existing!.Revision})",
        DrawingSetImportAction.Revise => $"New revision {Existing!.Revision + 1} (geometry changed)",
        DrawingSetImportAction.Create => "New library drawing",
        _ => "Not imported: " + Reason,
    };
}

/// <summary>A library write failed part-way; <see cref="Written"/> lists what was stored before it.</summary>
public sealed class DrawingSetImportException : IOException
{
    internal DrawingSetImportException(string message, IReadOnlyList<DrawingLibraryRecord> written, Exception inner)
        : base(message, inner)
    {
        Written = written;
    }

    public IReadOnlyList<DrawingLibraryRecord> Written { get; }
}

/// <summary>
/// Imports drawings (for example a BOM's) into a new drawing set through the library.
/// <see cref="PlanAsync"/> only reads: it matches each drawing to the active library
/// drawings with its identity (D4: normalized customer, name, material and grade, exact
/// thickness, units and cut type) and compares geometry. Same identity and
/// geometry: reuse the drawing. Same identity, other geometry: the next revision of that
/// drawing. No match: a new drawing. Several matches with none holding the geometry are
/// skipped, as are invalid items and a second item with the identity of an earlier one.
/// <para>
/// <see cref="ApplyAsync"/> is the only step that writes, and only the uploads and
/// revisions the plan lists; the caller confirms them first. It then adds every
/// imported drawing to a <see cref="DrawingSetEditSession"/>, which stores nothing
/// until the operator saves the set. A failed write stops the import with the drawings
/// already stored listed; planning the same drawings again reuses them.
/// </para>
/// </summary>
public sealed class DrawingSetImport
{
    private DrawingSetImport(IReadOnlyList<DrawingSetImportEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>Every item, in the order given.</summary>
    public IReadOnlyList<DrawingSetImportEntry> Entries { get; }

    public IEnumerable<DrawingSetImportEntry> Imported => Entries.Where(e => e.Action != DrawingSetImportAction.Skip);

    public int Count(DrawingSetImportAction action) => Entries.Count(e => e.Action == action);

    /// <summary>True when applying stores anything in the library.</summary>
    public bool HasWrites => Entries.Any(e => e.Action is DrawingSetImportAction.Create or DrawingSetImportAction.Revise);

    /// <param name="geometryKey">
    /// A key equal for packages with the same geometry (<c>OpenNest.IO.DrawingPackage.GeometryKey</c>):
    /// a file read from another folder records another path, and older packages carry random
    /// entity ids, so equal hashes are not required. It may
    /// throw <see cref="InvalidDataException"/> for an unreadable package.
    /// </param>
    public static async Task<DrawingSetImport> PlanAsync(IDrawingLibrary library, IEnumerable<DrawingSetImportItem> items,
        Func<byte[], string> geometryKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(geometryKey);

        var entries = new List<DrawingSetImportEntry>();
        var planned = new List<DrawingSetImportItem>();
        foreach (var item in items)
        {
            var reason = DrawingLibraryRules.GetMetadataError(item.Metadata)
                ?? DrawingLibraryRules.GetPackageError(item.Package)
                ?? (item.Quantity < 1 || item.Quantity > DrawingSetRules.MaxQuantity
                    ? $"quantity {item.Quantity:N0} is outside 1 to {DrawingSetRules.MaxQuantity:N0}"
                    : null);
            var twin = planned.FirstOrDefault(p => DrawingLibraryDuplicates.SameDuplicateKey(p.Metadata, item.Metadata));
            if (reason == null && twin != null)
                reason = $"same name and details as {twin.Label}; give one of them another material or thickness";
            if (reason != null)
            {
                entries.Add(new DrawingSetImportEntry(item, DrawingSetImportAction.Skip, null, reason));
                continue;
            }

            planned.Add(item);
            var matches = await DrawingLibraryDuplicates.FindMatchesAsync(library, item.Metadata, cancellationToken);
            var same = await FindSameGeometryAsync(library, item, matches, geometryKey, cancellationToken);
            if (same != null)
                entries.Add(new DrawingSetImportEntry(item, DrawingSetImportAction.Reuse, same, ""));
            else if (matches.Count == 1)
                entries.Add(new DrawingSetImportEntry(item, DrawingSetImportAction.Revise, matches[0], ""));
            else if (matches.Count > 1)
                entries.Add(new DrawingSetImportEntry(item, DrawingSetImportAction.Skip, null,
                    $"{matches.Count} library drawings have these details; add the right one in the set editor"));
            else
                entries.Add(new DrawingSetImportEntry(item, DrawingSetImportAction.Create, null, "", Guid.NewGuid()));
        }

        return new DrawingSetImport(entries);
    }

    // The first match holding the item's geometry: by package hash, otherwise by downloading
    // the match's current package and comparing geometry keys.
    private static async Task<DrawingLibraryRecord?> FindSameGeometryAsync(IDrawingLibrary library,
        DrawingSetImportItem item, IReadOnlyList<DrawingLibraryRecord> matches, Func<byte[], string> geometryKey,
        CancellationToken cancellationToken)
    {
        if (matches.Count == 0)
            return null;
        var hash = Sha256(item.Package);
        var same = matches.FirstOrDefault(m => string.Equals(m.PackageSha256, hash, StringComparison.OrdinalIgnoreCase));
        if (same != null)
            return same;

        var key = geometryKey(item.Package);
        foreach (var match in matches)
        {
            var stored = await library.GetRevisionPackageAsync(match.Id, match.Revision, cancellationToken);
            if (stored == null)
                continue;
            try
            {
                if (geometryKey(stored) == key)
                    return match;
            }
            catch (InvalidDataException)
            {
                // An unreadable stored package is not the same geometry; revising replaces it.
            }
        }

        return null;
    }

    /// <summary>The library changes <see cref="ApplyAsync"/> makes, one line each, for the operator to confirm.</summary>
    public string DescribeWrites()
    {
        var lines = new List<string>();
        var created = Entries.Where(e => e.Action == DrawingSetImportAction.Create).ToList();
        var revised = Entries.Where(e => e.Action == DrawingSetImportAction.Revise).ToList();
        if (created.Count > 0)
        {
            lines.Add($"{created.Count} new library drawing{(created.Count == 1 ? "" : "s")}:");
            lines.AddRange(created.Select(e => "- " + e.Item.Label));
        }

        if (revised.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add("");
            lines.Add($"{revised.Count} new revision{(revised.Count == 1 ? "" : "s")} of library drawings whose geometry changed:");
            lines.AddRange(revised.Select(e => $"- {e.Item.Label}: revision {e.Existing!.Revision} to {e.Existing.Revision + 1}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Stores the planned uploads and revisions, in order, then adds every imported drawing
    /// to <paramref name="edit"/> at its quantity. A revision is guarded by the revision it
    /// was planned against and keeps that drawing's details, notes and settings. When a
    /// write fails, <paramref name="edit"/> is left unchanged and a
    /// <see cref="DrawingSetImportException"/> lists what was stored before it.
    /// </summary>
    public async Task<IReadOnlyList<DrawingLibraryRecord>> ApplyAsync(IDrawingLibraryStore library,
        DrawingSetEditSession edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(edit);

        var written = new List<DrawingLibraryRecord>();
        var members = new List<(DrawingLibraryRecord Record, int Quantity)>();
        foreach (var entry in Imported)
        {
            DrawingLibraryRecord record;
            try
            {
                record = entry.Action switch
                {
                    DrawingSetImportAction.Create => await library.UploadAsync(Created(entry), entry.Item.Package, cancellationToken),
                    DrawingSetImportAction.Revise => await library.UpdatePackageAsync(entry.Existing!.Id,
                        Revised(entry.Existing, entry.Item.Metadata.SavedBy), entry.Item.Package, entry.Existing.Revision, cancellationToken),
                    _ => entry.Existing!,
                };
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                var stored = written.Count == 0
                    ? "Nothing was stored in the library."
                    : $"Already stored: {string.Join(", ", written.Select(r => r.Name))}. Importing again reuses them.";
                throw new DrawingSetImportException(
                    $"Could not store '{entry.Item.Label}' in the drawing library: {ex.Message}{Environment.NewLine}{stored}",
                    written, ex);
            }

            var expectedId = entry.Action == DrawingSetImportAction.Create ? entry.NewId : entry.Existing!.Id;
            if (record.Id != expectedId)
                throw new DrawingSetImportException(
                    $"The nest server returned a different library drawing than '{entry.Item.Label}'. Import again to check what was stored.",
                    written, new IOException("Unexpected drawing id."));
            if (entry.Action != DrawingSetImportAction.Reuse)
                written.Add(record);
            members.Add((record, (int)entry.Item.Quantity));
        }

        foreach (var (record, quantity) in members)
            edit.AddDrawings(new[] { record }, quantity);
        return written;
    }

    private static DrawingLibraryRecord Created(DrawingSetImportEntry entry)
    {
        var record = Copy(entry.Item.Metadata);
        record.Id = entry.NewId;
        record.Revision = 0;
        record.Thickness = DrawingLibraryRules.NormalizeThickness(record.Thickness);
        record.Archived = false;
        return record;
    }

    // The identity already matches; keep the stored spelling, notes and settings.
    private static DrawingLibraryRecord Revised(DrawingLibraryRecord existing, string savedBy)
    {
        var record = Copy(existing);
        record.Revision = 0;
        record.SavedBy = savedBy;
        return record;
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
        SavedBy = record.SavedBy,
    };

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
