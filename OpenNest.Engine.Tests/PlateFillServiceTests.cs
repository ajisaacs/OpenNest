using System.Threading;
using OpenNest.Geometry;

using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.Tests.Jobs;

namespace OpenNest.Engine.Tests;

public class PlateFillServiceTests
{
    private static readonly string[] Strategies =
    [
        "Default",
        "Strip",
        "Vertical Remnant",
        "Horizontal Remnant",
    ];

    [Theory]
    [MemberData(nameof(StrategiesData))]
    public void FillItem_ResolvesBuiltInStrategy_AndRebindsToCallerDrawing(string strategy)
    {
        var plate = new Plate(new Size(60, 80));
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle(6, 4));
        var progress = new CapturingProgress();

        var parts = PlateFillService.FillItem(
            strategy,
            plate,
            new NestItem { Drawing = drawing, Quantity = 6 },
            plate.WorkArea(),
            progress,
            CancellationToken.None
        );

        Assert.NotEmpty(parts);
        Assert.All(parts, part => Assert.Same(drawing, part.BaseDrawing));
        Assert.NotEmpty(progress.Reports);
    }

    [Fact]
    public void FillItem_DoesNotMutateCallerPlate()
    {
        var plate = new Plate(new Size(60, 80));
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle(6, 4));

        var parts = PlateFillService.FillItem(
            "Default",
            plate,
            new NestItem { Drawing = drawing, Quantity = 6 },
            plate.WorkArea(),
            null,
            CancellationToken.None
        );

        Assert.NotEmpty(parts);
        Assert.Empty(plate.Parts);
    }

    [Theory]
    [MemberData(nameof(StrategiesData))]
    public void FillGroup_ResolvesBuiltInStrategy(string strategy)
    {
        var plate = new Plate(new Size(60, 80));
        var drawing = new Drawing("group", TestDrawingFactory.Rectangle(6, 4));
        var groupParts = new List<Part> { new(drawing), new(drawing) };

        var parts = PlateFillService.FillGroup(
            strategy,
            plate,
            groupParts,
            plate.WorkArea(),
            null,
            CancellationToken.None
        );

        Assert.True(parts.Count >= 2, $"Expected the group template placed at least once, got {parts.Count} parts for '{strategy}'.");
        Assert.All(parts, part => Assert.Same(drawing, part.BaseDrawing));
    }

    [Theory]
    [MemberData(nameof(StrategiesData))]
    public void PackArea_ResolvesBuiltInStrategy(string strategy)
    {
        var plate = new Plate(new Size(60, 80));
        var drawing = new Drawing("packed", TestDrawingFactory.Rectangle(6, 4));
        var items = new List<NestItem>
        {
            new() { Drawing = drawing, Quantity = 3 },
        };

        var parts = PlateFillService.PackArea(
            strategy,
            plate,
            plate.WorkArea(),
            items,
            null,
            CancellationToken.None
        );

        Assert.NotEmpty(parts);
        Assert.All(parts, part => Assert.Same(drawing, part.BaseDrawing));
    }

    [Theory]
    [MemberData(nameof(StrategiesData))]
    public void FillItem_ReturnsNoParts_WhenTokenIsAlreadyCancelled(string strategy)
    {
        // Quantity > 2 keeps the request off the qty 1-2 fast path so the cancellation
        // check inside the strategy pipeline is actually reached.
        var plate = new Plate(new Size(60, 80));
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle(6, 4));
        var progress = new CapturingProgress();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var parts = PlateFillService.FillItem(
            strategy,
            plate,
            new NestItem { Drawing = drawing, Quantity = 4 },
            plate.WorkArea(),
            progress,
            cancellation.Token
        );

        Assert.Empty(parts);
        Assert.Empty(progress.Reports);
        Assert.Empty(plate.Parts);
    }

    [Theory]
    [InlineData("Mystery Engine")]
    [InlineData("")]
    [InlineData("StockLadder")]
    public void AllOperations_RejectUnknownStrategy(string strategy)
    {
        var plate = new Plate(new Size(60, 80));
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle(6, 4));

        Assert.Throws<NotSupportedException>(() =>
            PlateFillService.FillItem(strategy, plate, new NestItem { Drawing = drawing, Quantity = 1 }, plate.WorkArea(), null, CancellationToken.None)
        );
        Assert.Throws<NotSupportedException>(() =>
            PlateFillService.FillGroup(strategy, plate, new List<Part> { new(drawing) }, plate.WorkArea(), null, CancellationToken.None)
        );
        Assert.Throws<NotSupportedException>(() =>
            PlateFillService.PackArea(strategy, plate, plate.WorkArea(), new List<NestItem> { new() { Drawing = drawing, Quantity = 1 } }, null, CancellationToken.None)
        );
    }

    [Fact]
    public void AllOperations_RejectNullStrategy()
    {
        var plate = new Plate(new Size(60, 80));
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle(6, 4));

        Assert.Throws<ArgumentNullException>(() =>
            PlateFillService.FillItem(null!, plate, new NestItem { Drawing = drawing, Quantity = 1 }, plate.WorkArea(), null, CancellationToken.None)
        );
        Assert.Throws<ArgumentNullException>(() =>
            PlateFillService.FillGroup(null!, plate, new List<Part> { new(drawing) }, plate.WorkArea(), null, CancellationToken.None)
        );
        Assert.Throws<ArgumentNullException>(() =>
            PlateFillService.PackArea(null!, plate, plate.WorkArea(), new List<NestItem> { new() { Drawing = drawing, Quantity = 1 } }, null, CancellationToken.None)
        );
    }

    [Fact]
    public void FillItem_ResolvesStrategyNames_CaseInsensitively()
    {
        // The legacy registry matched ActiveEngineName with OrdinalIgnoreCase; the service
        // keeps that tolerance for its explicit strategy parameter.
        var plate = new Plate(new Size(60, 80));
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle(6, 4));

        var parts = PlateFillService.FillItem(
            "vertical remnant",
            plate,
            new NestItem { Drawing = drawing, Quantity = 4 },
            plate.WorkArea(),
            null,
            CancellationToken.None
        );

        Assert.NotEmpty(parts);
    }

    [Fact]
    public void FillItem_RejectsNullPlate()
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle(6, 4));

        Assert.Throws<ArgumentNullException>(() =>
            PlateFillService.FillItem("Default", null!, new NestItem { Drawing = drawing, Quantity = 1 }, new Box(0, 0, 10, 10), null, CancellationToken.None)
        );
    }

    public static IEnumerable<object[]> StrategiesData() =>
        Strategies.Select(strategy => new object[] { strategy });

    private sealed class CapturingProgress : IProgress<NestProgress>
    {
        public List<NestProgress> Reports { get; } = new();

        public void Report(NestProgress value)
        {
            Reports.Add(value);
        }
    }
}
