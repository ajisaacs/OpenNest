using System.Globalization;
using OpenNest;

namespace OpenNest.Data;

/// <summary>One set of a release plan: a set read from the server and how many kits of it.</summary>
public sealed record SetReleasePlanLine(DrawingSetRecord Set, int Quantity);

/// <summary>
/// One library drawing released on its own, outside any set: its current record, as read from
/// the library, and how many of it.
/// </summary>
public sealed record SetReleaseDrawingLine(DrawingLibraryRecord Drawing, int Quantity);

/// <summary>
/// How many of an item one set of the release asked for, kits included; <see cref="SetId"/>
/// is null for the drawing released on its own.
/// </summary>
public sealed record SetReleaseItemSource(Guid? SetId, int Quantity);

/// <summary>
/// One library drawing as released: its current record and the quantity summed over every
/// set of the release that lists it, kits included, and over the drawing released on its own.
/// </summary>
public sealed class SetReleaseItem
{
    internal SetReleaseItem(DrawingSetMember member, DrawingLibraryRecord drawing, IReadOnlyList<SetReleaseItemSource> sources)
    {
        Member = member;
        Drawing = drawing;
        Sources = sources;
        Quantity = checked(sources.Sum(s => s.Quantity));
    }

    /// <summary>
    /// The member that first listed the drawing, in set order; for a drawing that no set
    /// lists, a member made from its line released on its own.
    /// </summary>
    public DrawingSetMember Member { get; }

    /// <summary>
    /// The drawing's current library record, as joined by the server when the sets were read;
    /// the newest revision when sets read at different times disagree.
    /// </summary>
    public DrawingLibraryRecord Drawing { get; }

    /// <summary>
    /// Each set's share, its member quantity times its kits, in set order, then the quantity
    /// released on its own (with no set).
    /// </summary>
    public IReadOnlyList<SetReleaseItemSource> Sources { get; }

    /// <summary>The total over <see cref="Sources"/>.</summary>
    public int Quantity { get; }

    /// <summary>The drawing is archived and was ticked back in.</summary>
    public bool Archived => Drawing.Archived;
}

/// <summary>
/// The members that can share one nest: the same material name, grade, thickness, units
/// and cut type (S3). Each group becomes one nest when the set is released.
/// </summary>
public sealed class SetReleaseGroup
{
    internal SetReleaseGroup(string key, DrawingLibraryRecord first, bool unassigned,
        IReadOnlyList<SetReleaseItem> items, IReadOnlyList<string> warnings, string refusal)
    {
        Key = key;
        MaterialName = (first.MaterialName ?? "").Trim();
        MaterialGrade = (first.MaterialGrade ?? "").Trim();
        Thickness = DrawingLibraryRules.NormalizeThickness(first.Thickness);
        Units = first.Units;
        CutType = first.CutType;
        IsUnassigned = unassigned;
        Items = items;
        Warnings = warnings;
        Refusal = refusal;
    }

    /// <summary>Identifies the group across rebuilds of the plan (for example after the multiplier changes).</summary>
    public string Key { get; }

    /// <summary>The material as the group's first member spells it.</summary>
    public string MaterialName { get; }

    public string MaterialGrade { get; }

    /// <summary>Thickness in <see cref="Units"/>, rounded to four decimals.</summary>
    public double Thickness { get; }

    public Units Units { get; }

    public CutType CutType { get; }

    /// <summary>
    /// The members whose drawings have no material or no thickness. They cannot be released
    /// until the library drawings are given one, so the group is always refused.
    /// </summary>
    public bool IsUnassigned { get; }

    /// <summary>The members, in set order.</summary>
    public IReadOnlyList<SetReleaseItem> Items { get; }

    /// <summary>Sum of the released quantities.</summary>
    public int TotalQuantity => Items.Sum(i => i.Quantity);

    /// <summary>Operator-facing notes that do not stop the release.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Why this group cannot be released; empty when it can.</summary>
    public string Refusal { get; }

    public bool CanRelease => Refusal.Length == 0;
}

/// <summary>
/// How drawing set revisions and library drawings released on their own split into nests,
/// before anything is downloaded or created. Pure: it reads the members' joined drawing
/// records and the drawings' records only. A drawing listed by several sets, or by a set and
/// on its own, becomes one item with the quantities summed, each share kept. Items are
/// grouped by material name and grade (compared by <see cref="SharedListNames.Key"/>),
/// thickness (rounded as the library stores it), units and cut type. Archived drawings are
/// skipped unless included (S5). A group is refused when two of its drawings share a name,
/// because a nest keys drawings by name (S6).
/// </summary>
public sealed class SetReleasePlan
{
    private SetReleasePlan(IReadOnlyList<SetReleasePlanLine> lines, IReadOnlyList<SetReleaseDrawingLine> drawings,
        bool includeArchived, IReadOnlyList<SetReleaseGroup> groups, IReadOnlyList<DrawingSetMember> skippedArchived)
    {
        Lines = lines;
        Drawings = drawings;
        IncludeArchived = includeArchived;
        Groups = groups;
        SkippedArchived = skippedArchived;
        var customers = lines.Select(l => l.Set.Customer).Concat(drawings.Select(d => d.Drawing.Customer))
            .Select(c => (c ?? "").Trim()).Distinct(StringComparer.Ordinal).ToList();
        Customer = customers.Count == 1 ? customers[0] : "";
    }

