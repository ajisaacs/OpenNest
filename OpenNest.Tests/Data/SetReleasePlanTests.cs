using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class SetReleasePlanTests
{
    internal static DrawingLibraryRecord Drawing(string name, string material = "Synthetic Steel", string grade = "Grade A",
        double thickness = 0.25, Units units = Units.Inches, CutType cutType = CutType.Laser, bool archived = false) => new()
        {
            Id = Guid.NewGuid(),
            Revision = 2,
            Name = name,
            Customer = "Synthetic customer",
            MaterialName = material,
            MaterialGrade = grade,
            Thickness = thickness,
            Units = units,
            CutType = cutType,
            Archived = archived,
        };

    internal static DrawingSetRecord Set(params (DrawingLibraryRecord Drawing, int Quantity)[] members) => new()
    {
        Id = Guid.NewGuid(),
        Revision = 4,
        Name = "Synthetic kit",
        Customer = "Synthetic set customer",
        Members = members.Select(m => new DrawingSetMember { DrawingId = m.Drawing.Id, Quantity = m.Quantity, Drawing = m.Drawing }).ToList(),
    };

    [Fact]
    public void Build_GroupsByNormalizedMaterialGradeThicknessUnitsAndCutType()
    {
        var a = Drawing("SYN-A");
        var sameSpelledApart = Drawing("SYN-B", material: " synthetic  STEEL ", grade: "grade a", thickness: 0.25004);
        var otherGrade = Drawing("SYN-C", grade: "Grade B");
        var otherCut = Drawing("SYN-D", cutType: CutType.Waterjet);
        var otherThickness = Drawing("SYN-E", thickness: 0.1875);
        var otherUnits = Drawing("SYN-F", units: Units.Millimeters);

        var plan = SetReleasePlan.Build(
            Set((a, 1), (sameSpelledApart, 1), (otherGrade, 1), (otherCut, 1), (otherThickness, 1), (otherUnits, 1)), 1, false);

        Assert.Equal(5, plan.Groups.Count);
        var first = plan.Groups.Single(g => g.Items.Any(i => i.Drawing == a));
        Assert.Equal(new[] { "SYN-A", "SYN-B" }, first.Items.Select(i => i.Drawing.Name));
        Assert.Equal(("Synthetic Steel", "Grade A", 0.25, Units.Inches, CutType.Laser),
            (first.MaterialName, first.MaterialGrade, first.Thickness, first.Units, first.CutType));
        Assert.All(new[] { otherGrade, otherCut, otherThickness, otherUnits },
            d => Assert.Single(plan.Groups.Single(g => g.Items.Any(i => i.Drawing == d)).Items));
        Assert.All(plan.Groups, g => Assert.True(g.CanRelease));
        Assert.Equal(plan.Groups.Select(g => g.Key).Distinct().Count(), plan.Groups.Count);
    }

    [Fact]
    public void Build_OrdersGroupsByMaterialThenGradeThenThickness()
    {
        var plan = SetReleasePlan.Build(Set(
            (Drawing("SYN-1", material: "Synthetic Steel", thickness: 0.5), 1),
            (Drawing("SYN-2", material: "Synthetic Aluminum"), 1),
            (Drawing("SYN-3", material: "Synthetic Steel", thickness: 0.25), 1),
            (Drawing("SYN-4", material: "Synthetic Steel", grade: "Grade 0", thickness: 0.75), 1)), 1, false);

        Assert.Equal(new[] { "SYN-2", "SYN-4", "SYN-3", "SYN-1" }, plan.Groups.Select(g => g.Items[0].Drawing.Name));
    }

    [Fact]
    public void Build_MultipliesEveryQuantity()
    {
        var plan = SetReleasePlan.Build(Set((Drawing("SYN-A"), 3), (Drawing("SYN-B"), 1)), 4, false);

        var group = Assert.Single(plan.Groups);
        Assert.Equal(new[] { 12, 4 }, group.Items.Select(i => i.Quantity));
        Assert.Equal(16, group.TotalQuantity);
        Assert.Equal(4, plan.Lines.Single().Quantity);
    }

    [Fact]
    public void Build_OfSeveralSets_SumsASharedDrawing_KeepingEachSetsShare_AndGroupsAcrossSets()
    {
        var shared = Drawing("SYN-SHARED");
        var onlyFirst = Drawing("SYN-FIRST");
        var onlySecond = Drawing("SYN-SECOND", material: "Synthetic Aluminum");
        var newer = Drawing("SYN-SHARED");
        newer.Id = shared.Id;
        newer.Revision = 3;
        var first = Set((shared, 2), (onlyFirst, 1));
        var second = Set((onlySecond, 5), (newer, 1));
        second.Customer = "Another customer";

        var plan = SetReleasePlan.Build(new[] { new SetReleasePlanLine(first, 3), new SetReleasePlanLine(second, 2) }, false);

        Assert.Equal(new[] { first.Id, second.Id }, plan.Lines.Select(l => l.Set.Id));
        Assert.Equal("", plan.Customer);
        var steel = plan.Groups.Single(g => g.MaterialName == "Synthetic Steel");
        Assert.Equal(new[] { "SYN-SHARED", "SYN-FIRST" }, steel.Items.Select(i => i.Drawing.Name));
        var item = steel.Items[0];
        Assert.Equal(8, item.Quantity);
        Assert.Equal(new[] { new SetReleaseItemSource(first.Id, 6), new SetReleaseItemSource(second.Id, 2) }, item.Sources);
        Assert.Equal(3, item.Drawing.Revision);
        Assert.Equal(10, plan.Groups.Single(g => g.MaterialName == "Synthetic Aluminum").TotalQuantity);
        Assert.True(steel.CanRelease);
        Assert.Equal("Synthetic set customer", SetReleasePlan.Build(Set((shared, 1)), 1, false).Customer);
    }

    [Fact]
    public void Build_RefusesTheSameSetTwice_AndMakesAnEmptyPlanOfNothing()
    {
        var set = Set((Drawing("SYN-A"), 1));

        Assert.Throws<ArgumentException>(() => SetReleasePlan.Build(new[] { new SetReleasePlanLine(set, 1), new SetReleasePlanLine(set, 2) }, false));

        // The release dialog starts empty (File > Releases > New Release) and fills in.
        var empty = SetReleasePlan.Build(Array.Empty<SetReleasePlanLine>(), false);
        Assert.Empty(empty.Groups);
        Assert.Empty(empty.Lines);
        Assert.Empty(empty.SkippedArchived);
    }

    [Fact]
    public void Build_OfDrawingsOnTheirOwn_GroupsThemWithSetMembers_SummingAShareWithNoSet()
    {
        var shared = Drawing("SYN-SHARED");
        var loose = Drawing("SYN-LOOSE");
        var aluminum = Drawing("SYN-AL", material: "Synthetic Aluminum");
        var set = Set((shared, 2));

        var plan = SetReleasePlan.Build(new[] { new SetReleasePlanLine(set, 3) },
            new[] { new SetReleaseDrawingLine(loose, 7), new SetReleaseDrawingLine(shared, 4), new SetReleaseDrawingLine(aluminum, 100_000) },
            false);

        var steel = plan.Groups.Single(g => g.MaterialName == "Synthetic Steel");
        Assert.Equal(new[] { "SYN-SHARED", "SYN-LOOSE" }, steel.Items.Select(i => i.Drawing.Name));
        Assert.Equal(new[] { new SetReleaseItemSource(set.Id, 6), new SetReleaseItemSource(null, 4) }, steel.Items[0].Sources);
        Assert.Equal((7, loose.Id), (steel.Items[1].Quantity, steel.Items[1].Member.DrawingId));
        Assert.Equal(100_000, plan.Groups.Single(g => g.MaterialName == "Synthetic Aluminum").TotalQuantity);
        Assert.Equal(new[] { loose.Id, shared.Id, aluminum.Id }, plan.Drawings.Select(d => d.Drawing.Id));
        Assert.Equal("", plan.Customer);
    }

    [Fact]
    public void Build_OfDrawingsAlone_TakesTheirCustomer_AndTheSameRefusalsAsSets()
    {
        var archived = Drawing("SYN-OLD", archived: true);
        var unassigned = Drawing("SYN-NOMAT", material: "");
        var plan = SetReleasePlan.Build(Array.Empty<SetReleasePlanLine>(), new[]
        {
            new SetReleaseDrawingLine(Drawing("SYN-A"), 1),
            new SetReleaseDrawingLine(Drawing("SYN-A"), 2),
            new SetReleaseDrawingLine(archived, 3),
            new SetReleaseDrawingLine(unassigned, 4),
        }, includeArchived: false);

        Assert.Equal("Synthetic customer", plan.Customer);
        Assert.Contains("2 drawings in this group are named 'SYN-A'", plan.Groups[0].Refusal);
        Assert.True(plan.Groups[^1].IsUnassigned);
        Assert.Equal(new[] { (archived.Id, 3) }, plan.SkippedArchived.Select(m => (m.DrawingId, m.Quantity)));
    }

    [Fact]
    public void Build_RefusesADrawingTwice_OutOfRangeQuantities_AndADrawingNotReadFromTheLibrary()
    {
        var drawing = Drawing("SYN-A");
        var unsaved = Drawing("SYN-B");
        unsaved.Revision = 0;
        SetReleasePlan Build(params SetReleaseDrawingLine[] lines) => SetReleasePlan.Build(Array.Empty<SetReleasePlanLine>(), lines, false);

        Assert.Throws<ArgumentException>(() => Build(new SetReleaseDrawingLine(drawing, 1), new SetReleaseDrawingLine(drawing, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(new SetReleaseDrawingLine(drawing, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(new SetReleaseDrawingLine(drawing, SetReleaseRules.MaxDrawingQuantity + 1)));
        Assert.Throws<ArgumentException>(() => Build(new SetReleaseDrawingLine(unsaved, 1)));
        Assert.Empty(Build().Groups);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SetReleaseRules.MaxQuantity + 1)]
    public void Build_RejectsMultiplierOutOfRange(int multiplier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SetReleasePlan.Build(Set((Drawing("SYN-A"), 1)), multiplier, false));
    }

    [Fact]
    public void Build_SkipsArchivedDrawingsUnlessIncluded()
    {
        var active = Drawing("SYN-A");
        var archived = Drawing("SYN-B", archived: true);
        var set = Set((active, 1), (archived, 2));

        var skipping = SetReleasePlan.Build(set, 1, includeArchived: false);
        var including = SetReleasePlan.Build(set, 1, includeArchived: true);

        Assert.Equal(new[] { archived.Id }, skipping.SkippedArchived.Select(m => m.DrawingId));
        Assert.Equal(new[] { "SYN-A" }, Assert.Single(skipping.Groups).Items.Select(i => i.Drawing.Name));
        Assert.Empty(Assert.Single(skipping.Groups).Warnings);

        Assert.Empty(including.SkippedArchived);
        var group = Assert.Single(including.Groups);
        Assert.Equal(new[] { false, true }, group.Items.Select(i => i.Archived));
        Assert.Contains("'SYN-B' is archived", Assert.Single(group.Warnings));
        Assert.True(group.CanRelease);
    }

    [Fact]
    public void Build_OnlyArchivedMembers_HasNoGroups()
    {
        var plan = SetReleasePlan.Build(Set((Drawing("SYN-A", archived: true), 1)), 1, false);

        Assert.Empty(plan.Groups);
        Assert.Single(plan.SkippedArchived);
    }

    [Fact]
    public void Build_RefusesAGroupWithDuplicateDrawingNames()
    {
        var plan = SetReleasePlan.Build(Set(
            (Drawing("SYN-A"), 1), (Drawing("SYN-A"), 2), (Drawing("SYN-A", cutType: CutType.Plasma), 1), (Drawing("syn-a"), 1)), 1, false);

        var laser = plan.Groups.Single(g => g.CutType == CutType.Laser);
        Assert.False(laser.CanRelease);
        Assert.Contains("2 drawings in this group are named 'SYN-A'", laser.Refusal);
        // The same name in another group, or differently cased, nests separately.
        Assert.True(plan.Groups.Single(g => g.CutType == CutType.Plasma).CanRelease);
    }

    [Fact]
    public void Build_PutsDrawingsWithoutMaterialOrThicknessInOneRefusedGroupLast()
    {
        var plan = SetReleasePlan.Build(Set(
            (Drawing("SYN-NOMAT", material: " "), 1),
            (Drawing("SYN-A"), 1),
            (Drawing("SYN-NOTHICK", thickness: 0, units: Units.Millimeters), 1)), 1, false);

        Assert.Equal(2, plan.Groups.Count);
        var unassigned = plan.Groups[^1];
        Assert.True(unassigned.IsUnassigned);
        Assert.Equal(SetReleasePlan.UnassignedKey, unassigned.Key);
        Assert.Equal(new[] { "SYN-NOMAT", "SYN-NOTHICK" }, unassigned.Items.Select(i => i.Drawing.Name));
        Assert.False(unassigned.CanRelease);
        Assert.Contains("no material or thickness", unassigned.Refusal);
        Assert.False(plan.Groups[0].IsUnassigned);
    }

    [Fact]
    public void Build_RequiresASetReadFromTheServer()
    {
        var unsaved = Set((Drawing("SYN-A"), 1));
        unsaved.Revision = 0;
        var unjoined = Set((Drawing("SYN-A"), 1));
        unjoined.Members[0].Drawing = null;
        var mismatched = Set((Drawing("SYN-A"), 1));
        mismatched.Members[0].DrawingId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => SetReleasePlan.Build(unsaved, 1, false));
        Assert.Throws<ArgumentException>(() => SetReleasePlan.Build(unjoined, 1, false));
        Assert.Throws<ArgumentException>(() => SetReleasePlan.Build(mismatched, 1, false));
    }

    [Fact]
    public void GroupKey_IsStableAcrossSpellingAndRebuilds()
    {
        var drawing = Drawing("SYN-A");
        var respelled = Drawing("SYN-B", material: "SYNTHETIC steel", grade: " grade  a", thickness: 0.250001);

        Assert.Equal(SetReleasePlan.GroupKey(drawing), SetReleasePlan.GroupKey(respelled));
        Assert.Equal(SetReleasePlan.Build(Set((drawing, 1)), 1, false).Groups[0].Key,
            SetReleasePlan.Build(Set((drawing, 1)), 7, false).Groups[0].Key);
    }
}
