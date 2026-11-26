using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

public class NestReviewFormTests
{
    [Fact]
    public void ShowsTheSheetsAndNamesUnplacedPartsWithoutTouchingQuantities() => StaTestThread.Run(() =>
    {
        var item = new NestItem { Drawing = new Drawing("bracket", Rectangle()), Quantity = 3 };
        var stock = new NestPlateStock("sheet", new Size(48, 96), quantity: null, partSpacing: 0.25);
        var engine = new StubEngine(job => new NestJobResult(
            NestJobStatus.Incomplete,
            NestJobStopReason.NoPlacementFound,
            new[]
            {
                new NestJobPlateResult(0, job.Plates[0], new[]
                {
                    new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0),
                    new NestJobPlacement(job.Parts[0].Id, 1, 20, 1, 0),
                }),
            },
            new[] { new PartFulfillment(job.Parts[0].Id, 3, 2, 1) },
            new[] { new StockUsage(job.Plates[0].Id, 1, null) }));
        var result = NestPipeline.Run(engine, "Stub", new NestPipelineRequest("Stub", new[] { item }, new[] { stock }));

        using var form = new NestReviewForm(result, ColorScheme.Default);

        Assert.Equal(1, form.Strip.SheetCount);
        Assert.Equal(2, form.Strip.PartCount);
        // Two 10 x 20 parts on a 48 x 96 sheet.
        Assert.Equal(400.0 / (48 * 96), form.Strip.Utilization, 6);
        Assert.Contains("bracket: 1 not placed", form.Summary);
        Assert.Equal(0, item.Drawing.Quantity.Nested);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void RepeatedSheetsCountEveryPhysicalCopy() => StaTestThread.Run(() =>
    {
        var drawing = new Drawing("bracket", Rectangle());
        var stock = new NestPlateStock("sheet", new Size(48, 96), quantity: null, partSpacing: 0.25);
        var part = new Part(drawing, new Vector(1, 1));
        using var strip = new OpenNest.Controls.SheetStripControl();

        strip.SetSheets(new[] { new ProposedPlate(0, stock, new[] { part }) { Quantity = 3 } });

        Assert.Equal(3, strip.SheetCount);
        Assert.Equal(3, strip.PartCount);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void AnOutlinedSheetShowsItsShapeAndMeasuresUtilizationOnItsMaterial() => StaTestThread.Run(() =>
    {
        // A right triangle: half of its 96 x 48 bounding box.
        var outline = new SheetOutline(new[] { new Vector(0, 0), new Vector(96, 0), new Vector(0, 48) });
        var outlined = new NestPlateStock("remnant", outline.Size, quantity: null, partSpacing: 0.25,
            edgeSpacing: default, quadrant: 1, cost: null, outline: outline);
        var rectangle = new NestPlateStock("sheet", outline.Size, quantity: null, partSpacing: 0.25);
        var part = new Part(new Drawing("bracket", Rectangle()), new Vector(1, 1));
        using var strip = new OpenNest.Controls.SheetStripControl { Size = new System.Drawing.Size(600, 400) };

        strip.SetSheets(new[] { new ProposedPlate(0, rectangle, Array.Empty<Part>()) });
        var whole = SheetPixels(strip);
        strip.SetSheets(new[] { new ProposedPlate(0, outlined, Array.Empty<Part>()) });
        var shaped = SheetPixels(strip);

        Assert.InRange(shaped / (double)whole, 0.45, 0.55);

        strip.SetSheets(new[] { new ProposedPlate(0, outlined, new[] { part }) });
        Assert.Equal(200.0 / outline.Area, strip.Utilization, 6);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void SheetsKeepFullHeightWhenTheCaptionsAloneOverflowTheWidth() => StaTestThread.Run(() =>
    {
        var stock = new NestPlateStock("sheet", new Size(48, 96), quantity: null, partSpacing: 0.25);
        using var strip = new OpenNest.Controls.SheetStripControl { Size = new System.Drawing.Size(600, 300), ShowRemnant = true };

        strip.SetSheets(new[] { new ProposedPlate(0, stock, Array.Empty<Part>()) });
        var one = strip.PixelsPerUnit;
        // Twelve captions cannot fit 600 pixels at any scale.
        strip.SetSheets(Enumerable.Range(0, 12)
            .Select(i => new ProposedPlate(i, stock, Array.Empty<Part>()))
            .ToArray());

        Assert.True(one > 0);
        Assert.Equal(one, strip.PixelsPerUnit, 3);
        Assert.True(strip.AutoScrollMinSize.Width > strip.ClientSize.Width);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    private static int SheetPixels(OpenNest.Controls.SheetStripControl strip)
    {
        using var bitmap = new System.Drawing.Bitmap(strip.Width, strip.Height);
        strip.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, strip.Width, strip.Height));
        var fill = ColorScheme.Default.LayoutFillColor.ToArgb();
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).ToArgb() == fill)
                    count++;
        return count;
    }

    private sealed class StubEngine(Func<NestJob, NestJobResult> solve) : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null,
            CancellationToken token = default) => solve(job);
    }

    internal static CNC.Program Rectangle()
    {
        var program = new CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(10, 0);
        program.LineTo(10, 20);
        program.LineTo(0, 20);
        program.LineTo(0, 0);
        return program;
    }
}