    /// <summary>The sets released and how many kits of each, as read from the server.</summary>
    public IReadOnlyList<SetReleasePlanLine> Lines { get; }

    /// <summary>The library drawings released on their own and how many of each, as read from the library.</summary>
    public IReadOnlyList<SetReleaseDrawingLine> Drawings { get; }

    /// <summary>The customer every set and every drawing on its own names, or blank when they differ.</summary>
    public string Customer { get; }

    public bool IncludeArchived { get; }

    /// <summary>
    /// Groups ordered by material, grade, thickness, units and cut type, with the
    /// unassigned group last.
    /// </summary>
    public IReadOnlyList<SetReleaseGroup> Groups { get; }

    /// <summary>
    /// Members left out because their drawing is archived and archived drawings were not
    /// included; a drawing released on its own is listed as a member made from its line.
    /// </summary>
    public IReadOnlyList<DrawingSetMember> SkippedArchived { get; }

    /// <param name="set">A set read from the server, so every member carries its drawing's current record.</param>
    public static SetReleasePlan Build(DrawingSetRecord set, int multiplier, bool includeArchived) =>
        Build(new[] { new SetReleasePlanLine(set, multiplier) }, includeArchived);

    /// <param name="lines">Sets read from the server, so every member carries its drawing's current record.</param>
    public static SetReleasePlan Build(IReadOnlyList<SetReleasePlanLine> lines, bool includeArchived) =>
        Build(lines, Array.Empty<SetReleaseDrawingLine>(), includeArchived);

    /// <param name="lines">Sets read from the server, so every member carries its drawing's current record; may be empty.</param>
    /// <param name="drawings">Library drawings released on their own, as read from the library; may be empty.</param>
    public static SetReleasePlan Build(IReadOnlyList<SetReleasePlanLine> lines, IReadOnlyList<SetReleaseDrawingLine> drawings,
        bool includeArchived)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(drawings);
        // Both empty makes an empty plan: the release dialog starts that way and fills in.
        if (lines.Count > SetReleaseRules.MaxSetLines)
            throw new ArgumentException($"Release at most {SetReleaseRules.MaxSetLines} sets.", nameof(lines));
        if (drawings.Count > SetReleaseRules.MaxDrawingLines)
            throw new ArgumentException($"Release at most {SetReleaseRules.MaxDrawingLines} drawings on their own.", nameof(drawings));
        var seenSets = new HashSet<Guid>();
        foreach (var line in lines)
        {
            var set = line?.Set ?? throw new ArgumentException("Every line needs a set.", nameof(lines));
            if (line.Quantity < 1 || line.Quantity > SetReleaseRules.MaxQuantity)
                throw new ArgumentOutOfRangeException(nameof(lines), line.Quantity,
                    $"The number of kits must be between 1 and {SetReleaseRules.MaxQuantity}.");
            if (set.Id == Guid.Empty || set.Revision < 1)
                throw new ArgumentException("Only a set read from the server can be released.", nameof(lines));
            if (!seenSets.Add(set.Id))
                throw new ArgumentException($"Set '{set.Name}' is listed twice. Release it once with more kits.", nameof(lines));
            var members = set.Members ?? throw new ArgumentException("The set has no member list.", nameof(lines));
            if (members.Any(m => m?.Drawing is null || m.Drawing.Id != m.DrawingId))
                throw new ArgumentException("A set member is missing its library drawing. Reload the set.", nameof(lines));
        }

        var seenDrawings = new HashSet<Guid>();
        foreach (var line in drawings)
        {
            var drawing = line?.Drawing ?? throw new ArgumentException("Every drawing line needs a drawing.", nameof(drawings));
            if (line.Quantity < 1 || line.Quantity > SetReleaseRules.MaxDrawingQuantity)
                throw new ArgumentOutOfRangeException(nameof(drawings), line.Quantity,
                    $"The quantity must be between 1 and {SetReleaseRules.MaxDrawingQuantity}.");
            if (drawing.Id == Guid.Empty || drawing.Revision < 1)
                throw new ArgumentException("Only a drawing read from the library can be released.", nameof(drawings));
            if (!seenDrawings.Add(drawing.Id))
                throw new ArgumentException($"Drawing '{drawing.Name}' is listed twice. Release it once with a larger quantity.",
                    nameof(drawings));
        }

