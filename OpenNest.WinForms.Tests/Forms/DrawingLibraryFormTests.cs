using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.IO;
using OpenNest.Shapes;

namespace OpenNest.WinForms.Tests.Forms;

public class DrawingLibraryFormTests
{
    private const string Server = "http://library.test:8090";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void ListsTheNestFilteredPage_AndAddThenAddAgainRaisesTheQuantity()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-101", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "Deburr both sides");
                var nest = MatchingNest();
                var messages = new List<string>();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.View.ShowMessage = (text, _, _) => { messages.Add(text); return DialogResult.OK; };
                // Only the adds' downloads are counted here; the preview has its own test.
                form.View.LoadPreview = (_, _) => Task.FromResult<Drawing>(null!);
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                var notes = Field<TextBox>(form, "notesBox");
                var quantity = Field<NumericUpDown>(form, "quantityBox");

                PumpUntilListed(grid, "the library page");
                var query = Assert.Single(library.Queries);
                Assert.Equal(("Synthetic Steel", "Grade A", (double?)0.25), (query.MaterialName, query.MaterialGrade, query.Thickness));
                Assert.Equal("SYN-101", grid.Rows[0].Cells["nameColumn"].Value);
                Assert.Equal("Laser", grid.Rows[0].Cells["cutTypeColumn"].Value);
                Assert.Equal("Deburr both sides", notes.Text);

                quantity.Value = 3;
                Run(form.View.AddSelectedAsync());
                quantity.Value = 2;
                Run(form.View.AddSelectedAsync());

