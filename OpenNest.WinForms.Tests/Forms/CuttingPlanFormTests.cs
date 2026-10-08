using System.Reflection;
using System.Windows.Forms;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

// Shown forms and Application.OpenForms-based guards share this collection with the fill tests.
[Collection("Fill operation lifetime")]
public class CuttingPlanFormTests
{
    [Fact]
    public void PlanThenApply_InstallsThePlanAndRedrawsTheEditorInPlateOrder() => RunSta(() =>
    {
        var (nest, view) = CreateView(Square("a", 12, 1), Square("b", 1, 1));
        using var editor = view;
        var plate = view.Plate;
        using var form = new CuttingPlanForm(view, nest, allPlates: false, Parameters());
        form.Show();
        WaitForPlan(form);

        Assert.True(Control<Button>(form, "applyButton").Enabled);
        Assert.StartsWith("Ready to apply to 1 plate", Control<TextBox>(form, "summaryBox").Text);
        var preview = Field<PlateView>(form, "preview");
        Assert.True(preview.Visible);
        Assert.NotSame(plate, preview.Plate);
        Assert.Equal(2, preview.Plate.Parts.Count);
        Assert.All(preview.Plate.Parts, part => Assert.DoesNotContain(part, plate.Parts));
        Assert.All(plate.Parts, part => Assert.False(part.HasManualLeadIns)); // The preview is detached.

        var reordered = 0;
        view.PartsReordered += (_, _) => reordered++;
        Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);

        Assert.Equal(DialogResult.OK, form.DialogResult);
        Assert.Equal(CuttingCommitStatus.Applied, form.CommitResult!.Status);
        Assert.All(plate.Parts, part => Assert.True(part.HasManualLeadIns));
        Assert.NotNull(plate.CuttingParameters);
        Assert.NotSame(form.ConfirmedParameters, plate.CuttingParameters);
        Assert.Equal(1, reordered);
        Assert.Equal(plate.Parts, view.LayoutParts.Select(layout => layout.BasePart));
        Assert.All(view.LayoutParts, layout => Assert.True(layout.IsDirty));
    });

    [Fact]
    public void BlockedPlate_KeepsApplyDisabledAndShowsTheCurrentParts() => RunSta(() =>
    {
        var locked = Square("locked", 1, 1);
        locked.LeadInsLocked = true; // A locked program is never regenerated: no lead-in blocks it.
        var (nest, view) = CreateView(locked);
        using var editor = view;
        using var form = new CuttingPlanForm(view, nest, allPlates: false, Parameters());
        form.Show();
        WaitForPlan(form);

        Assert.False(Control<Button>(form, "applyButton").Enabled);
        var summary = Control<TextBox>(form, "summaryBox").Text;
        Assert.Contains("Plate 1: blocked", summary);
        Assert.Contains("Part 1 (locked)", summary);
        // A refused plate is never previewed; the editor already shows its part numbers.
        Assert.False(Field<PlateView>(form, "preview").Visible);
        Assert.StartsWith("No preview", Control<Label>(form, "previewLabel").Text);
        Assert.Null(view.Plate.CuttingParameters);
    });

    [Fact]
    public void ClosingWhilePlanning_CancelsAndWaitsForTheWorker() => RunSta(() =>
    {
        var (nest, view) = CreateView(Square("a", 1, 1), Square("b", 12, 1));
        using var editor = view;
        var programs = view.Plate.Parts.Select(part => part.Program).ToArray();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var cancelled = false;
        var form = new CuttingPlanForm(view, nest, allPlates: false, Parameters())
        {
            // Hold the worker until the close request has been checked.
            BeforePlan = token =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                cancelled = token.IsCancellationRequested;
            },
        };
        try
        {
            form.Show();
            PumpUntil(() => form.IsPlanning, "planning to start");
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)), "The worker did not start.");

            form.Close();

            // The worker still holds the plan; the dialog stays up, cancelling, until it stops.
            Assert.False(form.IsDisposed);
            Assert.True(form.Visible);
            Assert.True(form.IsPlanning);
            Assert.False(Control<Button>(form, "cancelButton").Enabled);
            Assert.False(Control<Button>(form, "planButton").Enabled);
            release.Set();
            PumpUntil(() => form.IsDisposed, "the dialog to close after cancelling");
            Observe(form);
            Assert.True(cancelled);
            Assert.Equal(programs, view.Plate.Parts.Select(part => part.Program));
            Assert.All(view.Plate.Parts, part => Assert.False(part.HasManualLeadIns));
            Assert.Null(view.Plate.CuttingParameters);
        }
        finally
        {
            release.Set();
            form.Dispose();
        }
    });

    [Fact]
    public void Constructor_PlansWithItsOwnCopyOfTheSettings() => RunSta(() =>
    {
        var (nest, view) = CreateView(Square("a", 1, 1));
        using var editor = view;
        var parameters = Parameters();
        using var form = new CuttingPlanForm(view, nest, allPlates: false, parameters);

        ((LineLeadIn)parameters.ExternalLeadIn).Length = 5;

        Assert.NotSame(parameters, form.ConfirmedParameters);
        Assert.Equal(0.3, ((LineLeadIn)form.ConfirmedParameters.ExternalLeadIn).Length);
    });

    [Fact]
    public void ApplyAfterALiveEdit_ChangesNothingAndReplanRecovers() => RunSta(() =>
    {
        var (nest, view) = CreateView(Square("a", 1, 1), Square("b", 12, 1));
        using var editor = view;
        using var form = new CuttingPlanForm(view, nest, allPlates: false, Parameters());
        form.Show();
        WaitForPlan(form);
        var programs = view.Plate.Parts.Select(part => part.Program).ToArray();

        view.Plate.Parts[1].Offset(0, 1);
        Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);

        Assert.False(form.IsDisposed);
        Assert.Equal(DialogResult.None, form.DialogResult);
        Assert.Equal(CuttingCommitStatus.Stale, form.CommitResult!.Status);
        Assert.Contains("changed after planning", Control<TextBox>(form, "summaryBox").Text);
        Assert.False(Control<Button>(form, "applyButton").Enabled);
        Assert.Equal(programs, view.Plate.Parts.Select(part => part.Program));
        Assert.Null(view.Plate.CuttingParameters);

        Invoke(form, "PlanButton_Click", null, EventArgs.Empty);
        WaitForPlan(form);
        Assert.True(Control<Button>(form, "applyButton").Enabled);
    });

    [Fact]
    public void BusyEditor_DoesNotStartPlanningUntilItIsFree() => RunSta(() =>
    {
        var busy = true;
        var (nest, view) = CreateView(Square("a", 1, 1));
        using var editor = view;
        using var form = new CuttingPlanForm(view, nest, allPlates: false, Parameters(), () => busy);

        Invoke(form, "StartPlanning");

        Assert.False(form.IsPlanning);
        Assert.Null(form.Proposal);
        Assert.Contains("Wait for the current nesting or plate action", Control<TextBox>(form, "summaryBox").Text);
        busy = false;
        // Ending a DoEvents loop uninstalls WinForms' ambient context; planning started outside a
        // message loop must still report progress and results on the dialog's thread.
        Application.DoEvents();
        Invoke(form, "StartPlanning");
        WaitForPlan(form);
        Assert.True(Control<Button>(form, "applyButton").Enabled);
    });

    [Fact]
    public void AllPlates_SkipsEmptyPlatesAndRefusesTheBatchWhenOnePlateIsBlocked() => RunSta(() =>
    {
        var nest = new Nest();
        var first = nest.CreatePlate();
        first.Parts.Add(Square("open", 1, 1));
        var second = nest.CreatePlate();
        var locked = Square("locked", 1, 1);
        locked.LeadInsLocked = true;
        second.Parts.Add(locked);
        nest.CreatePlate(); // An empty plate, like the editor's trailing plate.
        using var view = new PlateView { Plate = first };
        using var form = new CuttingPlanForm(view, nest, allPlates: true, Parameters());
        form.Show();
        WaitForPlan(form);

        Assert.Equal("Plan Cutting — All Plates", form.Text);
        Assert.Equal(2, form.Proposal!.Plates.Count);
        var summary = Control<TextBox>(form, "summaryBox").Text;
        Assert.Contains("Plate 1: ready.", summary);
        Assert.Contains("Plate 2: blocked", summary);
        Assert.DoesNotContain("Plate 3", summary);
        Assert.False(Control<Button>(form, "applyButton").Enabled);
        Assert.All(first.Parts, part => Assert.False(part.HasManualLeadIns));
    });

    [Fact]
    public void OnePlateDialog_ExcludesTheSentinelFromTheShownTotal() => RunSta(() =>
    {
        var (nest, view) = CreateView(Square("a", 1, 1));
        nest.CreatePlate(); // The editor's trailing empty new-plate sentinel.
        using var editor = view;
        using var form = new CuttingPlanForm(view, nest, allPlates: false, Parameters());

        Assert.StartsWith("Plate 1 of 1, numbered in cutting order:", Control<Label>(form, "previewLabel").Text);
    });

    [Fact]
    public void SentinelActivePlate_IsLabeledNotNumberedBeyondTheTotal() => RunSta(() =>
    {
        var (nest, _) = CreateView(Square("a", 1, 1));
        var sentinel = nest.CreatePlate(); // trailing empty new-plate sentinel
        using var view = new PlateView { Plate = sentinel };
        using var editor = view;
        using var form = new CuttingPlanForm(view, nest, allPlates: false, Parameters());

        var text = Control<Label>(form, "previewLabel").Text;
        Assert.StartsWith("New plate (empty)", text);
        Assert.DoesNotContain("Plate 2", text);
    });

    private static (Nest Nest, PlateView View) CreateView(params Part[] parts)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(100, 100);
        foreach (var part in parts)
            plate.Parts.Add(part);
        return (nest, new PlateView { Plate = plate });
    }

    private static CuttingParameters Parameters() => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
        InternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
        ArcCircleLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
        ExternalLeadOut = new NoLeadOut(),
        InternalLeadOut = new NoLeadOut(),
        ArcCircleLeadOut = new NoLeadOut(),
    };

    private static Part Square(string name, double x, double y) =>
        new(new Drawing(name, SquareProgram()), new Vector(x, y));

    private static CNC.Program SquareProgram()
    {
        var program = new CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, 0);
        return program;
    }

    private static void WaitForPlan(CuttingPlanForm form)
    {
        PumpUntil(() => form.Proposal != null && !form.IsPlanning, "the plan");
        Observe(form);
    }

    // The planning task is already complete here; this rethrows anything its completion raised.
    private static void Observe(CuttingPlanForm form) => form.PlanningTask.GetAwaiter().GetResult();

    private static void PumpUntil(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(60);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.True(condition(), $"Timed out waiting for {what}.");
    }

    private static T Control<T>(Form form, string name) where T : Control =>
        Assert.IsType<T>(form.Controls.Find(name, true).Single());

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Invoke(CuttingPlanForm form, string method, params object?[] arguments) =>
        typeof(CuttingPlanForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, arguments);

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromMinutes(3), "The STA test did not complete.");
}
