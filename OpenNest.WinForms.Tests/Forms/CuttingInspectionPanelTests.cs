using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

[Collection("Fill operation lifetime")]
public class CuttingInspectionPanelTests
{
    [Fact]
    public void OnePlatePreviewUsesMainCanvasAndCancelRestoresLiveView() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        var view = editor.PlateView;
        var plate = view.Plate;
        var programs = plate.Parts.Select(part => part.Program).ToArray();
        view.ZoomToArea(-5, -5, 60, 60);
        var viewport = view.CaptureViewport();

        editor.OpenCuttingInspection();
        var panel = Assert.IsType<CuttingInspectionPanel>(editor.CuttingInspection);
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.True(panel.Proposal.CanApply);
        Assert.True(view.InspectionOnly);
        Assert.True(view.HasProposalDisplay);
        Assert.Same(plate, view.Plate);
        Assert.NotSame(plate, view.DisplayPlate);
        Assert.Equal(new[] { 1, 0 }, panel.Proposal.Plates[0].Result.ProposedOrder.Select(p => p.SourceOrdinal));
        Assert.Equal(new[] { "near", "far" }, view.DisplayPlate.Parts.Select(p => p.BaseDrawing.Name));
        Assert.Equal(programs, plate.Parts.Select(part => part.Program));
        Assert.All(plate.Parts, part => Assert.False(part.HasManualLeadIns));

