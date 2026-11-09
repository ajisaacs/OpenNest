using System.Diagnostics;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using Xunit.Abstractions;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// A reported nest: a 7" hexagon cover with a 2.5" center hole and six bolt holes on a
/// 60 x 120 plate. Fill tiled 10 staggered rows of 18 (180) and left 2.7" above the top row;
/// lifting that row straight over the one below opens a notch at its end where one more
/// hexagon nests (181), as the user laid it out by hand.
/// </summary>
public class EndRowRefillTests
{
    private readonly ITestOutputHelper output;

    public EndRowRefillTests(ITestOutputHelper output) => this.output = output;

    private static Plate HexPlate() =>
        new(new Size(60, 120))
        {
            PartSpacing = 0.25,
            EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25),
        };

    /// <summary>The reported drawing's program, in absolute coordinates.</summary>
    private static Drawing HexCover()
    {
        var program = new Program();
        foreach (var (x, y, r) in new[]
        {
            (0.0, 0.0, 0.1875), (-1.9918584287, 1.15, 0.1875), (-3.9837168574, 0.0, 0.1875),
            (-3.9837168574, -2.3, 0.1875), (-1.9918584287, -3.45, 0.1875), (0.0, -2.3, 0.1875),
            (-0.9293584287, -1.15, 1.25),
        })
        {
            program.MoveTo(x, y);
            program.ArcTo(new Vector(x, y), new Vector(x - r, y), RotationType.CCW);
        }

        program.MoveTo(1.3206415713, -1.15);
        foreach (var (x, y) in new[]
        {
            (-0.4293584287, -4.1810889132), (-3.9293584287, -4.1810889132), (-5.6793584287, -1.15),
            (-3.9293584287, 1.8810889132), (-0.4293584287, 1.8810889132), (1.3206415713, -1.15),
        })
            program.LineTo(x, y);

        return new Drawing("Hex Cover", program);
    }

    [Fact]
    public void FillNestsOneMoreHexagonAtTheEndOfTheLiftedTopRow()
    {
        var plate = HexPlate();
        var drawing = HexCover();

        var parts = PlateFillService.FillItem("Fill", plate,
            new NestItem { Drawing = drawing, Quantity = 0 }, plate.WorkArea(), null,
            CancellationToken.None);

        Assert.Equal(181, parts.Count);
        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, parts) },
            new Dictionary<Drawing, (string, int)> { [drawing] = ("Hex Cover", 181) }));
    }

    [Fact]
    public void ImproveKeepsAGridThatHasNoRoomAtItsEnds()
    {
        var plate = new Plate(new Size(40, 40)) { PartSpacing = 0.5 };
        var drawing = new Drawing("block", Jobs.TestDrawingFactory.Rectangle(9.5, 9.5));
        var parts = new List<Part>();
        for (var x = 0; x < 4; x++)
            for (var y = 0; y < 4; y++)
                parts.Add(new Part(drawing, new Vector(x * 10, y * 10)));

        var result = EndRowRefiller.Improve(parts, plate.WorkArea(), plate.PartSpacing,
            new DefaultFillComparer(), 0, CancellationToken.None);

        Assert.Same(parts, result);
    }

    [Fact]
    public void ImproveStopsAtTheRequestedQuantity()
    {
        var plate = HexPlate();
        var drawing = HexCover();
        var full = PlateFillService.FillItem("Fill", plate,
            new NestItem { Drawing = drawing, Quantity = 0 }, plate.WorkArea(), null,
            CancellationToken.None);

        var capped = EndRowRefiller.Improve(full, plate.WorkArea(), plate.PartSpacing,
            new DefaultFillComparer(), full.Count, CancellationToken.None);
        Assert.Same(full, capped);

        // Reports what the pass costs on a full plate; not asserted.
        var stopwatch = Stopwatch.StartNew();
        EndRowRefiller.Improve(full, plate.WorkArea(), plate.PartSpacing,
            new DefaultFillComparer(), 0, CancellationToken.None);
        output.WriteLine($"Improve on {full.Count} parts: {stopwatch.ElapsedMilliseconds} ms");
    }
}
