using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

// The picks read here have already completed on this STA thread; nothing blocks.
#pragma warning disable xUnit1031

/// <summary>
/// The Auto Nest progress window's top layouts: one card per layout with its sheets, and the
/// pick of the layout to use, during the run and once it has finished.
/// </summary>
public class NestProgressFormLayoutsTests
{
    private static readonly Drawing Bracket = new("bracket", Rectangle());
    private static readonly NestPlateStock Stock = new("sheet", new Size(48, 96), quantity: null, partSpacing: 0.25);

    private static Program Rectangle()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(10, 0);
        program.LineTo(10, 20);
        program.LineTo(0, 20);
        program.LineTo(0, 0);
        return program;
    }

    private static NestLayoutChoice Layout(params (double X, double Y)[] at)
    {
        var parts = at.Select(p =>
        {
            var part = new Part(Bracket) { Location = new Vector(p.X, p.Y) };
            part.UpdateBounds();
            return part;
        }).ToList();
        return new NestLayoutChoice(new[] { new ProposedPlate(0, Stock, parts) }, _ => null!);
    }

    private static NestLayoutChoice Columns() => Layout((0, 0), (0, 28), (10, 0));

    private static NestLayoutChoice Row() => Layout((0, 0), (40, 0), (86, 0));

    private static void WithForm(Action<NestProgressForm, CancellationTokenSource, Func<bool>> test) => StaTestThread.Run(() =>
    {
        using var cts = new CancellationTokenSource();
        var stopped = false;
        using var form = new NestProgressForm(cts) { AllowAccept = false, HoldOpenUntilCompleted = true };
        form.ShowPlanStrip(new ColorScheme(), () => stopped = true);
        form.Show();
        try
        {
            test(form, cts, () => stopped);
        }
        finally
        {
            form.ShowCompleted();
            form.Close();
        }
    }, TimeSpan.FromSeconds(30), "The STA test did not complete.");

    private static Button Button(Form form, string name) =>
        Assert.IsType<Button>(form.Controls.Find(name, true).Single());

    private static LayoutChoiceList List(Control parent) =>
        Descendants(parent).OfType<LayoutChoiceList>().Single();

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Descendants(c).Prepend(c));

    [Fact]
    public void EachLayoutGetsACardAndTheBestIsSelected() => WithForm((form, _, _) =>
    {
        var use = Button(form, "useLayoutButton");
        Assert.False(use.Enabled);

        var columns = Columns();
        var row = Row();
        form.AddLayout(columns, best: true);
        form.AddLayout(row, best: false);

        Assert.Equal(new[] { columns, row }, form.Layouts);
        Assert.Equal(2, Descendants(form).OfType<SheetStripControl>().Count());
        Assert.Same(columns, List(form).Selected);
        Assert.True(use.Enabled);
        Assert.Contains("76 to right edge", columns.Summary);
        Assert.Contains("28 to top", row.Summary);
    });

    [Fact]
    public void UsingALayoutDuringTheRunStopsTheSearchOnly() => WithForm((form, cts, stopped) =>
    {
        var row = Row();
        form.AddLayout(Columns(), best: true);
        form.AddLayout(row, best: false);

        List(form).Pick(row);
        Button(form, "useLayoutButton").PerformClick();

        Assert.True(stopped());
        Assert.False(cts.IsCancellationRequested);
        Assert.Same(row, form.ChosenLayout);
        Assert.True(form.LayoutPickedByUser);
        // Already chosen: the finished search does not ask again.
        Assert.True(form.ChooseLayoutAsync(Columns()).Result);
        Assert.Same(row, form.ChosenLayout);
    });

    [Fact]
    public void AFinishedSearchWaitsForThePickAmongItsLayouts() => WithForm((form, cts, stopped) =>
    {
        form.AddLayout(Columns(), best: true);
        form.AddLayout(Row(), best: false);
        var final = Columns();

        var pick = form.ChooseLayoutAsync(final);

        Assert.False(pick.IsCompleted);
        Assert.Equal("Discard", Button(form, "stopButton").Text);
        Assert.Same(final, List(form).Selected);
        Button(form, "useLayoutButton").PerformClick();
        Assert.True(pick.Result);
        Assert.Same(final, form.ChosenLayout);
        Assert.True(form.LayoutPickedByUser);
        Assert.False(stopped());
        Assert.False(cts.IsCancellationRequested);
    });

    [Fact]
    public void DiscardAfterTheSearchChoosesNothing() => WithForm((form, cts, _) =>
    {
        form.AddLayout(Columns(), best: true);
        form.AddLayout(Row(), best: false);

        var pick = form.ChooseLayoutAsync(Columns());
        Button(form, "stopButton").PerformClick();

        Assert.False(pick.Result);
        Assert.Null(form.ChosenLayout);
        Assert.False(cts.IsCancellationRequested);
    });

    // Runs the form's timers until the task completes or the wait gives up.
    private static void PumpUntil(Task task, TimeSpan max)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && waited.Elapsed < max)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static Label Status(Form form) =>
        Assert.IsType<Label>(form.Controls.Find("descriptionValue", true).Single());

    [Fact]
    public void ATimedPickUsesTheSearchResultWhenTheTimeIsUp() => WithForm((form, cts, stopped) =>
    {
        form.AddLayout(Columns(), best: true);
        form.AddLayout(Row(), best: false);
        var final = Columns();

        var pick = form.ChooseLayoutAsync(final, TimeSpan.FromMilliseconds(300));
        Assert.False(pick.IsCompleted);
        Assert.Contains("Using the best layout in 1 s", Status(form).Text);
        PumpUntil(pick, TimeSpan.FromSeconds(10));

        Assert.True(pick.IsCompleted);
        Assert.True(pick.Result);
        Assert.Same(final, form.ChosenLayout);
        // Taken for the user, not picked: the Keep/Discard review still asks.
        Assert.False(form.LayoutPickedByUser);
        Assert.False(stopped());
        Assert.False(cts.IsCancellationRequested);
    });

    [Fact]
    public void PickingACardStopsTheCountdown() => WithForm((form, _, _) =>
    {
        form.AddLayout(Columns(), best: true);
        var row = Row();
        form.AddLayout(row, best: false);

        var pick = form.ChooseLayoutAsync(Columns(), TimeSpan.FromMilliseconds(100));
        List(form).Pick(row);
        PumpUntil(pick, TimeSpan.FromMilliseconds(600));

        Assert.False(pick.IsCompleted);
        Assert.Equal("Search finished. Pick the layout to use.", Status(form).Text);
        Assert.Equal(NestProgressForm.FinishedBackColor, Status(form).Parent!.BackColor);
        Assert.Equal(NestProgressForm.FinishedForeColor, Status(form).ForeColor);
        Button(form, "useLayoutButton").PerformClick();
        Assert.True(pick.Result);
        Assert.Same(row, form.ChosenLayout);
    });

    [Fact]
    public void DiscardDuringTheCountdownChoosesNothing() => WithForm((form, _, _) =>
    {
        form.AddLayout(Columns(), best: true);
        form.AddLayout(Row(), best: false);

        var pick = form.ChooseLayoutAsync(Columns(), TimeSpan.FromMilliseconds(200));
        Button(form, "stopButton").PerformClick();
        PumpUntil(Task.Delay(400), TimeSpan.FromSeconds(2));

        Assert.False(pick.Result);
        Assert.Null(form.ChosenLayout);
    });

    [Fact]
    public void AFinishedSearchWithOneLayoutDoesNotAsk() => WithForm((form, _, _) =>
    {
        form.AddLayout(Columns(), best: true);
        var final = Columns();

        Assert.True(form.ChooseLayoutAsync(final).Result);
        Assert.Same(final, form.ChosenLayout);
        Assert.False(form.LayoutPickedByUser);
    });
}
