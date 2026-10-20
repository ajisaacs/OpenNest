using OpenNest;

namespace OpenNest.Data;

/// <summary>One library drawing a nest would add to a set, with the nest's required quantity.</summary>
public sealed class NestSetCandidate
{
    internal NestSetCandidate(DrawingLibraryLink link, string name, int quantity)
    {
        Link = link;
        Name = name;
        Quantity = quantity;
    }

    /// <summary>The link of the first nest drawing copied from this library drawing.</summary>
    public DrawingLibraryLink Link { get; }

    public Guid DrawingId => Link.Id;

    /// <summary>The nest drawing's name.</summary>
    public string Name { get; }

    /// <summary>The required quantity, summed over every nest drawing copied from this library drawing.</summary>
    public int Quantity { get; }
}

/// <summary>A nest drawing left out of the set, and why.</summary>
public sealed record NestSetSkip(string Name, string Reason);

/// <summary>A drawing the set already lists: its quantity there is kept, not replaced or added to.</summary>
public sealed record NestSetKept(string Name, int SetQuantity, int NestQuantity);

/// <summary>What <see cref="NestSetMerge.ApplyAsync"/> changed in the set editor.</summary>
public sealed class NestSetMergeResult
{
    internal NestSetMergeResult(IReadOnlyList<NestSetCandidate> added, IReadOnlyList<NestSetKept> kept,
        IReadOnlyList<NestSetSkip> skipped, IReadOnlyList<string> warnings)
    {
        Added = added;
        Kept = kept;
        Skipped = skipped;
        Warnings = warnings;
    }

    /// <summary>The drawings appended to the set, in order.</summary>
    public IReadOnlyList<NestSetCandidate> Added { get; }

    public IReadOnlyList<NestSetKept> Kept { get; }

    /// <summary>Every nest drawing left out: those the plan skipped, then those the library no longer holds.</summary>
    public IReadOnlyList<NestSetSkip> Skipped { get; }

    /// <summary>Added drawings the operator should know about (archived, or newer in the library).</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>True when the operator only needs to review the editor, with nothing else to read first.</summary>
    public bool IsClean => Kept.Count == 0 && Skipped.Count == 0 && Warnings.Count == 0;

    /// <summary>The operator-facing account of what was added, kept, skipped and why.</summary>
    public string Describe(string setName)
    {
        var lines = new List<string>();
        var target = string.IsNullOrWhiteSpace(setName) ? "the new set" : $"set '{setName}'";
        lines.Add(Added.Count == 0
            ? $"No drawings were added to {target}."
            : $"Added {Added.Count} drawing{(Added.Count == 1 ? "" : "s")} to {target}.");

        if (Kept.Count > 0)
        {
            lines.Add("");
            lines.Add("Already in the set (its quantity is kept; edit it there to change it):");
            lines.AddRange(Kept.Select(k => $"- {k.Name}: set {k.SetQuantity}, nest {k.NestQuantity}"));
        }

        if (Skipped.Count > 0)
        {
            lines.Add("");
            lines.Add("Skipped:");
            lines.Add(Lines(Skipped));
        }

        if (Warnings.Count > 0)
        {
            lines.Add("");
            lines.Add("Check:");
            lines.AddRange(Warnings.Select(w => "- " + w));
        }

        return string.Join(Environment.NewLine, lines);
    }

    internal static string Lines(IEnumerable<NestSetSkip> skipped) =>
        string.Join(Environment.NewLine, skipped.Select(s => $"- {s.Name}: {s.Reason}"));
}

/// <summary>
/// Adds a nest's library drawings to a drawing set (Slice 6). <see cref="Plan"/> is a snapshot of
/// the nest taken before any I/O: each library drawing on this server once, with the required
/// quantities of its nest copies summed. Drawings that were never saved to the library, came from
/// another server or have no required quantity are skipped with a reason; cutoffs are not parts
/// and are left out silently. <see cref="ApplyAsync"/> then reads each drawing's current library
/// record and appends it to a <see cref="DrawingSetEditSession"/>, which the operator reviews and
/// saves. A drawing the set already lists keeps the set's quantity: a nest released from a set
/// carries its multiplier, so copying its quantities back would scale the kit.
/// </summary>
public sealed class NestSetMerge
{
    private const string UnlinkedReason = "not in the drawing library (save it to the library first)";

