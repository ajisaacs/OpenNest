using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class AutoNestFormStockOptionsTests
{
    [Theory]
    [InlineData("invalid", false)]
    [InlineData("-1", false)]
    [InlineData("NaN", false)]
    [InlineData("27", true)]
    public void PendingCostEditsAreValidatedBeforePublishing(string text, bool valid)
    {
        RunSta(() =>
        {
            using var form = new AutoNestForm(new Nest());
            form.LoadPlateOptions(new List<PlateOption> { new() { Width = 48, Length = 96, Cost = 5 } }, 0.5);
            var grid = GetGrid(form);
            ((TabControl)form.Controls.Find("tabControl", true).Single()).SelectedIndex = 1;
            form.Show();
            grid.Focus();
            grid.CurrentCell = grid.Rows[0].Cells[1];
            Assert.True(grid.BeginEdit(false));
            grid.EditingControl.Text = text;
            Assert.Equal(valid, form.TryGetPlateOptions(out var options, out var error));
            if (valid)
                Assert.Equal(27, Assert.Single(options).Cost);
            else
            {
                Assert.Empty(options);
                Assert.Contains("cost", error);
            }
            grid.CancelEdit();
        });
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(5, 8, true)]
    [InlineData(5, 0, false)]
    [InlineData(0, 5, false)]
    [InlineData(-1, -1, false)]
    [InlineData(double.NaN, 5, false)]
    [InlineData(double.PositiveInfinity, 5, false)]
    public void CostRowsRequireConsistentFiniteValues(double first, double second, bool valid)
    {
        RunSta(() =>
        {
            using var form = new AutoNestForm(new Nest());
            form.LoadPlateOptions(new List<PlateOption>
            {
                new() { Width = 48, Length = 96, Cost = first },
                new() { Width = 60, Length = 120, Cost = second },
            }, 0.5);
            Assert.Equal(valid, form.TryGetPlateOptions(out var options, out var error));
            if (!valid)
            {
                Assert.Empty(options);
                Assert.Contains("cost", error);
            }
            AssertNewRow(GetGrid(form));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StockRowsCanBeSelectedAndRemoved(bool loadSavedOptions)
    {
        RunSta(() =>
        {
            using var form = new AutoNestForm(new Nest());
            if (loadSavedOptions)
                form.LoadPlateOptions(new List<PlateOption>
                {
                    new() { Width = 48, Length = 96, Cost = 25 },
                    new() { Width = 60, Length = 120, Cost = 40 },
                }, 0.5);
            form.OptimizePlateSize = true;
            var grid = GetGrid(form);

            Assert.True(grid.RowHeadersVisible);
            Assert.True(grid.AllowUserToDeleteRows);
            Assert.Equal(DataGridViewSelectionMode.RowHeaderSelect, grid.SelectionMode);
            grid.Rows[0].Selected = true;
            Assert.True(grid.Rows[0].Selected);

            var remaining = form.GetPlateOptions().Count;
            Assert.IsAssignableFrom<IList>(grid.DataSource).RemoveAt(grid.SelectedRows[0].Index);
            Assert.Equal(remaining - 1, form.GetPlateOptions().Count);
            AssertNewRow(grid);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StockOptionsKeepAnAddRowAfterAddingAPlate(bool loadSavedOptions)
    {
        RunSta(() =>
        {
            using var form = new AutoNestForm(new Nest());
            if (loadSavedOptions)
                form.LoadPlateOptions(new List<PlateOption>
                {
                    new() { Width = 48, Length = 96, Cost = 25 },
                }, 0.5);
            form.OptimizePlateSize = true;
            var grid = GetGrid(form);
            var initialCount = form.GetPlateOptions().Count;

            AssertNewRow(grid);
            var rows = Assert.IsAssignableFrom<IBindingList>(grid.DataSource);
            Assert.True(rows.AllowNew);
            var added = rows.AddNew()!;
            SetValue(added, "Size", "84 x 168");
            SetValue(added, "Cost", loadSavedOptions ? 123.5 : 0);
            ((ICancelAddNew)rows).EndNew(rows.Count - 1);

            var options = form.GetPlateOptions();
            Assert.Equal(initialCount + 1, options.Count);
            var option = options.Last();
            Assert.Equal(84, option.Width);
            Assert.Equal(168, option.Length);
            Assert.Equal(loadSavedOptions ? 123.5 : 0, option.Cost);
            AssertNewRow(grid);
        });
    }

    [Fact]
    public void BlankAndCancelledRowsDoNotBecomeStockOptions()
    {
        RunSta(() =>
        {
            using var form = new AutoNestForm(new Nest());
            var saved = new List<PlateOption>
            {
                new() { Width = 48, Length = 96, Cost = 25 },
            };
            form.LoadPlateOptions(saved, 0.5);
            var grid = GetGrid(form);
            var rows = Assert.IsAssignableFrom<IBindingList>(grid.DataSource);
            var addNew = Assert.IsAssignableFrom<ICancelAddNew>(rows);

            rows.AddNew();
            Assert.Single(form.GetPlateOptions());
            addNew.CancelNew(rows.Count - 1);
            Assert.Single((IEnumerable)rows);

            rows.Clear();
            Assert.Empty(form.GetPlateOptions());
            AssertNewRow(grid);
            Assert.Single(saved);
            Assert.Equal(48, saved[0].Width);
            Assert.Equal(96, saved[0].Length);
            Assert.Equal(25, saved[0].Cost);
        });
    }

    [Fact]
    public void StockSizeRegexHasExplicitTimeout()
    {
        var regex = Assert.IsType<Regex>(typeof(AutoNestForm)
            .GetField("SizePattern", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        Assert.Equal(TimeSpan.FromMilliseconds(250), regex.MatchTimeout);
        Assert.Equal(RegexOptions.None, regex.Options);
    }

    [Theory]
    [InlineData("48 x")]
    [InlineData("abc")]
    [InlineData("0 x 96")]
    [InlineData("-1 x 96")]
    [InlineData("١ x 96")]
    public void NonblankInvalidRowRejectsWholeCollection(string invalid) => RunSta(() =>
    {
        using var form = new StockTestForm();
        form.LoadPlateOptions(new List<PlateOption> { new() { Width = 48, Length = 96, Cost = 25 } }, 0.5);
        var grid = GetGrid(form);
        var rows = Assert.IsAssignableFrom<IBindingList>(grid.DataSource);
        var row = rows.AddNew()!;
        SetValue(row, "Size", invalid);
        SetValue(row, "Cost", 25);
        ((ICancelAddNew)rows).EndNew(rows.Count - 1);

        Assert.False(form.TryGetPlateOptions(out var options, out var error));
        Assert.Empty(options);
        Assert.Contains(invalid, error);
        Assert.Contains("row 2", error);
        Assert.Throws<FormatException>(() => form.GetPlateOptions());
        Assert.NotEmpty(grid.Rows[1].ErrorText);

        SetValue(row, "Size", "72.5 × 144.25");
        Assert.True(form.TryGetPlateOptions(out options, out error));
        Assert.Null(error);
        Assert.Equal(2, options.Count);
        Assert.Equal(72.5, options[1].Width);
        Assert.Equal(144.25, options[1].Length);
        Assert.Equal(25, options[0].Cost);
        Assert.Empty(grid.Rows[1].ErrorText);
    });

    [Fact]
    public void TimeoutAfterValidRowPublishesNoPrefixAndDoesNotAcceptDialog() => RunSta(() =>
    {
        using var form = new StockTestForm();
        form.LoadPlateOptions(new List<PlateOption> { new() { Width = 48, Length = 96 } }, 0.5);
        var grid = GetGrid(form);
        var rows = Assert.IsAssignableFrom<IBindingList>(grid.DataSource);
        var row = rows.AddNew()!;
        SetValue(row, "Size", "timeout row");
        ((ICancelAddNew)rows).EndNew(rows.Count - 1);
        form.FailOn = "timeout row";

        Assert.False(form.TryGetPlateOptions(out var options, out var error));
        Assert.Empty(options);
        Assert.Contains("timeout row", error);
        Assert.True(form.TryClose(DialogResult.OK));
        Assert.Equal(DialogResult.None, form.DialogResult);
        Assert.Contains("timeout row", Assert.Single(form.Notices));
        Assert.True(form.MatchCalls > 1);

        form.Notices.Clear();
        Assert.False(form.TryClose(DialogResult.Cancel));
        Assert.Empty(form.Notices);
        Assert.False(Assert.IsType<Button>(form.Controls.Find("cancelButton", true).Single()).CausesValidation);
    });

    [Fact]
    public void InvalidPendingEditCannotPublishOldBoundValue() => RunSta(() =>
    {
        using var form = new StockTestForm();
        form.LoadPlateOptions(new List<PlateOption> { new() { Width = 48, Length = 96 } }, 0.5);
        var grid = GetGrid(form);
        form.Show();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        Assert.True(grid.BeginEdit(false));
        grid.EditingControl.Text = "48 x";

        Assert.False(form.TryGetPlateOptions(out var options, out var error));
        Assert.Empty(options);
        Assert.Contains("48 x", error);
        grid.CancelEdit();
    });

    [Fact]
    public void FractionalStockSizesRoundTripUnderCommaDecimalCulture() => RunSta(() =>
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using var form = new StockTestForm();
            // FormatSize must emit invariant dots or the reloaded collection would be
            // rejected as invalid under the invariant parser.
            form.LoadPlateOptions(new List<PlateOption>
            {
                new() { Width = 48.5, Length = 96.25, Cost = 25 },
            }, 0.5);
            Assert.True(form.TryGetPlateOptions(out var options, out var error));
            Assert.Null(error);
            Assert.Equal(48.5, options[0].Width);
            Assert.Equal(96.25, options[0].Length);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    });

    private sealed class StockTestForm : AutoNestForm
    {
        public StockTestForm() : base(new Nest()) { }
        public string? FailOn { get; set; }
        public int MatchCalls { get; private set; }
        public List<string> Notices { get; } = new();

        internal override Match MatchStockSize(string value)
        {
            MatchCalls++;
            if (value == FailOn)
                throw new RegexMatchTimeoutException();
            return base.MatchStockSize(value);
        }

        internal override void ReportStockValidationFailure(string error) => Notices.Add(error);

        public bool TryClose(DialogResult result)
        {
            DialogResult = result;
            var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
            OnFormClosing(closing);
            return closing.Cancel;
        }
    }

    private static DataGridView GetGrid(AutoNestForm form)
    {
        // Binding needs a context even when the test does not show the form.
        form.BindingContext = new BindingContext();
        var grid = Assert.IsType<DataGridView>(form.Controls.Find("plateGrid", true).Single());
        grid.CreateControl();
        return grid;
    }

    private static void AssertNewRow(DataGridView grid)
    {
        Assert.True(grid.AllowUserToAddRows);
        Assert.True(grid.NewRowIndex >= 0);
        Assert.True(grid.Rows[grid.NewRowIndex].IsNewRow);
    }

    private static void SetValue(object row, string property, object value) =>
        TypeDescriptor.GetProperties(row)[property]!.SetValue(row, value);

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(15), "The STA test did not complete.");
}
