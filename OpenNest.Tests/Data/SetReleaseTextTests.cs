using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

public class SetReleaseTextTests
{
    private static SetReleaseNest Nest(NestStatus status, bool deleted = false, bool joined = true) => new()
    {
        NestId = Guid.NewGuid(),
        MaterialName = "Synthetic Steel",
        MaterialGrade = "Grade A",
        Thickness = 0.25,
        Units = Units.Inches,
        CutType = CutType.Waterjet,
        Nest = joined ? new NestRecord { Name = "N1", Status = status, Deleted = deleted } : null,
    };

    [Fact]
    public void Progress_CountsCutNestsAndLeavesDeletedOnesOut()
    {
        Assert.Equal("2 of 3 cut", SetReleaseText.Progress(new SetReleaseRecord
        {
            Nests = new[] { Nest(NestStatus.HasBeenCut), Nest(NestStatus.HasBeenCut), Nest(NestStatus.Draft) },
        }));
        Assert.Equal("0 of 1 cut, 2 deleted", SetReleaseText.Progress(new SetReleaseRecord
        {
            Nests = new[] { Nest(NestStatus.ToBeCut), Nest(NestStatus.HasBeenCut, deleted: true), Nest(NestStatus.Quote, joined: false) },
        }));
        Assert.Equal("All nests deleted", SetReleaseText.Progress(new SetReleaseRecord
        {
            Nests = new[] { Nest(NestStatus.Draft, deleted: true), Nest(NestStatus.Draft, deleted: true) },
        }));
    }

    [Fact]
    public void NestState_AndGroup_DescribeOneNest()
    {
        Assert.Equal("To Be Cut", SetReleaseText.NestState(Nest(NestStatus.ToBeCut)));
        Assert.Equal("Deleted", SetReleaseText.NestState(Nest(NestStatus.ToBeCut, deleted: true)));
        Assert.Equal("Synthetic Steel Grade A 0.25 in waterjet", SetReleaseText.Group(Nest(NestStatus.Draft)));
    }

    [Fact]
    public void Origin_NamesTheSetRevisionMultiplierAndWho()
    {
        var text = SetReleaseText.Origin(new SetReleaseRecord
        {
            Sets = new[] { new SetReleaseLine { SetName = "Synthetic kit", SetRevision = 3, Quantity = 2 } },
            ReleasedBy = "Synthetic operator",
        });

        Assert.Equal("Released from set 'Synthetic kit' revision 3 ×2 by Synthetic operator", text);
    }

    [Fact]
    public void UndoRefusal_NamesEachChangedNest_OrSaysItWasUndoneBefore()
    {
        var conflict = new SetReleaseUndoConflict
        {
            Reason = SetReleaseUndoRefusal.NestsChanged,
            ChangedNests = new[]
            {
                new NestRecord { Name = "Edited nest", Revision = 5, Status = NestStatus.ToBeCut },
                new NestRecord { Name = "Removed nest", Revision = 2, Deleted = true },
            },
        };

        var text = SetReleaseText.UndoRefusal(conflict);

        Assert.Contains("- Edited nest: saved since the release (now revision 5, To Be Cut)", text);
        Assert.Contains("- Removed nest: deleted since the release", text);
        Assert.Equal("This release was already undone.",
            SetReleaseText.UndoRefusal(new SetReleaseUndoConflict { Reason = SetReleaseUndoRefusal.AlreadyUndone }));
    }

    [Fact]
    public void Origin_OfAMergeOfSeveralSets_NamesThemAll_AndAnUndo()
    {
        var nest = Guid.NewGuid();
        var release = new SetReleaseRecord
        {
            Sets = new[]
            {
                new SetReleaseLine { SetName = "Kit A", SetRevision = 3, Quantity = 2 },
                new SetReleaseLine { SetName = "Kit B", SetRevision = 1, Quantity = 1 },
                new SetReleaseLine { SetName = "Kit C", SetRevision = 5, Quantity = 4 },
            },
            Nests = new[] { new SetReleaseNest { NestId = nest, Kind = SetReleaseNestKind.Merged } },
            UndoneAt = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc),
        };

        Assert.Equal("Merged from sets 'Kit A' revision 3 ×2, 'Kit B' revision 1 ×1 and 'Kit C' revision 5 ×4 (undone)",
            SetReleaseText.Origin(release, nest));
        Assert.StartsWith("Released from sets", SetReleaseText.Origin(release));
        Assert.Equal("Undone", SetReleaseText.Progress(release));
    }

    [Fact]
    public void NestDefaults_ConvertedTo_ScalesPlateDefaultsBetweenUnits()
    {
        var inches = new NestDefaults
        {
            Units = Units.Inches,
            Size = new Size(60, 120),
            Quadrant = 3,
            PartSpacing = 0.125,
            EdgeSpacing = new Spacing(0.5, 0.25, 0.5, 0.25),
        };

        var millimeters = inches.ConvertedTo(Units.Millimeters);
        var back = millimeters.ConvertedTo(Units.Inches);

        Assert.Equal((Units.Millimeters, 1524.0, 3048.0, 3, 3.175), (millimeters.Units, millimeters.Size.Width,
            millimeters.Size.Length, millimeters.Quadrant, millimeters.PartSpacing));
        Assert.Equal((12.7, 6.35, 12.7, 6.35), (millimeters.EdgeSpacing.Left, millimeters.EdgeSpacing.Bottom,
            millimeters.EdgeSpacing.Right, millimeters.EdgeSpacing.Top));
        Assert.Equal((60.0, 120.0, 0.125), (back.Size.Width, back.Size.Length, back.PartSpacing));
        Assert.NotSame(inches, inches.ConvertedTo(Units.Inches));
    }
}
