using System.Collections;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class AutoNestFormStockOptionsTests
{
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
            SetValue(added, "Cost", 123.5);
            ((ICancelAddNew)rows).EndNew(rows.Count - 1);

            var options = form.GetPlateOptions();
            Assert.Equal(initialCount + 1, options.Count);
            var option = options.Last();
            Assert.Equal(84, option.Width);
            Assert.Equal(168, option.Length);
            Assert.Equal(123.5, option.Cost);
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

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
