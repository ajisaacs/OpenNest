using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Engine.Tests.NestingEngines;
using OpenNest.Geometry;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

/// <summary>Cutout filling maximizes the area of material seated, not the copies: a hole takes
/// its big part instead of a crowd of tiny ones, unless the crowd really seats more.</summary>
[Collection("Cutout pipeline registry")]
public class CutoutSeatedAreaTests
{
    private const double Spacing = 0.25;

    // A frame with one 22.25 x 14.85 window, the shape of the job that exposed the copy count.
    private static NestJobPart Frame(string id = "frame", int quantity = 1)
    {
        var program = TestShapes.Rectangle(26, 18);
        program.Codes.Add(new RapidMove(2, 2));
        program.Codes.Add(new LinearMove(24.25, 2));
        program.Codes.Add(new LinearMove(24.25, 16.85));
        program.Codes.Add(new LinearMove(2, 16.85));
        program.Codes.Add(new LinearMove(2, 2));
        return JobBuilder.Part(id, program, quantity);
    }

    private static double Seated(IReadOnlyList<NestJobPlacement> poses, params NestJobPart[] inserts)
    {
        var geometry = inserts.ToDictionary(p => p.Id, p => JobPartGeometry.Read(p.Geometry));
        return CutoutSeatedArea.Of(poses, id => geometry[id]);
    }

    private static void AssertClear(NestJobPart frame, IReadOnlyList<NestJobPlacement> poses, double spacing,
        params NestJobPart[] inserts)
    {
        var geometry = inserts.ToDictionary(p => p.Id, p => JobPartGeometry.Read(p.Geometry));
        var hostGeometry = JobPartGeometry.Read(frame.Geometry);
        var host = new NestJobPlacement(frame.Id, 0, 0, 0, 0);
        for (var i = 0; i < poses.Count; i++)
        {
            Assert.True(NestLayoutCheck.Clears(hostGeometry, host, geometry[poses[i].PartId], poses[i], spacing),
                $"{poses[i]} meets the frame");
            for (var j = i + 1; j < poses.Count; j++)
                Assert.True(NestLayoutCheck.Clears(geometry[poses[i].PartId], poses[i], geometry[poses[j].PartId],
                    poses[j], spacing), $"{poses[i]} meets {poses[j]}");
        }
    }

    [Fact]
    public void MoreIgnoresRounding()
    {
        Assert.True(CutoutSeatedArea.More(229, 228));
        Assert.True(CutoutSeatedArea.More(0.001, 0));
        Assert.False(CutoutSeatedArea.More(0, 0));
        Assert.False(CutoutSeatedArea.More(229 + 1e-9, 229));
        Assert.False(CutoutSeatedArea.More(228, 229));
    }

    [Fact]
    public void BetterWeighsAreaBeforeCopies()
    {
        var geometry = new[] { JobBuilder.Rectangle("big", 10, 10, 1), JobBuilder.Rectangle("tiny", 1, 1, 9) }
            .ToDictionary(p => p.Id, p => JobPartGeometry.Read(p.Geometry));
        var big = new[] { new NestJobPlacement("big", 0, 0, 0, 0) };
        var tiny = Enumerable.Range(0, 9).Select(i => new NestJobPlacement("tiny", i, i * 2, 0, 0)).ToArray();
        Assert.True(CutoutSeatedArea.Better(big, tiny, id => geometry[id]));
        Assert.False(CutoutSeatedArea.Better(tiny, big, id => geometry[id]));
    }

    [Fact]
    public void TiesKeepTheTighterLayout()
    {
        var geometry = JobPartGeometry.Read(JobBuilder.Rectangle("a", 2, 2, 2).Geometry);
        var near = new[] { new NestJobPlacement("a", 0, 0, 0, 0), new NestJobPlacement("a", 1, 3, 0, 0) };
        var far = new[] { new NestJobPlacement("a", 0, 0, 0, 0), new NestJobPlacement("a", 1, 9, 0, 0) };
        Assert.True(CutoutSeatedArea.Better(near, far, _ => geometry));
        Assert.False(CutoutSeatedArea.Better(far, near, _ => geometry));
    }

    [Fact]
    public void AHoleTakesItsBigPartOverACrowdOfTinyOnes()
    {
        var frame = Frame();
        var big = JobBuilder.Rectangle("big", 19.12, 12, 1);
        var strip = JobBuilder.Rectangle("strip", 1.5, 3, 30);
        var tiny = JobBuilder.Rectangle("tiny", 1, 2, 30);
        var inserts = new[] { strip, tiny, big };
        var poses = CutoutRouter.Fill(frame, 0, inserts, Spacing);
        Assert.Contains(poses, p => p.PartId == "big");
        Assert.True(Seated(poses, inserts) >= 229, $"seated {Seated(poses, inserts):F1}");
        AssertClear(frame, poses, Spacing, inserts);
    }

    [Fact]
    public void FewerCopiesOfAWidePartLeaveRoomForATallerOne()
    {
        // Three bars fill the height for 258; two bars and the plate beside them seat 279.
        var frame = Frame();
        var bar = JobBuilder.Rectangle("bar", 21.5, 4, 8);
        var plate = JobBuilder.Rectangle("plate", 17.88, 6, 1);
        var poses = CutoutPackerProposal.Fill(frame, 0, new[] { bar, plate }, 0.1);
        Assert.True(Seated(poses, bar, plate) >= 279, $"seated {Seated(poses, bar, plate):F1}");
        AssertClear(frame, poses, 0.1, bar, plate);
    }

    [Fact]
    public void ManySmallCopiesStayWhenTheySeatMoreArea()
    {
        var frame = Frame();
        var lone = JobBuilder.Rectangle("lone", 6, 6, 1);
        var small = JobBuilder.Rectangle("small", 3, 2, 60);
        var poses = CutoutRouter.Fill(frame, 0, new[] { lone, small }, Spacing);
        Assert.True(poses.Count(p => p.PartId == "small") > 20, $"{poses.Count} copies");
        Assert.True(Seated(poses, lone, small) > 200, $"seated {Seated(poses, lone, small):F1}");
        AssertClear(frame, poses, Spacing, lone, small);
    }

    [Fact]
    public void LargestFirstCandidatesAreRepeatable()
    {
        var frame = Frame();
        var inserts = new[]
        {
            JobBuilder.Rectangle("a", 19.12, 12, 1), JobBuilder.Rectangle("b", 16.81, 12.38, 1),
            JobBuilder.Rectangle("c", 21.5, 4.06, 8), JobBuilder.Rectangle("d", 2, 3, 12),
            JobBuilder.Rectangle("e", 1, 1.5, 12),
        };
        var first = CutoutPackerProposal.Fill(frame, 0, inserts, Spacing);
        var second = CutoutPackerProposal.Fill(frame, 0, inserts, Spacing);
        Assert.Equal(first, second);
        AssertClear(frame, first, Spacing, inserts);
    }

    [Fact]
    public void ThePrepassReservesTheLargestPartsForEachFrame()
    {
        var job = JobBuilder.Job(
            new[]
            {
                Frame(quantity: 2), JobBuilder.Rectangle("big", 19.12, 12, 2),
                JobBuilder.Rectangle("tiny", 1, 2, 40),
            },
            new[] { JobBuilder.Stock("sheet", 60, 120, Spacing) });
        var prepass = CutoutPipelinePrepass.Prepare(job, default);
        Assert.NotNull(prepass);
        Assert.DoesNotContain(prepass.EngineJob.Parts, p => p.Id == "big");
    }
}