                Assert.Empty(messages);
                var drawing = Assert.Single(nest.Drawings);
                Assert.Equal(5, drawing.Quantity.Required);
                Assert.Equal((Server, record.Id, record.Revision), (drawing.Library!.ServerKey, drawing.Library.Id, drawing.Library.Revision));
                Assert.Same(drawing, Assert.Single(form.AddedDrawings));
                Assert.Equal(new[] { (record.Id, record.Revision), (record.Id, record.Revision) }, library.Downloads);
            },
            TestTimeout,
            "The drawing library dialog test did not complete.");
    }

    [Fact]
    public void SelectingADrawing_ShowsItsDetailsAndPreviewsItsGeometry()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-505", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "");
                var nest = MatchingNest();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.View.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                var preview = Field<OpenNest.Controls.ShapePreviewControl>(form, "previewBox");

                PumpUntilListed(grid, "the library page");
                Assert.Equal("SYN-505", Field<Label>(form, "detailNameLabel").Text);
                Assert.Equal("Synthetic Steel Grade A", Field<Label>(form, "materialValue").Text);
                Assert.Equal("0.25 in", Field<Label>(form, "thicknessValue").Text);

                Run(form.View.LoadSelectedPreviewAsync());
                PumpUntil(() => preview.Plate.Parts.Count > 0, "the preview");
                Assert.NotNull(Assert.Single(preview.Plate.Parts).BaseDrawing.Program);

                // A cached revision is shown again without another download.
                Run(form.View.LoadSelectedPreviewAsync());
                Assert.Single(preview.Plate.Parts);
                Assert.Single(library.Downloads, d => d == (record.Id, record.Revision));
                Assert.Empty(nest.Drawings);
            },
            TestTimeout,
            "The drawing library preview test did not complete.");
    }

    [Fact]
    public void InThisNestTab_ListsTheNestsDrawings_AndEditsTheirRequiredQuantity()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-606", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "");
                var nest = MatchingNest();
                var local = new RectangleShape { Name = "LOCAL-1", Length = 3, Width = 1 }.GetDrawing();
                local.Quantity.Required = 4;
                nest.Drawings.Add(local);
                var cutoff = new RectangleShape { Name = "CUTOFF", Length = 1, Width = 1 }.GetDrawing();
                cutoff.IsCutOff = true;
                nest.Drawings.Add(cutoff);

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.View.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                form.View.LoadPreview = (_, _) => Task.FromResult<Drawing>(null!);
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                var nestGrid = Field<DataGridView>(form, "nestGrid");
                var nestTab = Field<TabPage>(form, "nestTab");

                Assert.Equal("In This Nest (1)", nestTab.Text);
                Assert.Equal("LOCAL-1", Assert.Single(nestGrid.Rows.Cast<DataGridViewRow>()).Cells["nestNameColumn"].Value);
                Assert.Equal("Not from the library", nestGrid.Rows[0].Cells["nestLibraryColumn"].Value);

                PumpUntilListed(grid, "the library page");
                Field<NumericUpDown>(form, "quantityBox").Value = 2;
                Run(form.View.AddSelectedAsync());
                Assert.Equal("In This Nest (2)", nestTab.Text);
                Assert.Equal($"Revision {record.Revision}", nestGrid.Rows[1].Cells["nestLibraryColumn"].Value);
                Assert.Equal(2, nestGrid.Rows[1].Cells["nestRequiredColumn"].Value);

                Field<TabControl>(form, "tabControl").SelectedTab = nestTab;
                var required = nestGrid.Rows[0].Cells["nestRequiredColumn"];

                nestGrid.CurrentCell = required;
                Assert.True(nestGrid.BeginEdit(true));
                nestGrid.EditingControl!.Text = "-1";
                nestGrid.NotifyCurrentCellDirty(true);
                // Leaving the cell validates it and keeps the editor open on a bad number.
                Assert.Throws<InvalidOperationException>(() => nestGrid.CurrentCell = nestGrid.Rows[0].Cells["nestNameColumn"]);
                Assert.NotEmpty(nestGrid.Rows[0].ErrorText);
                // A commit that skips validation is refused when it is applied.
                nestGrid.EndEdit();
                Assert.Equal(4, local.Quantity.Required);
                Assert.Equal(4, required.Value);
                Assert.False(form.QuantitiesChanged);

                Assert.True(nestGrid.BeginEdit(true));
                nestGrid.EditingControl!.Text = "9";
                nestGrid.NotifyCurrentCellDirty(true);
                Assert.True(nestGrid.EndEdit());
                Assert.Equal(9, local.Quantity.Required);
                Assert.Equal(9, nestGrid.Rows[0].Cells["nestRemainingColumn"].Value);
                Assert.True(form.QuantitiesChanged);
                Assert.Contains("11 parts required", Field<Label>(form, "nestSummaryLabel").Text);
            },
            TestTimeout,
            "The In This Nest tab test did not complete.");
    }

    [Fact]
    public void MismatchWarning_IsShownBeforeTheAdd_AndCancelLeavesTheNestAlone()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                library.Add("SYN-202", "Other Steel", "Grade B", 0.1875, Units.Inches, "");
                var nest = MatchingNest();
                var answers = new Queue<DialogResult>(new[] { DialogResult.Cancel, DialogResult.OK });
                var messages = new List<string>();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.View.ShowMessage = (text, buttons, _) =>
                {
                    messages.Add(text);
                    Assert.Equal(MessageBoxButtons.OKCancel, buttons);
                    return answers.Dequeue();
                };
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                var showAll = Field<CheckBox>(form, "showAllCheckBox");

                showAll.Checked = true;
                PumpUntilListed(grid, "the unfiltered page");
                Assert.Equal(("", "", (double?)null), (library.Queries[^1].MaterialName, library.Queries[^1].MaterialGrade, library.Queries[^1].Thickness));

                Run(form.View.AddSelectedAsync());
                Assert.Empty(nest.Drawings);
                Run(form.View.AddSelectedAsync());

                Assert.Equal(2, messages.Count);
                Assert.Contains("thickness 0.1875 (nest 0.25)", messages[0]);
                Assert.Contains("cut pins are dropped", messages[0]);
                Assert.Single(nest.Drawings);
            },
            TestTimeout,
            "The drawing library mismatch test did not complete.");
    }

    [Fact]
    public void Refusal_IsShownAndNothingIsAdded()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                library.Add("SYN-303", "Synthetic Steel", "Grade A", 0.25, Units.Millimeters, "");
                var nest = MatchingNest();
                var messages = new List<string>();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.View.ShowMessage = (text, buttons, _) => { messages.Add(text); return DialogResult.OK; };
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");

                PumpUntilListed(grid, "the library page");
                Run(form.View.AddSelectedAsync());

                Assert.Contains("millimeters", Assert.Single(messages));
                Assert.Empty(nest.Drawings);
                Assert.Empty(form.AddedDrawings);
            },
            TestTimeout,
            "The drawing library refusal test did not complete.");
    }

    [Fact]
    public void EditSettings_SavesCutFirstAsANewRevision_AndTheAddAppliesIt()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-404", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "Test square");
                var nest = MatchingNest();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.View.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                form.View.EditDrawing = edit =>
                {
                    using var editor = new EditLibraryDrawingForm(edit) { LoadMaterialCatalog = Array.Empty<MaterialConfig> };
                    editor.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                    editor.Show();
                    var save = Field<Button>(editor, "saveButton");
                    Assert.False(save.Enabled);
                    Field<CheckBox>(editor, "cutFirstCheckBox").Checked = true;
                    Assert.True(save.Enabled);
                    Run(editor.SaveAsync());
                    return editor.Saved;
                };
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                PumpUntilListed(grid, "the library page");
                Assert.Equal("", grid.Rows[0].Cells["cutFirstColumn"].Value);

                Run(form.View.EditSelectedAsync());
                PumpUntil(() => Equals(grid.Rows[0].Cells["revisionColumn"].Value, record.Revision + 1), "the reloaded page");

                var update = Assert.Single(library.MetadataUpdates);
                Assert.Equal((record.Id, record.Revision), (update.Id, update.ExpectedRevision));
                Assert.True(update.Record.Settings.CutFirst);
                Assert.Equal("Test square", update.Record.Notes);
                Assert.Equal("Yes", grid.Rows[0].Cells["cutFirstColumn"].Value);

                SelectFirstRow(grid);
                Run(form.View.AddSelectedAsync());
                Assert.True(Assert.Single(nest.Drawings).CutFirst);
            },
            TestTimeout,
            "The drawing library edit test did not complete.");
    }

    [Fact]
    public void EditDrawing_SavesNewDetailsAsANewRevision_KeepingUnits()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-405", "", "", 0, Units.Inches, "Test square");
                var nest = MatchingNest();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.View.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                form.View.EditDrawing = edit =>
                {
                    using var editor = new EditLibraryDrawingForm(edit) { LoadMaterialCatalog = Array.Empty<MaterialConfig> };
                    editor.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                    editor.Show();
                    var save = Field<Button>(editor, "saveButton");
                    Assert.Equal("SYN-405", Field<TextBox>(editor, "nameBox").Text);
                    Assert.Equal("in", Field<Label>(editor, "unitsLabel").Text);
                    Assert.False(save.Enabled);
                    Field<TextBox>(editor, "nameBox").Text = "SYN-405B";
                    Field<ComboBox>(editor, "materialBox").Text = "Synthetic Stainless";
                    Field<ComboBox>(editor, "gradeBox").Text = "304";
                    Field<OpenNest.Controls.ThicknessComboBox>(editor, "thicknessBox").Value = 0.1875;
                    Field<ComboBox>(editor, "cutTypeBox").SelectedItem = "Waterjet";
                    Assert.True(save.Enabled);
                    Field<TextBox>(editor, "nameBox").Text = " ";
                    Assert.False(save.Enabled);
                    Field<TextBox>(editor, "nameBox").Text = "SYN-405B";
                    Run(editor.SaveAsync());
                    return editor.Saved;
                };
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                PumpUntilListed(grid, "the library page");

                Run(form.View.EditSelectedAsync());
                PumpUntil(() => Equals(grid.Rows[0].Cells["nameColumn"].Value, "SYN-405B"), "the reloaded page");

                var update = Assert.Single(library.MetadataUpdates);
                Assert.Equal((record.Id, record.Revision), (update.Id, update.ExpectedRevision));
                Assert.Equal(("SYN-405B", "Synthetic Stainless", "304", 0.1875, CutType.Waterjet, Units.Inches),
                    (update.Record.Name, update.Record.MaterialName, update.Record.MaterialGrade, update.Record.Thickness,
                        update.Record.CutType, update.Record.Units));
                Assert.Equal("Waterjet", grid.Rows[0].Cells["cutTypeColumn"].Value);
            },
            TestTimeout,
            "The drawing library details edit test did not complete.");
    }

    [Fact]
    public void EditDrawing_WarnsBeforeDuplicatingAnotherDrawing_AndCancelSavesNothing()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-606", "", "", 0, Units.Millimeters, "No material yet");
                var twin = library.Add("SYN-707", "Synthetic Steel", "Grade A", 6.35, Units.Millimeters, "");
                var warnings = new List<string>();
                var answer = DialogResult.Cancel;

                using var editor = new EditLibraryDrawingForm(new DrawingLibraryEditSession(library, record, "Synthetic operator"))
                {
                    LoadMaterialCatalog = Array.Empty<MaterialConfig>,
                };
                editor.ShowMessage = (text, _, _) => { warnings.Add(text); return answer; };
                editor.Show();
                Field<TextBox>(editor, "nameBox").Text = "SYN-707";
                Field<ComboBox>(editor, "materialBox").Text = "Synthetic Steel";
                Field<ComboBox>(editor, "gradeBox").Text = "Grade A";
                Field<OpenNest.Controls.ThicknessComboBox>(editor, "thicknessBox").Value = 6.35;

                // The new details duplicate SYN-707: cancelling the warning saves nothing.
                Run(editor.SaveAsync());
                Assert.Contains("'SYN-707'", Assert.Single(warnings));
                Assert.Empty(library.MetadataUpdates);
                Assert.Null(editor.Saved);

                Field<TextBox>(editor, "nameBox").Text = "SYN-606";
                Field<ComboBox>(editor, "cutTypeBox").SelectedItem = "Plasma";
                answer = DialogResult.OK;
                Run(editor.SaveAsync());

                Assert.Single(warnings);
                var update = Assert.Single(library.MetadataUpdates);
                Assert.Equal(("SYN-606", "Synthetic Steel", "Grade A", 6.35, Units.Millimeters, CutType.Plasma),
                    (update.Record.Name, update.Record.MaterialName, update.Record.MaterialGrade, update.Record.Thickness,
                        update.Record.Units, update.Record.CutType));
                Assert.NotEqual(twin.Id, editor.Saved!.Id);
            },
            TestTimeout,
            "The edit drawing duplicate warning test did not complete.");
    }

    [Fact]
    public void EditDrawing_OffersTheLibrarysAndCatalogsValues_WithoutChangingTheEdit()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-801", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "");
                library.Add("SYN-802", "synthetic  steel", "Grade B", 0.1875, Units.Inches, "");
                library.Add("SYN-803", "Synthetic Brass", "", 3, Units.Millimeters, "");

                using var editor = new EditLibraryDrawingForm(new DrawingLibraryEditSession(library, record, "Synthetic operator"))
                {
                    LoadMaterialCatalog = () => new[] { new MaterialConfig { Name = "Synthetic Steel", Grade = "Grade C" } },
                };
                editor.Show();
                Run(editor.LoadChoicesAsync());

                Assert.Contains("Synthetic customer", Field<ComboBox>(editor, "customerBox").Items.Cast<string>());
                var materials = Field<ComboBox>(editor, "materialBox").Items.Cast<string>().ToList();
                Assert.Contains("Synthetic Brass", materials);
                Assert.Single(materials, m => SharedListNames.Key(m) == "SYNTHETIC STEEL");
                Assert.Equal(new[] { "Grade A", "Grade B", "Grade C" }, Field<ComboBox>(editor, "gradeBox").Items.Cast<string>());
                var thickness = Field<OpenNest.Controls.ThicknessComboBox>(editor, "thicknessBox");
                Assert.Equal(StandardThicknesses.Format(0.25, Units.Inches), thickness.Items[0]);

                // Without a grade the material's other thicknesses are listed first too; millimeter ones never are.
                Field<ComboBox>(editor, "gradeBox").Text = "";
                Assert.Equal(new object[] { StandardThicknesses.Format(0.1875, Units.Inches), StandardThicknesses.Format(0.25, Units.Inches) },
                    thickness.Items.Cast<object>().Take(2));
                Field<ComboBox>(editor, "gradeBox").Text = "Grade A";
                Assert.False(Field<Button>(editor, "saveButton").Enabled);
                Assert.Equal(record.Thickness, editor.ReadDetails().Thickness);
            },
            TestTimeout,
            "The edit drawing choices test did not complete.");
    }

    [Fact]
    public void EditDrawing_ColorCanBeMadeAutomatic_AndResetToTheStoredColor()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-811", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "");
                var custom = System.Drawing.Color.FromArgb(255, 40, 50, 60).ToArgb();
                record.Settings = new DrawingNestingSettings { ColorArgb = custom };

                using var editor = new EditLibraryDrawingForm(new DrawingLibraryEditSession(library, record, "Synthetic operator"))
                {
                    LoadMaterialCatalog = Array.Empty<MaterialConfig>,
                };
                editor.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                editor.Show();
                var save = Field<Button>(editor, "saveButton");
                var automatic = Field<Button>(editor, "automaticColorButton");
                var reset = Field<Button>(editor, "resetColorButton");
                Assert.Equal((true, false), (automatic.Enabled, reset.Enabled));
                Assert.StartsWith("Custom: #28323C", Field<Label>(editor, "colorModeLabel").Text);

                automatic.PerformClick();
                Assert.Equal(0, editor.ReadSettings().ColorArgb);
                Assert.Equal((false, true, true), (automatic.Enabled, reset.Enabled, save.Enabled));
                Assert.StartsWith("Automatic", Field<Label>(editor, "colorModeLabel").Text);

                reset.PerformClick();
                Assert.Equal(custom, editor.ReadSettings().ColorArgb);
                Assert.Equal((true, false, false), (automatic.Enabled, reset.Enabled, save.Enabled));

                automatic.PerformClick();
                Run(editor.SaveAsync());
                Assert.Equal(0, Assert.Single(library.MetadataUpdates).Record.Settings.ColorArgb);
            },
            TestTimeout,
            "The edit drawing color test did not complete.");
    }

    [Fact]
    public void EditDrawing_RotationDialAndAngleBoxesStayInStep()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-821", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "");
                record.Settings = new DrawingNestingSettings
                {
                    StartAngle = 0,
                    EndAngle = System.Math.PI / 2,
                    StepAngle = OpenNest.Math.Angle.ToRadians(15),
                };

                using var editor = new EditLibraryDrawingForm(new DrawingLibraryEditSession(library, record, "Synthetic operator"))
                {
                    LoadMaterialCatalog = Array.Empty<MaterialConfig>,
                };
                editor.Show();
                var dial = Field<OpenNest.Controls.RotationRangeDial>(editor, "rotationDial");
                var summary = Field<Label>(editor, "rotationSummaryLabel");
                Assert.Equal((0d, 90d, 15d), (dial.StartAngle, dial.EndAngle, dial.StepAngle));
                Assert.Contains("tries 7 angles", summary.Text);
                Assert.False(Field<Button>(editor, "saveButton").Enabled);

                Field<NumericUpDown>(editor, "endBox").Value = 120;
                Assert.Equal(120d, dial.EndAngle);

                dial.ActiveHandle = OpenNest.Controls.DialHandle.Start;
                dial.MoveActive(15);
                Assert.Equal(15m, Field<NumericUpDown>(editor, "startBox").Value);
                Assert.Equal(OpenNest.Math.Angle.ToRadians(15), editor.ReadSettings().StartAngle, 12);
                Assert.Contains("15° to 120° every 15°: tries 8 angles", summary.Text);

                Field<CheckBox>(editor, "allow180CheckBox").Checked = true;
                Assert.True(dial.Allow180Equivalent);
                Assert.Contains("tries 16 angles", summary.Text);
                Assert.True(Field<Button>(editor, "saveButton").Enabled);
            },
            TestTimeout,
            "The edit drawing rotation dial test did not complete.");
    }

    [Theory]
    [InlineData(0, 0, 0, false, "Automatic")]
    [InlineData(90, 0, 15, false, "end is before the start")]
    [InlineData(45, 45, 360, false, "Fixed at 45°: tries 1 angle.")]
    [InlineData(0, 90, 45, true, "0° to 90° every 45°, and 180° from each: tries 6 angles.")]
    public void DescribeRotation_SaysWhatTheEnginesTry(double start, double end, double step, bool allow180, string expected) =>
        Assert.Contains(expected, EditLibraryDrawingForm.DescribeRotation(start, end, step, allow180));

    [Fact]
    public void WithoutANest_ListsEveryDrawing_HasNoAdd_AndEditsOnDoubleClick()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-909", "", "", 0, Units.Inches, "");
                var edited = new List<DrawingLibraryEditSession>();

                using var form = new DrawingLibraryForm(library, Server, null);
                form.View.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                form.View.LoadPreview = (_, _) => Task.FromResult<Drawing>(null!);
                form.View.EditDrawing = edit =>
                {
                    edited.Add(edit);
                    var save = edit.SaveAsync(edit.Details with { MaterialName = "Synthetic Steel", Thickness = 0.25 },
                        edit.Listed.Notes, edit.Listed.Settings ?? new DrawingNestingSettings());
                    return Run(save);
                };
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                PumpUntilListed(grid, "the library page");

                var query = Assert.Single(library.Queries);
                Assert.Equal(("", "", (double?)null), (query.MaterialName, query.MaterialGrade, query.Thickness));
                Assert.DoesNotContain(Field<TabPage>(form, "nestTab"), Field<TabControl>(form, "tabControl").TabPages.Cast<TabPage>());
                Assert.False(Field<Button>(form, "addButton").Visible);
                Assert.Equal("Edit Drawing...", Field<Button>(form, "editButton").Text);
                Run(form.View.AddSelectedAsync());
                Assert.Empty(library.Downloads);

                typeof(DataGridView).GetMethod("OnCellDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(grid, new object[] { new DataGridViewCellEventArgs(0, 0) });
                PumpUntil(() => Equals(grid.Rows[0].Cells["revisionColumn"].Value, record.Revision + 1), "the reloaded page");

                Assert.Equal(record.Id, Assert.Single(edited).Listed.Id);
                Assert.Equal("Synthetic Steel", grid.Rows[0].Cells["materialColumn"].Value);
            },
            TestTimeout,
            "The library-only dialog test did not complete.");
    }

    private static Nest MatchingNest() => new("Synthetic job")
    {
        Units = Units.Inches,
        Thickness = 0.25,
        Material = new Material("Synthetic Steel", "Grade A"),
    };

    // A faulted task rethrows here, so a failed step reports its own error instead of a later timeout.
    private static void Run(Task task)
    {
        PumpUntil(() => task.IsCompleted, "the add to finish");
        task.GetAwaiter().GetResult();
    }

    private static T Run<T>(Task<T> task)
    {
        Run((Task)task);
        return task.Result;
    }

    /// <summary>
    /// Waits for the one-row page, then makes that row current. The grid only picks a current
    /// row on its own when the row is already laid out as displayed, which a CI desktop does not
    /// guarantee; without one every Add/Edit is a silent no-op.
    /// </summary>
    private static void PumpUntilListed(DataGridView grid, string what)
    {
        PumpUntil(() => grid.Rows.Count == 1, what);
        SelectFirstRow(grid);
    }

    private static void SelectFirstRow(DataGridView grid) => grid.CurrentCell = grid.Rows[0].Cells["nameColumn"];

    private static void PumpUntil(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        Assert.True(condition(), $"Timed out waiting for {what}.");
    }

    private static T Field<T>(object owner, string name)
    {
        if (owner is DrawingLibraryForm dialog)
            owner = dialog.View;
        return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    }

    /// <summary>Serves every record (ignoring filters) and real packages by revision.</summary>
    private sealed class FakeLibrary : IDrawingLibraryStore
    {
        private readonly List<DrawingLibraryRecord> _records = new();
        private readonly Dictionary<(Guid, int), byte[]> _packages = new();

        public List<DrawingLibraryQuery> Queries { get; } = new();

        public List<(Guid, int)> Downloads { get; } = new();

        public DrawingLibraryRecord Add(string name, string material, string grade, double thickness, Units units, string notes)
        {
            var drawing = new RectangleShape { Name = name, Length = 4, Width = 2 }.GetDrawing();
            var package = DrawingPackage.Write(drawing, units);
            var record = new DrawingLibraryRecord
            {
                Id = Guid.NewGuid(),
                Revision = 2,
                Name = name,
                Customer = "Synthetic customer",
                MaterialName = material,
                MaterialGrade = grade,
                Thickness = thickness,
                Units = units,
                CutType = CutType.Laser,
                Notes = notes,
                PackageSha256 = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant(),
                SavedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            };
            _records.Add(record);
            _packages[(record.Id, record.Revision)] = package;
            return record;
        }

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new DrawingLibraryPage
            {
                Items = _records.ToArray(),
                Total = _records.Count,
                Offset = query.Offset,
                Limit = query.Limit,
            });
        }

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default)
        {
            Downloads.Add((id, revision));
            return Task.FromResult(_packages.TryGetValue((id, revision), out var package) ? package : null);
        }

        public List<(Guid Id, int ExpectedRevision, DrawingLibraryRecord Record)> MetadataUpdates { get; } = new();

        /// <summary>Replaces the listed record with the next revision, keeping its package.</summary>
        public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
            CancellationToken cancellationToken = default)
        {
            MetadataUpdates.Add((id, expectedRevision, record));
            var index = _records.FindIndex(r => r.Id == id);
            var current = _records[index];
            if (current.Revision != expectedRevision)
                throw new DrawingLibraryConflictException(current);
            var saved = new DrawingLibraryRecord
            {
                Id = id,
                Revision = current.Revision + 1,
                Name = record.Name,
                Customer = record.Customer,
                MaterialName = record.MaterialName,
                MaterialGrade = record.MaterialGrade,
                Thickness = record.Thickness,
                Units = record.Units,
                CutType = record.CutType,
                Notes = record.Notes,
                Settings = record.Settings,
                PackageSha256 = current.PackageSha256,
                SavedAt = current.SavedAt.AddMinutes(1),
            };
            _records[index] = saved;
            _packages[(id, saved.Revision)] = _packages[(id, current.Revision)];
            return Task.FromResult(saved);
        }

        public Task<DrawingLibraryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryPage?> GetRevisionsAsync(Guid id, int offset = 0,
            int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UploadAsync(DrawingLibraryRecord record, byte[] package,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UpdatePackageAsync(Guid id, DrawingLibraryRecord record, byte[] package,
            int expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
