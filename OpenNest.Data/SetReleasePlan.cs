using System.Globalization;
using OpenNest;

namespace OpenNest.Data;

/// <summary>One set member as released: its drawing's current record and the multiplied quantity.</summary>
public sealed class SetReleaseItem
{
    internal SetReleaseItem(DrawingSetMember member, int quantity)
    {
        Member = member;
        Drawing = member.Drawing!;
        Quantity = quantity;
    }

    public DrawingSetMember Member { get; }

    /// <summary>The drawing's current library record, as joined by the server when the set was read.</summary>
    public DrawingLibraryRecord Drawing { get; }

    /// <summary>The member quantity times the release multiplier.</summary>
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
/// How a drawing set revision splits into nests, before anything is downloaded or created.
/// Pure: it reads the members' joined drawing records only. Members are grouped by
/// material name and grade (compared by <see cref="SharedListNames.Key"/>), thickness
/// (rounded as the library stores it), units and cut type, and every quantity is
/// multiplied. Archived drawings are skipped unless included (S5). A group is refused when
/// two of its drawings share a name, because a nest keys drawings by name (S6).
/// </summary>
public sealed class SetReleasePlan
{
    private SetReleasePlan(DrawingSetRecord set, int multiplier, bool includeArchived,
        IReadOnlyList<SetReleaseGroup> groups, IReadOnlyList<DrawingSetMember> skippedArchived)
    {
        Set = set;
        Multiplier = multiplier;
        IncludeArchived = includeArchived;
        Groups = groups;
        SkippedArchived = skippedArchived;
    }

    /// <summary>The set revision being released, as read from the server.</summary>
    public DrawingSetRecord Set { get; }

    public int Multiplier { get; }

    public bool IncludeArchived { get; }

    /// <summary>
    /// Groups ordered by material, grade, thickness, units and cut type, with the
    /// unassigned group last.
    /// </summary>
    public IReadOnlyList<SetReleaseGroup> Groups { get; }

    /// <summary>Members left out because their drawing is archived and archived drawings were not included.</summary>
    public IReadOnlyList<DrawingSetMember> SkippedArchived { get; }

    /// <param name="set">A set read from the server, so every member carries its drawing's current record.</param>
    public static SetReleasePlan Build(DrawingSetRecord set, int multiplier, bool includeArchived)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (multiplier < 1 || multiplier > SetReleaseRules.MaxMultiplier)
            throw new ArgumentOutOfRangeException(nameof(multiplier), multiplier,
                $"The multiplier must be between 1 and {SetReleaseRules.MaxMultiplier}.");
        if (set.Id == Guid.Empty || set.Revision < 1)
            throw new ArgumentException("Only a set read from the server can be released.", nameof(set));
        var members = set.Members ?? throw new ArgumentException("The set has no member list.", nameof(set));
        if (members.Any(m => m?.Drawing is null || m.Drawing.Id != m.DrawingId))
            throw new ArgumentException("A set member is missing its library drawing. Reload the set.", nameof(set));

        var skipped = new List<DrawingSetMember>();
        var items = new List<(string Key, bool Unassigned, SetReleaseItem Item)>();
        foreach (var member in members)
        {
            if (member.Drawing!.Archived && !includeArchived)
            {
                skipped.Add(member);
                continue;
            }

            var unassigned = IsUnassigned(member.Drawing);
            items.Add((unassigned ? UnassignedKey : GroupKey(member.Drawing), unassigned,
                new SetReleaseItem(member, member.Quantity * multiplier)));
        }

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
        return new SetReleasePlan(set, multiplier, includeArchived, groups, skipped);
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