    private NestSetMerge(IReadOnlyList<NestSetCandidate> candidates, IReadOnlyList<NestSetSkip> skipped)
    {
        Candidates = candidates;
        Skipped = skipped;
    }

    /// <summary>The library drawings to add, in the drawing list's (name) order.</summary>
    public IReadOnlyList<NestSetCandidate> Candidates { get; }

    public IReadOnlyList<NestSetSkip> Skipped { get; }

    /// <summary>The skipped drawings, one "- name: reason" line each.</summary>
    public string DescribeSkipped() => NestSetMergeResult.Lines(Skipped);

    public static NestSetMerge Plan(Nest nest, string serverUrl)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var serverKey = ServerUrl.Key(serverUrl);
        if (serverKey.Length == 0)
            throw new ArgumentException("A nest server URL is required.", nameof(serverUrl));

        var skipped = new List<NestSetSkip>();
        var linked = new List<(DrawingLibraryLink Link, string Name, int Quantity)>();
        foreach (var drawing in nest.Drawings.Where(d => d != null && !d.IsCutOff).OrderBy(d => d.Name))
        {
            var name = drawing.Name ?? "";
            var link = drawing.Library;
            if (link == null)
                skipped.Add(new NestSetSkip(name, UnlinkedReason));
            else if (!string.Equals(link.ServerKey, serverKey, StringComparison.Ordinal))
                skipped.Add(new NestSetSkip(name, $"from the drawing library on another server ({link.ServerKey})"));
            else if (drawing.Quantity.Required < 1)
                skipped.Add(new NestSetSkip(name, "has no required quantity"));
            else
                linked.Add((link, name, drawing.Quantity.Required));
        }

        var candidates = new List<NestSetCandidate>();
        foreach (var group in linked.GroupBy(l => l.Link.Id))
        {
            var first = group.First();
            var quantity = group.Sum(l => (long)l.Quantity);
            if (quantity > DrawingSetRules.MaxQuantity)
            {
                skipped.Add(new NestSetSkip(first.Name,
                    $"quantity {quantity:N0} is more than a set member allows ({DrawingSetRules.MaxQuantity:N0})"));
                continue;
            }

            candidates.Add(new NestSetCandidate(first.Link, first.Name, (int)quantity));
        }

        return new NestSetMerge(candidates, skipped);
    }

    /// <summary>
    /// Reads each candidate's current library record and appends the drawings
    /// <paramref name="edit"/> does not already list. Nothing is saved; a read failure throws
    /// before <paramref name="edit"/> changes.
    /// </summary>
    public async Task<NestSetMergeResult> ApplyAsync(IDrawingLibraryStore library, DrawingSetEditSession edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(edit);

        var skipped = new List<NestSetSkip>(Skipped);
        var kept = new List<NestSetKept>();
        var found = new List<(NestSetCandidate Candidate, DrawingLibraryRecord Record)>();
        foreach (var candidate in Candidates)
        {
            var existing = edit.Members.FirstOrDefault(m => m.DrawingId == candidate.DrawingId);
            if (existing != null)
            {
                kept.Add(new NestSetKept(candidate.Name, existing.Quantity, candidate.Quantity));
                continue;
            }

            var record = await library.GetAsync(candidate.DrawingId, cancellationToken);
            if (record == null)
                skipped.Add(new NestSetSkip(candidate.Name, "no longer in the drawing library on this server"));
            else
                found.Add((candidate, record));
        }

        var added = new List<NestSetCandidate>();
        var warnings = new List<string>();
        foreach (var (candidate, record) in found)
        {
            edit.AddDrawings(new[] { record }, candidate.Quantity);
            added.Add(candidate);
            if (record.Archived)
                warnings.Add($"'{record.Name}' is archived in the library; releases skip it unless it is ticked back in.");
            else if (record.Revision > candidate.Link.Revision)
                warnings.Add($"'{record.Name}' is at revision {record.Revision} in the library; this nest has revision "
                    + $"{candidate.Link.Revision}. Sets release the latest revision.");
        }

        return new NestSetMergeResult(added, kept, skipped, warnings);
    }
}
