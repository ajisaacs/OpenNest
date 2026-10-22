using System.Reflection;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

public class AutomaticCutOffFormTests
{
    [Theory]
    [InlineData(Units.Inches, "35", "in", 12)]
    [InlineData(Units.Millimeters, "889", "mm", 304.8)]
    public void InitialSpacingUsesNestUnitsAndActualSheetWidth(Units units, string spacing, string suffix, double minimumTail)
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var form = new AutomaticCutOffForm(view, units);
            Assert.Equal(spacing, Control<TextBox>(form, "spacingBox").Text);
            Assert.Contains(suffix, Control<Label>(form, "spacingLabel").Text);
            Assert.Contains(suffix, Control<Label>(form, "minimumStripLabel").Text);
            Assert.Equal(double.Parse(spacing) / 2, double.Parse(Control<TextBox>(form, "minimumStripBox").Text));
            Assert.Contains(suffix, Control<Label>(form, "minimumTailLabel").Text);
            Assert.Equal(minimumTail, double.Parse(Control<TextBox>(form, "minimumTailBox").Text));
            Assert.Contains($"36 {suffix} width", Control<Label>(form, "sheetLabel").Text);
        });
    }

    [Fact]
    public void MinimumStripFollowsSpacingUntilEditedAndPreviewUsesIt()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            var edge = new CNC.Program();
            edge.Codes.Add(new RapidMove(0, 0));
            edge.Codes.Add(new LinearMove(10, 0));
            edge.Codes.Add(new LinearMove(10, 6));
            edge.Codes.Add(new LinearMove(0, 6));
            edge.Codes.Add(new LinearMove(0, 0));
            view.Plate.Parts.Add(new Part(new Drawing("edge", edge), new Vector(24.75, 29)));
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            var spacing = Control<TextBox>(form, "spacingBox");
            var minimum = Control<TextBox>(form, "minimumStripBox");
            spacing.Text = "30";
            Assert.Equal(15, double.Parse(minimum.Text));
            minimum.Text = "20";
            spacing.Text = "40";
            Assert.Equal("20", minimum.Text);
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            var expected = AutomaticCutOffPlanner.Create(view.Plate,
                new AutomaticCutOffOptions { Spacing = 40, MinimumStripLength = 20, MinimumTailLength = 12 },
                view.CutOffSettings);
            Assert.NotEqual(40, expected.Definitions.First(c => c.Axis == CutOffAxis.Vertical).Position.X);
            Assert.Equal(expected.Definitions.Select(c => c.Position.X),
                PreviewParts(view).Select(p => ((RapidMove)p.BasePart.Program.Codes[0]).EndPoint.X));
            Assert.Contains("Strip cuts:", Control<Label>(form, "stripCutsLabel").Text);
            Assert.Contains("less than the fixed grid", Control<Label>(form, "stripCutsLabel").Text);
        });
    }

    [Fact]
    public void EqualMinimumAndSpacing_PreviewReportsMeasuredFixedGridCutting()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            Control<TextBox>(form, "minimumStripBox").Text = "35";
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            var expected = AutomaticCutOffPlanner.Create(view.Plate,
                new AutomaticCutOffOptions { Spacing = 35, MinimumStripLength = 35, MinimumTailLength = 12 },
                view.CutOffSettings);
            Assert.True(expected.StripCutLength > 0);
            Assert.Equal(expected.StripCutLength, expected.FixedGridCutLength);
            Assert.Contains($"Strip cuts: {expected.StripCutLength:0.####} in (same as the fixed grid)",
                Control<Label>(form, "stripCutsLabel").Text);
        });
    }

    [Fact]
    public void InvalidMinimumStripRefusesApplyWithoutChangingCutoffs()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            Control<TextBox>(form, "minimumStripBox").Text = "40";
            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);
            Assert.NotEqual(DialogResult.OK, form.DialogResult);
            Assert.Empty(view.Plate.CutOffs);
            Assert.False(Control<Button>(form, "applyButton").Enabled);
            Assert.Contains("Minimum strip", Control<TextBox>(form, "diagnosticsBox").Text);
        });
    }

    [Fact]
    public void SettingsMode_SeedsTheGivenOptionsAndOkReturnsThemWithoutAddingCutoffs()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            var initial = new AutomaticCutOffOptions { Spacing = 30, MinimumStripLength = 20, MinimumTailLength = 8 };
            using var form = AutomaticCutOffForm.ForSettings(view, Units.Inches, null, initial);
            Assert.Equal("&Use These Settings", Control<Button>(form, "applyButton").Text);
            Assert.Equal(30, double.Parse(Control<TextBox>(form, "spacingBox").Text));
            Assert.Equal(20, double.Parse(Control<TextBox>(form, "minimumStripBox").Text));
            Assert.Equal(8, double.Parse(Control<TextBox>(form, "minimumTailBox").Text));
            Control<TextBox>(form, "spacingBox").Text = "40";
            Assert.Equal(20, double.Parse(Control<TextBox>(form, "minimumStripBox").Text)); // Seeded minimum is kept.

            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);

            Assert.Equal(DialogResult.OK, form.DialogResult);
            Assert.Equal((40.0, (double?)20, 8.0), (form.SelectedOptions!.Spacing, form.SelectedOptions.MinimumStripLength,
                form.SelectedOptions.MinimumTailLength));
            Assert.Empty(view.Plate.CutOffs);
            Assert.DoesNotContain(view.Plate.Parts, p => p.BaseDrawing.IsCutOff);
            Assert.Empty(PreviewParts(view));
        });
    }

    [Fact]
    public void SettingsMode_InvalidInputReturnsNothing()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var form = AutomaticCutOffForm.ForSettings(view, Units.Inches, null, null);
            Control<TextBox>(form, "minimumStripBox").Text = "40";
            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);
            Assert.NotEqual(DialogResult.OK, form.DialogResult);
            Assert.Null(form.SelectedOptions);
            Assert.Contains("Minimum strip", Control<TextBox>(form, "diagnosticsBox").Text);
            Assert.Empty(view.Plate.CutOffs);
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
                Assert.False(Control<ListView>(form, "plateGrid").Visible);
                Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
                Assert.NotEmpty(PreviewParts(view));
                Assert.Empty(view.Plate.CutOffs);
                Assert.Equal(originalParts, view.Plate.Parts.ToArray());
                var expected = AutomaticCutOffPlanner.Create(view.Plate,
                    new AutomaticCutOffOptions { Spacing = 35, MinimumTailLength = 12 }, view.CutOffSettings);
                Assert.True(expected.HasSeparatedTail);
                Assert.Equal($"{expected.TailLength:0.####} in", Control<Label>(form, "tailLabel").Text);
                Assert.Contains("Ready", Control<Label>(form, "statusLabel").Text);
                Assert.Contains("Notice [SegmentedCut]", Control<TextBox>(form, "diagnosticsBox").Text);
                Assert.DoesNotContain("BLOCKING", Control<TextBox>(form, "diagnosticsBox").Text);
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
                new AutomaticCutOffOptions { Spacing = 35, MinimumStripLength = 17.5 }, view.CutOffSettings);
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
            Assert.Equal(view.Plate.CutOffs.Sum(c => c.SegmentPrograms().Count),
                view.Plate.Parts.Count(p => p.BaseDrawing.IsCutOff));
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
            Control<TextBox>(form, "minimumStripBox").Text = "35";
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
            Assert.Contains("Blocked", Control<Label>(form, "statusLabel").Text);
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
            Assert.Contains("No new cut-offs", Control<Label>(form, "statusLabel").Text);
            Assert.Equal("No issues detected.", Control<TextBox>(form, "diagnosticsBox").Text);
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

    [Fact]
    public void NestApply_PreviewsWithoutMutationAndAppliesEveryPlate()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var otherView = CreateView();
            var nest = new Nest();
            nest.Plates.Add(view.Plate);
            nest.Plates.Add(otherView.Plate);
            nest.Plates.Add(new Plate(36, 120));
            using var form = new AutomaticCutOffForm(view, nest);
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            Assert.Contains("All 3 plates", Control<Label>(form, "sheetLabel").Text);
            var grid = Control<ListView>(form, "plateGrid");
            Assert.Equal(3, grid.Items.Count);
            Assert.Equal("1 (active)", grid.Items[0].Text);
            Assert.Equal("2", grid.Items[1].Text);
            Assert.Equal("3", grid.Items[2].Text);
            Assert.Equal("Ready", grid.Items[0].SubItems[5].Text);
            Assert.Equal("Ready", grid.Items[1].SubItems[5].Text);
            Assert.Equal("No change", grid.Items[2].SubItems[5].Text);
            Assert.Equal("—", grid.Items[2].SubItems[1].Text);
            Assert.Contains("Ready", Control<Label>(form, "statusLabel").Text);
            Assert.Contains("across 2 of 3 plates", Control<Label>(form, "statusLabel").Text);
            Assert.DoesNotContain("BLOCKING", Control<TextBox>(form, "diagnosticsBox").Text);
            Assert.NotEmpty(PreviewParts(view));
            Assert.All(nest.Plates, p => Assert.Empty(p.CutOffs));

            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);

            Assert.Equal(DialogResult.OK, form.DialogResult);
            Assert.NotEmpty(view.Plate.CutOffs);
            Assert.NotEmpty(otherView.Plate.CutOffs);
            Assert.Empty(nest.Plates[2].CutOffs);
            Assert.Empty(PreviewParts(view));
            Assert.Same(nest.Plates[0], view.Plate);
        });
    }

    [Fact]
    public void NestApply_LaterConflictBlocksEntireNest()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var otherView = CreateView();
            var nest = new Nest();
            nest.Plates.Add(view.Plate);
            nest.Plates.Add(otherView.Plate);
            using var form = new AutomaticCutOffForm(view, nest);
            Control<TextBox>(form, "minimumStripBox").Text = "35";
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            otherView.Plate.CutOffs.Add(new CutOff(new Vector(35, 0), CutOffAxis.Vertical) { EndLimit = 2 });

            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);

            Assert.NotEqual(DialogResult.OK, form.DialogResult);
            Assert.Empty(view.Plate.CutOffs);
            Assert.Single(otherView.Plate.CutOffs);
            Assert.False(Control<Button>(form, "applyButton").Enabled);
            Assert.Contains("Plate 2", Control<TextBox>(form, "diagnosticsBox").Text);
            Assert.Contains("BLOCKING", Control<TextBox>(form, "diagnosticsBox").Text);
            Assert.Contains("Blocked", Control<Label>(form, "statusLabel").Text);
            var grid = Control<ListView>(form, "plateGrid");
            Assert.Equal("Ready", grid.Items[0].SubItems[5].Text);
            Assert.Equal("Blocked", grid.Items[1].SubItems[5].Text);
            Assert.Empty(PreviewParts(view));
        });
    }

    [Fact]
    public void NestApply_RechecksOperationGuardAfterPreview()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            var nest = new Nest();
            nest.Plates.Add(view.Plate);
            var busy = false;
            using var form = new AutomaticCutOffForm(view, nest, () => busy);
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            Assert.NotEmpty(PreviewParts(view));
            busy = true;

            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);

            Assert.NotEqual(DialogResult.OK, form.DialogResult);
            Assert.Empty(view.Plate.CutOffs);
            Assert.Empty(PreviewParts(view));
            Assert.False(Control<Button>(form, "applyButton").Enabled);
        });
    }

    [Fact]
    public void NestPreview_DisposeLeavesEveryPlateUntouched()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var otherView = CreateView();
            var nest = new Nest();
            nest.Plates.Add(view.Plate);
            nest.Plates.Add(otherView.Plate);
            using (var form = new AutomaticCutOffForm(view, nest))
                Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            Assert.All(nest.Plates, p => Assert.Empty(p.CutOffs));
            Assert.All(nest.Plates, p => Assert.Single(p.Parts));
            Assert.Empty(PreviewParts(view));
        });
    }

    [Fact]
    public void MinimumTailChangeClearsPreviewAndApplyUsesCurrentValue()
    {
        RunSta(() =>
        {
            using var view = CreateView();
            view.Plate.Size = new Size(36, 90);
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            Invoke(form, "PreviewButton_Click", null, EventArgs.Empty);
            Assert.Equal(2, PreviewParts(view).Count);
            Assert.Contains("TailBelowMinimum", Control<TextBox>(form, "diagnosticsBox").Text);
            Control<TextBox>(form, "minimumTailBox").Text = "0";
            Assert.Empty(PreviewParts(view));
            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);
            Assert.Equal(DialogResult.OK, form.DialogResult);
            Assert.Equal(3, view.Plate.CutOffs.Count);
        });
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("-1")]
    [InlineData("NaN")]
    public void InvalidMinimumTailBlocksApply(string text)
    {
        RunSta(() =>
        {
            using var view = CreateView();
            using var form = new AutomaticCutOffForm(view, Units.Inches);
            Control<TextBox>(form, "minimumTailBox").Text = text;
            Invoke(form, "ApplyButton_Click", null, EventArgs.Empty);
            Assert.NotEqual(DialogResult.OK, form.DialogResult);
            Assert.Empty(view.Plate.CutOffs);
            Assert.False(Control<Button>(form, "applyButton").Enabled);
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

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, Timeout.InfiniteTimeSpan, "The STA test did not complete.");
}