        var key = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        Assert.True((bool)typeof(OpenNest.Controls.PlateView)
            .GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(view, [key, Keys.Escape])!);
        Assert.Null(editor.CuttingInspection);
        Assert.False(view.HasProposalDisplay);
        Assert.False(view.InspectionOnly);
        Assert.Same(plate, view.Plate);
        Assert.Equal(viewport, view.CaptureViewport());
        Assert.Equal(programs, plate.Parts.Select(part => part.Program));
    });

    [Fact]
    public void CloseDuringWorkerRetainsPanelAndInspectionLockUntilCompletion() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        editor.CuttingBeforePlan = token =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            Assert.True(token.IsCancellationRequested);
        };
        try
        {
            editor.OpenCuttingInspection();
            var panel = Assert.IsType<CuttingInspectionPanel>(editor.CuttingInspection);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
            panel.RequestClose();
            Assert.True(panel.IsPlanning);
            Assert.True(editor.PlateView.InspectionOnly);
            Assert.True(editor.IsSidePanelVisible);
            Assert.False(panel.IsDisposed);
            release.Set();
            PumpUntil(() => panel.IsDisposed);
            Assert.False(editor.PlateView.InspectionOnly);
            Assert.False(editor.PlateView.HasProposalDisplay);
            Assert.Null(editor.CuttingInspection);
        }
        finally
        {
            release.Set();
        }
    });

    [Fact]
    public void DirectEditorDisposalWaitsForRunningWorker() => RunSta(() =>
    {
        var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        editor.CuttingBeforePlan = token =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            Assert.True(token.IsCancellationRequested);
        };
        try
        {
            editor.OpenCuttingInspection();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
            var panel = Assert.IsType<CuttingInspectionPanel>(editor.CuttingInspection);
            editor.Dispose();
            Assert.False(editor.IsDisposed);
            Assert.True(editor.PlateView.InspectionOnly);
            Assert.False(panel.IsDisposed);
            release.Set();
            PumpUntil(() => editor.IsDisposed);
            Assert.True(panel.IsDisposed);
        }
        finally
        {
            release.Set();
            editor.Dispose();
        }
    });

    [Fact]
    public void DirectMdiHostDisposalWaitsForRunningWorker() => RunSta(() =>
    {
        var host = new MainForm();
        var editor = Editor();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            host.Show();
            editor.MdiParent = host;
            editor.Show();
            editor.PlateManager.LoadAt(0);
            editor.CuttingBeforePlan = token =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                Assert.True(token.IsCancellationRequested);
            };
            editor.OpenCuttingInspection();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
            var panel = Assert.IsType<CuttingInspectionPanel>(editor.CuttingInspection);
            host.Dispose();
            Assert.False(host.IsDisposed);
            Assert.False(editor.IsDisposed);
            Assert.True(editor.PlateView.InspectionOnly);
            Assert.False(panel.IsDisposed);
            release.Set();
            PumpUntil(() => host.IsDisposed);
            Assert.True(panel.IsDisposed);
        }
        finally
        {
            release.Set();
            host.Dispose();
        }
    });

    [Fact]
    public void AllPlatesNavigatesReadyAndBlockedWithoutPartialApply() => RunSta(() =>
    {
        using var editor = Editor();
        var second = editor.Nest.Plates[^1]; // Fill the managed empty sentinel; a new one is appended.
        second.Size = new OpenNest.Geometry.Size(100, 100);
        var locked = Square("locked", 1);
        var crossing = (OpenNest.CNC.Program)locked.Program.Clone();
        crossing.MoveTo(5, 5);
        Assert.True(locked.RestoreLeadInProgram(crossing, true));
        second.Parts.Add(locked);
        editor.Show();
        editor.PlateManager.LoadAt(editor.Nest.Plates.Count - 1); // Different empty sentinel.
        var original = editor.PlateView.Plate;
        OpenAll(editor);
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.Equal(2, panel.Proposal.Plates.Count);
        Assert.Equal(ScrollBars.Vertical, Field<TextBox>(panel, "resultLabel").ScrollBars);
        Assert.False(Field<Button>(panel, "applyButton").Enabled);
        Assert.True(editor.PlateView.HasProposalDisplay);
        Field<Button>(panel, "nextButton").PerformClick();
        Assert.Same(second, editor.PlateView.Plate); // Refusal shows the requested live plate, not the opening plate.
        Assert.False(editor.PlateView.HasProposalDisplay);
        Assert.Contains("no current preview", Field<Label>(panel, "previewLabel").Text);
        var grid = Field<DataGridView>(panel, "warningGrid");
        Assert.Equal(panel.Proposal.DiagnosticRows().Count, grid.Rows.Count);
        var row = grid.Rows.Cast<DataGridViewRow>().First(r => r.Tag is OpenNest.Engine.CuttingPlanning.CuttingPlanDiagnosticRow d && d.PlateIndex == 1);
        grid.ClearSelection();
        row.Selected = true; // SelectionChanged precedes CurrentCell.
        Assert.Contains("Plate 2", Field<Label>(panel, "previewLabel").Text);
        Assert.All(editor.Nest.Plates[0].Parts, p => Assert.False(p.HasManualLeadIns));
        panel.RequestClose();
        Assert.Same(original, editor.PlateView.Plate);
    });

    [Fact]
    public void NextAndPreviousShowTheSelectedReadyPlateNotTheOpeningPlate() => RunSta(() =>
    {
        using var editor = Editor();
        var first = editor.Nest.Plates[0];
        var second = editor.Nest.Plates[^1]; // Fill the managed sentinel.
        second.Size = new OpenNest.Geometry.Size(100, 100);
        second.Parts.Add(Square("other plate", 40));
        editor.Show();
        editor.PlateManager.LoadAt(0);
        OpenAll(editor);
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.Equal(2, panel.Proposal.Plates.Count);
        Assert.Same(first, editor.PlateView.Plate);
        Assert.True(editor.PlateView.HasProposalDisplay);
        Assert.Contains(editor.PlateView.DisplayPlate.Parts, part => part.BaseDrawing.Name == "near");

        Field<Button>(panel, "nextButton").PerformClick();
        Assert.Same(second, editor.PlateView.Plate);
        Assert.True(editor.PlateView.HasProposalDisplay);
        Assert.NotSame(second, editor.PlateView.DisplayPlate);
        Assert.Single(editor.PlateView.DisplayPlate.Parts);
        Assert.Equal("other plate", editor.PlateView.DisplayPlate.Parts[0].BaseDrawing.Name);
        Assert.Contains("Plate 2", Field<Label>(panel, "previewLabel").Text);

        Field<Button>(panel, "previousButton").PerformClick();
        Assert.Same(first, editor.PlateView.Plate);
        Assert.True(editor.PlateView.HasProposalDisplay);
        Assert.Contains(editor.PlateView.DisplayPlate.Parts, part => part.BaseDrawing.Name == "near");
        panel.RequestClose();
        Assert.Same(first, editor.PlateView.Plate);
    });

    [Fact]
    public void WarningAcceptanceResetsOnReplanAndSettingsOrderChange() => RunSta(() =>
    {
        using var editor = Editor();
        var plate = editor.Nest.Plates[0];
        plate.Parts.Clear();
        var program = Square("fixture", 1).Program;
        program.MoveTo(2, 2); program.LineTo(4, 2); program.LineTo(4, 4);
        program.LineTo(2, 4); program.LineTo(2, 2);
        program.MoveTo(4, 2); program.LineTo(6, 2); program.LineTo(6, 4);
        program.LineTo(4, 4); program.LineTo(4, 2);
        plate.Parts.Add(new Part(new Drawing("touching contours", program), new Vector(1, 1)));
        editor.Show();
        editor.PlateManager.LoadAt(0);
        editor.OpenCuttingInspection();
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        var accept = Field<CheckBox>(panel, "acceptWarningsCheckBox");
        Assert.True(accept.Visible);
        Assert.False(accept.Checked);
        Assert.Contains("UNVERIFIED", Field<Label>(panel, "previewLabel").Text);
        accept.Checked = true;
        Assert.True(Field<Button>(panel, "applyButton").Enabled);
        panel.StartPlanning();
        Assert.False(accept.Checked);
        Assert.Empty(Field<DataGridView>(panel, "warningGrid").Rows.Cast<DataGridViewRow>());
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        accept.Checked = true;
        Field<CheckBox>(panel, "keepOrderCheckBox").Checked = true;
        Assert.False(accept.Checked);
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.False(Field<Button>(panel, "applyButton").Enabled);
        var grid = Field<DataGridView>(panel, "warningGrid");
        Assert.NotEmpty(grid.Rows.Cast<DataGridViewRow>());
        var partRow = grid.Rows.Cast<DataGridViewRow>().First(r => r.Cells[1].Value is int);
        grid.ClearSelection();
        partRow.Selected = true;
        Assert.Contains("affected part extent", Field<Label>(panel, "previewLabel").Text);
        Field<Button>(panel, "applyButton").PerformClick();
        Assert.Null(panel.CommitResult);
        Assert.All(plate.Parts, part => Assert.False(part.HasManualLeadIns));
        accept.Checked = true;
        Field<Button>(panel, "applyButton").PerformClick();
        Assert.Equal(OpenNest.CNC.CuttingPlanning.CuttingCommitStatus.Applied, panel.CommitResult!.Status);
        Assert.All(plate.Parts, part => Assert.True(part.HasManualLeadIns));
        Assert.Null(editor.CuttingInspection);
    });

    [Fact]
    public void StaleApplyClearsPreviewAndAcceptanceWithoutChangingPrograms() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        editor.OpenCuttingInspection();
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        var plate = editor.PlateView.Plate;
        var programs = plate.Parts.Select(p => p.Program).ToArray();
        plate.Parts[0].Offset(0, 1);
        Field<Button>(panel, "applyButton").PerformClick();
        Assert.False(editor.PlateView.HasProposalDisplay);
        Assert.False(Field<Button>(panel, "applyButton").Enabled);
        Assert.False(Field<CheckBox>(panel, "acceptWarningsCheckBox").Checked);
        Assert.Contains("Replan", Field<TextBox>(panel, "resultLabel").Text);
        Assert.Equal(programs, plate.Parts.Select(p => p.Program));
        Assert.All(plate.Parts, p => Assert.False(p.HasManualLeadIns));
        panel.StartPlanning();
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.True(Field<Button>(panel, "applyButton").Enabled);
        Assert.True(editor.PlateView.HasProposalDisplay);
        panel.RequestClose();
    });

    [Fact]
    public void ReadyApplyInstallsExactPreviewAndRefreshesLiveView() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        var plate = editor.PlateView.Plate;
        var original = plate.Parts.ToArray();
        editor.OpenCuttingInspection();
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        var proposed = panel.Proposal.Plates[0].Result.ProposedOrder
            .Select(part => original[part.SourceOrdinal]).ToArray();
        Assert.True(Field<Button>(panel, "applyButton").Enabled);
        var parameters = Field<CuttingParameters>(panel, "parameters");
        var reordered = 0;
        editor.PlateView.PartsReordered += (_, _) => reordered++;
        Field<Button>(panel, "applyButton").PerformClick();
        Assert.Equal(1, reordered);
        Assert.NotSame(parameters, plate.CuttingParameters);
        foreach (var layout in editor.PlateView.LayoutParts)
        {
            if (layout.IsDirty)
                layout.Update(editor.PlateView);
            Assert.True(layout.LeadInPath?.PointCount > 0);
        }
        Assert.Null(editor.CuttingInspection);
        Assert.False(editor.PlateView.InspectionOnly);
        Assert.False(editor.PlateView.HasProposalDisplay);
        Assert.Equal(proposed, plate.Parts);
        Assert.All(plate.Parts, part => Assert.True(part.HasManualLeadIns));
        Assert.NotNull(plate.CuttingParameters);
        Assert.Equal(plate.Parts, editor.PlateView.LayoutParts.Select(layout => layout.BasePart));
    });

    [Fact]
    public void PresentationFailureCannotLeaveApplyOrWarningAcceptanceEnabled() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        editor.OpenCuttingInspection();
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.True(Field<Button>(panel, "applyButton").Enabled);
        var original = editor.PlateView.Plate.Parts.ToArray();
        panel.BeforePresent = () => throw new InvalidOperationException("synthetic presentation failure");
        panel.StartPlanning();
        PumpUntil(() => !panel.IsPlanning);
        Assert.False(Field<Button>(panel, "applyButton").Enabled);
        Assert.False(Field<CheckBox>(panel, "acceptWarningsCheckBox").Checked);
        Assert.False(editor.PlateView.HasProposalDisplay);
        Assert.Contains("Nothing can be applied", Field<TextBox>(panel, "resultLabel").Text);
        Assert.Equal(original, editor.PlateView.Plate.Parts);
        panel.RequestClose();
    });

    [Fact]
    public void CommittedPlanStillClosesWhenSavingDefaultsFails() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        editor.OpenCuttingInspection();
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Exception? error = null;
        panel.BeforePostApply = () => throw new InvalidOperationException("synthetic defaults failure");
        panel.OnPostApplyError = ex => error = ex;
        Field<Button>(panel, "applyButton").PerformClick();
        Assert.Equal(OpenNest.CNC.CuttingPlanning.CuttingCommitStatus.Applied, panel.CommitResult!.Status);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Null(editor.CuttingInspection);
        Assert.False(editor.PlateView.InspectionOnly);
        Assert.False(editor.PlateView.HasProposalDisplay);
        Assert.All(editor.PlateView.Plate.Parts, part => Assert.True(part.HasManualLeadIns));
    });

    [Fact]
    public void ConstructorKeepsItsOwnCopyOfSettings() => RunSta(() =>
    {
        using var editor = Editor();
        var parameters = Parameters();
        using var panel = new CuttingInspectionPanel(editor, parameters);
        ((LineLeadIn)parameters.ExternalLeadIn).Length = 5;
        var captured = Field<CuttingParameters>(panel, "parameters");
        Assert.NotSame(parameters, captured);
        Assert.Equal(0.3, ((LineLeadIn)captured.ExternalLeadIn).Length);
    });

    [Fact]
    public void EmptySentinelIsNotNumberedAsAnOccupiedPlate() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(editor.Nest.Plates.Count - 1);
        editor.OpenCuttingInspection();
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.Equal("New plate (empty)", Field<Label>(panel, "previewLabel").Text);
        Assert.False(editor.PlateView.HasProposalDisplay);
        panel.RequestClose();
    });

    [Fact]
    public void BusyMenuRefusesPlanningAndFreeMenuOpensDockedPanel() => RunSta(() =>
    {
        using var host = new MenuHost();
        using var editor = Editor();
        host.Show();
        editor.MdiParent = host;
        editor.Show();
        editor.Activate();
        editor.PlateManager.LoadAt(0);
        var busy = typeof(MainForm).GetField("nestingInProgress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var show = typeof(MainForm).GetMethod("ShowCuttingPlan", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        busy.SetValue(host, true);
        Assert.True(host.IsCuttingPlanBusy([editor.PlateView]));
        string? notice = null;
        host.CuttingPlanNotice = (message, caption) =>
        {
            Assert.Equal("Plan Cutting", caption);
            notice = message;
        };
        show.Invoke(host, [false]); // Exercise the real guarded menu path, not only its predicate.
        Assert.Contains("Finish or cancel", notice);
        Assert.Null(editor.CuttingInspection);
        Assert.False(editor.PlateView.InspectionOnly);
        busy.SetValue(host, false);
        Assert.False(host.IsCuttingPlanBusy([editor.PlateView]));
        // A completed DoEvents loop removes the ambient context; the panel still owns its dispatcher.
        Application.DoEvents();
        show.Invoke(host, [false]);
        var panel = Assert.IsType<CuttingInspectionPanel>(editor.CuttingInspection);
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.True(Field<Button>(panel, "applyButton").Enabled);
        panel.RequestClose();
    });

    [Fact]
    public void AutomaticCutOffsAreStagedForThePlanAndRemovedOnCancelOrUntick() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        var plate = editor.PlateView.Plate;
        var original = plate.Parts.ToArray();
        editor.OpenCuttingInspection();
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.Empty(plate.CutOffs);

        Field<CheckBox>(panel, "autoCutOffsCheckBox").Checked = true;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);

        Assert.NotNull(panel.StagedCutOffs);
        Assert.NotEmpty(plate.CutOffs);
        Assert.Equal(plate.CutOffs.Count, plate.Parts.Count(p => p.BaseDrawing.IsCutOff));
        Assert.Equal(plate.CutOffs.Count, panel.Proposal.Plates[0].Result.ProposedOrder.Count(p => p.IsCutOff));
        Assert.False(Field<FlowLayoutPanel>(panel, "existingWorkBanner").Visible); // Staged lines are not "existing".

        Field<CheckBox>(panel, "autoCutOffsCheckBox").Checked = false;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.Null(panel.StagedCutOffs);
        Assert.Empty(plate.CutOffs);
        Assert.Equal(original, plate.Parts);

        Field<CheckBox>(panel, "autoCutOffsCheckBox").Checked = true;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.NotEmpty(plate.CutOffs);
        panel.RequestClose();
        Assert.Null(editor.CuttingInspection);
        Assert.Empty(plate.CutOffs);
        Assert.Equal(original, plate.Parts);
        Assert.All(plate.Parts, part => Assert.False(part.HasManualLeadIns));
    });

    [Fact]
    public void ApplyKeepsTheStagedAutomaticCutOffsInThePlannedOrder() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        var plate = editor.PlateView.Plate;
        editor.OpenCuttingInspection();
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Field<CheckBox>(panel, "autoCutOffsCheckBox").Checked = true;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        var staged = panel.StagedCutOffs!.Definitions.ToArray();
        var planned = panel.Proposal.Plates[0].Result.ProposedOrder.Select(p => p.SourcePart).ToArray();

        Field<Button>(panel, "applyButton").PerformClick();

        Assert.Equal(OpenNest.CNC.CuttingPlanning.CuttingCommitStatus.Applied, panel.CommitResult!.Status);
        Assert.Null(editor.CuttingInspection);
        Assert.Equal(staged, plate.CutOffs);
        Assert.Equal(planned, plate.Parts);
        Assert.All(plate.Parts.Where(p => !p.BaseDrawing.IsCutOff), part => Assert.True(part.HasManualLeadIns));
    });

    [Fact]
    public void ExistingLeadInsShowTheRedBannerAndClearRecalculatesFromScratch() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        var plate = editor.PlateView.Plate;
        editor.OpenCuttingInspection();
        var first = editor.CuttingInspection!;
        PumpUntil(() => first.Proposal != null && !first.IsPlanning);
        Assert.False(Field<FlowLayoutPanel>(first, "existingWorkBanner").Visible);
        Field<Button>(first, "applyButton").PerformClick();
        Assert.All(plate.Parts, part => Assert.True(part.HasManualLeadIns));
        plate.CutOffs.Add(new CutOff(new Vector(60, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(editor.PlateView.CutOffSettings);
        var real = plate.Parts.Where(p => !p.BaseDrawing.IsCutOff).ToArray();

        editor.OpenCuttingInspection();
        var panel = editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        var banner = Field<FlowLayoutPanel>(panel, "existingWorkBanner");
        Assert.True(banner.Visible);
        Assert.Contains("ALREADY APPLIED: lead-ins on 2 parts and 1 cut-off on plate 1.",
            Field<Label>(panel, "existingWorkLabel").Text);

        string? asked = null;
        panel.ConfirmClearExisting = message => { asked = message; return false; };
        Field<Button>(panel, "clearExistingButton").PerformClick();
        Assert.Contains("cannot be undone", asked);
        Assert.Single(plate.CutOffs);
        Assert.All(real, part => Assert.True(part.HasManualLeadIns));

        panel.ConfirmClearExisting = _ => true;
        Field<Button>(panel, "clearExistingButton").PerformClick();
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);

        Assert.Empty(plate.CutOffs);
        Assert.Equal(real, plate.Parts);
        Assert.All(real, part => Assert.False(part.HasManualLeadIns));
        Assert.False(banner.Visible);
        Assert.True(panel.Proposal.CanApply);
        panel.RequestClose();
    });

    [Fact]
    public void CutOffSettingsOpensTheAutomaticCutOffDialogAndReplansWithItsChoice() => RunSta(() =>
    {
        var remembered = (System.Collections.IDictionary)typeof(CuttingInspectionPanel)
            .GetField("lastCutOffOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        try
        {
            using var editor = Editor();
            editor.Show();
            editor.PlateManager.LoadAt(0);
            var plate = editor.PlateView.Plate;
            editor.OpenCuttingInspection();
            var panel = editor.CuttingInspection!;
            PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
            Assert.Equal(35, panel.CutOffOptions.Spacing);

            panel.ShowCutOffSettings = form => DialogResult.Cancel;
            Field<Button>(panel, "cutOffSettingsButton").PerformClick();
            PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
            Assert.False(Field<CheckBox>(panel, "autoCutOffsCheckBox").Checked);
            Assert.Equal(35, panel.CutOffOptions.Spacing);
            Assert.Empty(plate.CutOffs);

            AutomaticCutOffForm? shown = null;
            panel.ShowCutOffSettings = form =>
            {
                shown = form;
                Assert.Equal(35, double.Parse(form.Controls.Find("spacingBox", true).Single().Text));
                form.Controls.Find("spacingBox", true).Single().Text = "30";
                form.Controls.Find("minimumTailBox", true).Single().Text = "10";
                typeof(AutomaticCutOffForm).GetMethod("ApplyButton_Click",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(form, [null, EventArgs.Empty]);
                Assert.Empty(plate.CutOffs); // The dialog only chooses settings.
                return form.DialogResult;
            };
            Field<Button>(panel, "cutOffSettingsButton").PerformClick();
            PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);

            Assert.NotNull(shown);
            Assert.Equal((30.0, 10.0), (panel.CutOffOptions.Spacing, panel.CutOffOptions.MinimumTailLength));
            Assert.True(Field<CheckBox>(panel, "autoCutOffsCheckBox").Checked);
            Assert.Contains("30 in strips", Field<CheckBox>(panel, "autoCutOffsCheckBox").Text);
            Assert.NotNull(panel.StagedCutOffs);
            Assert.NotEmpty(plate.CutOffs);
            Assert.Equal(plate.CutOffs.Count, panel.Proposal.Plates[0].Result.ProposedOrder.Count(p => p.IsCutOff));
            panel.RequestClose();
            Assert.Empty(plate.CutOffs);

            editor.OpenCuttingInspection();
            var reopened = editor.CuttingInspection!;
            Assert.Equal(30, reopened.CutOffOptions.Spacing); // Remembered for this session.
            PumpUntil(() => reopened.Proposal != null && !reopened.IsPlanning);
            reopened.RequestClose();
        }
        finally
        {
            remembered.Clear();
        }
    });

    private sealed class MenuHost : MainForm
    {
        protected override void OnLoad(EventArgs e) { }
    }

    private static T Field<T>(object target, string name) => Assert.IsType<T>(target.GetType()
        .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(target));

    private static void OpenAll(EditNestForm editor)
    {
        var method = typeof(EditNestForm).GetMethod("OpenCuttingInspection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        method.Invoke(editor, method.GetParameters().Length == 0 ? [] : [true]);
    }

    private static EditNestForm Editor()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new OpenNest.Geometry.Size(100, 100);
        plate.CuttingParameters = Parameters();
        plate.Parts.Add(Square("far", 12));
        plate.Parts.Add(Square("near", 1));
        return new EditNestForm(nest);
    }

    private static Part Square(string name, double x)
    {
        var program = new OpenNest.CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, 0);
        return new Part(new Drawing(name, program), new Vector(x, 1));
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

    private static void PumpUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.True(condition(), "Timed out waiting for cutting inspection.");
    }

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(45), "STA cutting inspection test timed out.");
}