        // One entry per drawing, in first-listed order (sets, then drawings on their own); each share kept.
        var skipped = new List<DrawingSetMember>();
        var order = new List<Guid>();
        var entries = new Dictionary<Guid, (DrawingSetMember Member, DrawingLibraryRecord Drawing, List<SetReleaseItemSource> Sources)>();
        var shares = lines.SelectMany(line => line.Set.Members.Select(member =>
                (Member: member, Share: new SetReleaseItemSource(line.Set.Id, checked(member.Quantity * line.Quantity)))))
            .Concat(drawings.Select(line =>
                (Member: new DrawingSetMember { DrawingId = line.Drawing.Id, Quantity = line.Quantity, Drawing = line.Drawing },
                    Share: new SetReleaseItemSource(null, line.Quantity))));
        foreach (var (member, share) in shares)
        {
            if (member.Drawing!.Archived && !includeArchived)
            {
                skipped.Add(member);
                continue;
            }

            if (entries.TryGetValue(member.DrawingId, out var entry))
            {
                entry.Sources.Add(share);
                if (member.Drawing.Revision > entry.Drawing.Revision)
                    entries[member.DrawingId] = (entry.Member, member.Drawing, entry.Sources);
            }
            else
            {
                order.Add(member.DrawingId);
                entries[member.DrawingId] = (member, member.Drawing, new List<SetReleaseItemSource> { share });
            }
        }

        var items = order.Select(id =>
        {
            var (member, drawing, sources) = entries[id];
            var unassigned = IsUnassigned(drawing);
            return (Key: unassigned ? UnassignedKey : GroupKey(drawing), Unassigned: unassigned,
                Item: new SetReleaseItem(member, drawing, sources));
        }).ToList();

        var groups = items
            .GroupBy(i => i.Key, StringComparer.Ordinal)
            .Select(g => BuildGroup(g.Key, g.First().Unassigned, g.Select(i => i.Item).ToList()))
            .OrderBy(g => g.IsUnassigned)
            .ThenBy(g => SharedListNames.Key(g.MaterialName), StringComparer.Ordinal)
            .ThenBy(g => SharedListNames.Key(g.MaterialGrade), StringComparer.Ordinal)
            .ThenBy(g => g.Thickness)
            .ThenBy(g => g.Units)
            .ThenBy(g => g.CutType)
            .ToList();
        return new SetReleasePlan(lines.ToList(), drawings.ToList(), includeArchived, groups, skipped);
    }

    /// <summary>
    /// Draft nests on the server that <paramref name="group"/> could merge into: not deleted,
    /// status Draft, and the group's material, grade and thickness, newest saved first. Units
    /// and cut type are not in nest metadata, so the release checks them on the downloaded nest.
    /// </summary>
    public static async Task<IReadOnlyList<NestRecord>> FindDraftsAsync(INestRepository nests, SetReleaseGroup group,
        int limit = 50, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nests);
        ArgumentNullException.ThrowIfNull(group);
        if (!group.CanRelease)
            return Array.Empty<NestRecord>();
        var page = await nests.QueryAsync(new NestQuery
        {
            Status = NestStatus.Draft,
            Material = group.MaterialName,
            MaterialGrade = group.MaterialGrade,
            Thickness = group.Thickness,
            Limit = limit,
        }, cancellationToken);
        return page.Items;
    }

    /// <summary>The group key of a drawing that has a material and thickness.</summary>
    public static string GroupKey(DrawingLibraryRecord drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        return string.Join('|',
            SharedListNames.Key(drawing.MaterialName),
            SharedListNames.Key(drawing.MaterialGrade),
            DrawingLibraryRules.NormalizeThickness(drawing.Thickness).ToString("0.####", CultureInfo.InvariantCulture),
            drawing.Units.ToString(),
            drawing.CutType.ToString());
    }

    /// <summary>The key of the group holding every drawing without a material or thickness.</summary>
    public const string UnassignedKey = "unassigned";

    private static bool IsUnassigned(DrawingLibraryRecord drawing) =>
        SharedListNames.Key(drawing.MaterialName).Length == 0
        || !(DrawingLibraryRules.NormalizeThickness(drawing.Thickness) > 0);

    private static SetReleaseGroup BuildGroup(string key, bool unassigned, IReadOnlyList<SetReleaseItem> items)
    {
        var warnings = items.Where(i => i.Archived)
            .Select(i => $"'{i.Drawing.Name}' is archived in the library.")
            .ToList();

        var refusal = "";
        if (unassigned)
        {
            refusal = $"{Names(items)} {(items.Count == 1 ? "has" : "have")} no material or thickness. "
                + "Set them on the library drawings, then release again.";
        }
        else
        {
            // Matches LibraryDrawingInsert, which refuses a second drawing with the same name.
            var duplicate = items.GroupBy(i => i.Drawing.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                refusal = $"{duplicate.Count()} drawings in this group are named '{duplicate.Key}'. "
                    + "A nest keys drawings by name, so rename all but one in the library first.";
        }

        return new SetReleaseGroup(key, items[0].Drawing, unassigned, items, warnings, refusal);
    }

    private static string Names(IReadOnlyList<SetReleaseItem> items)
    {
        var names = items.Take(3).Select(i => $"'{i.Drawing.Name}'").ToList();
        if (items.Count > 3)
            names.Add($"{items.Count - 3} more");
        return string.Join(", ", names);
    }
}
