using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.Engine;

public class NestRenestTests
{
    private static Nest NestOf(params (string Name, double Length, double Width, int Quantity)[] drawings)
    {
        var nest = new Nest("N26-0001");
        nest.PlateDefaults.Size = new Size(48, 96);
        foreach (var (name, length, width, quantity) in drawings)
        {
            var drawing = new RectangleShape { Name = name, Length = length, Width = width }.GetDrawing();
            drawing.Quantity.Required = quantity;
            nest.Drawings.Add(drawing);
        }
        return nest;
    }

    [Fact]
    public void Run_DiscardsTheOldLayout_AndNestsTheWholeDemandFromThePlateDefaults()
    {
        var nest = NestOf(("SYN-A", 4, 2, 6), ("SYN-B", 3, 3, 2));
        var old = nest.CreatePlate();
        old.Size = new Size(10, 10);
        old.Parts.Add(new Part(nest.Drawings.First()));
        nest.CreatePlate();

        var result = NestRenest.Run(nest, "Rectangles");

        Assert.True(result.Nested, result.Note);
        Assert.DoesNotContain(old, nest.Plates);
        var plate = Assert.Single(nest.Plates);
        Assert.Equal(new Size(48, 96), plate.Size);
        Assert.Equal(8, plate.Parts.Count);
        Assert.All(nest.Drawings, d => Assert.Equal((d.Quantity.Required, 0), (d.Quantity.Nested, d.Quantity.Remaining)));
        Assert.Equal((1, plate.Utilization()), (result.Sheets, result.Utilization));
        Assert.Empty(result.Unplaced);
        Assert.StartsWith("Nested on 1 sheet, ", result.Note);
    }

    [Fact]
    public void Run_DropsTheOldPlatesStockItemCodes()
    {
        var nest = NestOf(("SYN-A", 4, 2, 2));
        var old = nest.CreatePlate();
        old.Parts.Add(new Part(nest.Drawings.First()));
        old.StockItemCodes = ["DROP-1182"];

        var result = NestRenest.Run(nest, "Rectangles");

        Assert.True(result.Nested, result.Note);
        Assert.All(nest.Plates, p => Assert.Empty(p.StockItemCodes));
    }

    [Fact]
    public void Run_LeavesCopiesThatDoNotFit_AsRemainingQuantity_AndListsThem()
    {
        var nest = NestOf(("SYN-SMALL", 4, 2, 2), ("SYN-HUGE", 200, 200, 1));

        var result = NestRenest.Run(nest, "Rectangles");

        Assert.True(result.Nested, result.Note);
        Assert.Equal(2, nest.Plates.Sum(p => p.Parts.Count));
        var huge = nest.Drawings.Single(d => d.Name == "SYN-HUGE");
        Assert.Equal(1, huge.Quantity.Remaining);
        Assert.Same(huge, Assert.Single(result.Unplaced).Drawing);
        Assert.Contains(" 1 copy not placed, left to nest: ", result.Note);
        Assert.Contains("SYN-HUGE: 1 not placed", result.Note);
    }

    [Fact]
    public void Run_WhenTheLayoutIsInvalid_LeavesTheNestWithoutPlates()
    {
        var nest = NestOf(("SYN-A", 4, 2, 2));
        var old = nest.CreatePlate();
        old.Parts.Add(new Part(nest.Drawings.First()));

        var result = NestRenest.Run(nest, new OverlappingEngine(), "overlapping");

        Assert.False(result.Nested);
        Assert.Equal(NestRenest.NotKeptNote, result.Note);
        Assert.Empty(nest.Plates);
        Assert.Equal(2, nest.Drawings.First().Quantity.Remaining);
    }

    [Fact]
    public void Run_WhenCanceled_KeepsNothing()
    {
        var nest = NestOf(("SYN-A", 4, 2, 2));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        Assert.Throws<OperationCanceledException>(() => NestRenest.Run(nest, "Rectangles", token: canceled.Token));
        Assert.Empty(nest.Plates);
    }

    [Fact]
    public void CreateRequest_LeavesTheNestAlone_AndKeepCommitsTheSolvedLayoutThroughTheCaller()
    {
        var nest = NestOf(("SYN-A", 4, 2, 6), ("SYN-B", 3, 3, 0));
        Assert.Null(NestRenest.CreateRequest(NestOf(("SYN-Z", 4, 2, 0)), "Rectangles"));

        var request = NestRenest.CreateRequest(nest, "Rectangles");

        Assert.NotNull(request);
        Assert.Empty(nest.Plates);
        Assert.Equal(new[] { ("SYN-A", 6) }, request.Items.Select(i => (i.Drawing.Name, i.Quantity)));
        var solved = NestPipeline.Run(request);
        var committed = 0;
        var kept = NestRenest.Keep(solved, r =>
        {
            committed++;
            return NestPipelineCommit.ApplyToPlates(r, nest.CreatePlate);
        });
        Assert.True(kept.Nested, kept.Note);
        Assert.Equal(1, committed);
        Assert.Equal(6, nest.Plates.Sum(p => p.Parts.Count * p.Quantity));
        Assert.StartsWith("Nested on 1 sheet", kept.Note);
    }

    /// <summary>Puts every copy at the same spot, so the layout never validates.</summary>
    private sealed class OverlappingEngine : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            var builder = new NestJobResultBuilder(job);
            builder.AddSheet(job.Plates[0], Enumerable.Range(0, job.Parts[0].Quantity)
                .Select(_ => (job.Parts[0].Id, 5.0, 5.0, 0.0)).ToArray());
            return builder.Build(NestJobStopReason.Completed);
        }
    }
}
