using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

public class AutomaticCutOffFormTests
{
    [Theory]
    [InlineData(Units.Inches, "35", "in")]
    [InlineData(Units.Millimeters, "889", "mm")]
    public void InitialSpacingUsesNestUnitsAndActualSheetWidth(Units units, string spacing, string suffix)
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var form = new AutomaticCutOffForm(view, units);
            Assert.Equal(spacing, Control<TextBox>(form, "spacingBox").Text);
            Assert.Contains(suffix, Control<Label>(form, "spacingLabel").Text);
            Assert.Contains($"36 {suffix} width", Control<Label>(form, "sheetLabel").Text);
        });
    }

    [Fact]
    public void PreviewIsDetachedAndDisposeClearsIt()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            var originalParts = view.Plate.Parts.ToArray();
            using (var form = new AutomaticCutOffForm(view, Units.Inches))
            {
                Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
                Assert.NotEmpty(PreviewParts(view));
                Assert.Empty(view.Plate.CutOffs);
                Assert.Equal(originalParts, view.Plate.Parts.ToArray());
                Assert.Contains("36 in width", Control<Label>(form, "tailLabel").Text);
                Assert.True(Control<Button>(form, "applyButton").Enabled);
            }
            Assert.Empty(PreviewParts(view));
            Assert.Equal(originalParts, view.Plate.Parts.ToArray());
        });
    }

    [Fact]
    public void SpacingChangeAndInvalidPreviewClearOldOverlay()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            Assert.NotEmpty(PreviewParts(view));
            Control<TextBox>(form, "spacingBox").Text = "0";
            Assert.Empty(PreviewParts(view));
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            Assert.False(Control<Button>(form, "applyButton").Enabled);
            Assert.Contains("Spacing must be", Control<TextBox>(form, "diagnosticsBox").Text);
            Assert.Empty(view.Plate.CutOffs);
            Assert.Single(view.Plate.Parts);
        });
    }

    [Fact]
    public void CloseCallbackClearsPreviewWithoutApplying()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            Invoke(form, "OnFormClosed", new FormClosedEventArgs(CloseReason.UserClosing));
            Assert.Empty(PreviewParts(view));
            Assert.Empty(view.Plate.CutOffs);
            Assert.Single(view.Plate.Parts);
            Assert.Equal(DialogResult.Cancel, Control<Button>(form, "cancelButton").DialogResult);
            Assert.Same(Control<Button>(form, "cancelButton"), form.CancelButton);
        });
    }

    [Fact]
    public void ApplyRecomputesWithCurrentPartsAndSettingsAndRegeneratesOnce()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            var existing = new CutOff(new Vector(5, 0), CutOffAxis.Vertical);
            view.Plate.CutOffs.Add(existing);
            view.Plate.RegenerateCutOffs(view.CutOffSettings);
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            var preview = PreviewParts(view).Select(p => p.BasePart).ToArray();

            // Simulate stale preview inputs. Apply must not trust the displayed proposal.
            view.Plate.Parts[0].Offset(5, 0);
            view.CutOffSettings.PartClearance = 2;
            var expected = AutomaticCutOffPlanner.Create(view.Plate,
                new AutomaticCutOffOptions { Spacing = 35 }, view.CutOffSettings);
            var removals = 0;
            view.Plate.PartRemoved += (_, e) =>
            {
                if (e.Item.BaseDrawing.IsCutOff)
                    removals++;
            };
            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);

            Assert.Equal(DialogResult.OK, form.DialogResult);
            Assert.Equal(1, removals);
            Assert.Same(existing, view.Plate.CutOffs[0]);
            Assert.Equal(expected.Definitions.Select(c => c.Position.X),
                view.Plate.CutOffs.Skip(1).Select(c => c.Position.X));
            Assert.Equal(view.Plate.CutOffs.Count, view.Plate.Parts.Count(p => p.BaseDrawing.IsCutOff));
            Assert.All(preview, p => Assert.DoesNotContain(p, view.Plate.Parts));
            Assert.Empty(PreviewParts(view));
        });
    }

    [Fact]
    public void ApplyRefusesNewBlockingConflictSincePreview()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            var limited = new CutOff(new Vector(35, 0), CutOffAxis.Vertical) { EndLimit = 2 };
            view.Plate.CutOffs.Add(limited);
            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);
            Assert.NotEqual(DialogResult.OK, form.DialogResult);
            Assert.Same(limited, Assert.Single(view.Plate.CutOffs));
            Assert.Single(view.Plate.Parts);
            Assert.Empty(PreviewParts(view));
            Assert.False(Control<Button>(form, "applyButton").Enabled);
            Assert.Contains("BLOCKING", Control<TextBox>(form, "diagnosticsBox").Text);
        });
    }

    [Fact]
    public void ApplyRefusesEmptyPlate()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            view.Plate.Parts.Clear();
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);
            Assert.NotEqual(DialogResult.OK, form.DialogResult);
            Assert.Empty(view.Plate.CutOffs);
            Assert.Empty(view.Plate.Parts);
            Assert.Contains("No new usable", Control<TextBox>(form, "diagnosticsBox").Text);
            Assert.False(Control<Button>(form, "applyButton").Enabled);
        });
    }

    [Fact]
    public void FailedRegenerationRestoresDefinitionsProgramsAndPartSequence()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            var existing = new CutOff(new Vector(5, 0), CutOffAxis.Vertical);
            view.Plate.CutOffs.Add(existing);
            view.Plate.RegenerateCutOffs(view.CutOffSettings);
            var cutoffPart = view.Plate.Parts[1];
            view.Plate.Parts.Remove(cutoffPart);
            view.Plate.Parts.Insert(0, cutoffPart);
            var beforeParts = view.Plate.Parts.ToArray();
            var beforeProgram = existing.Drawing.Program;
            var beforeQuantity = beforeParts[1].BaseDrawing.Quantity.Nested;
            var failOnce = true;
            view.Plate.PartAdded += (_, e) =>
            {
                if (failOnce && e.Item.BaseDrawing.IsCutOff)
                {
                    failOnce = false;
                    throw new InvalidOperationException("Injected regeneration failure");
                }
            };
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);

            Assert.False(failOnce);
            Assert.NotEqual(DialogResult.OK, form.DialogResult);
            Assert.Same(existing, Assert.Single(view.Plate.CutOffs));
            Assert.Same(beforeProgram, existing.Drawing.Program);
            Assert.Equal(beforeParts, view.Plate.Parts.ToArray());
            Assert.Equal(beforeQuantity, beforeParts[1].BaseDrawing.Quantity.Nested);
            Assert.Empty(PreviewParts(view));
            Assert.Contains("original cut-offs were restored", Control<TextBox>(form, "diagnosticsBox").Text);
        });
    }

    private static PlateView CreateView()
    {
        var program = new CNC.Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(70, 0));
        program.Codes.Add(new LinearMove(70, 20));
        program.Codes.Add(new LinearMove(0, 20));
        program.Codes.Add(new LinearMove(0, 0));
        var plate = new Plate(36, 120) { PartSpacing = 0.5, Quantity = 2 };
        plate.Parts.Add(new Part(new Drawing("rectangle", program), new Vector(10, 8)));
        return new PlateView { Plate = plate };
    }

    private static T Control<T>(AutomaticCutOffForm form, string name) where T : Control =>
        Assert.IsType<T>(form.Controls.Find(name, true).Single());

    private static IReadOnlyList<LayoutPart> PreviewParts(PlateView view) =>
        (IReadOnlyList<LayoutPart>)typeof(PlateView)
            .GetProperty("PreviewParts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;

    private static void Invoke(AutomaticCutOffForm form, string method, params object?[] arguments) =>
        typeof(AutomaticCutOffForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, arguments);

    private static void RunSta(System.Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
