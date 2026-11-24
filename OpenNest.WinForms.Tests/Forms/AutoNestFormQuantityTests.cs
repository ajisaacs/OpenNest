using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class AutoNestFormQuantityTests
{
    [Fact]
    public void GridShowsRequiredAndPlacedAndQtyStartsAtRemaining()
    {
        RunSta(() =>
        {
            var (nest, drawing) = NestWithPlacedParts(required: 5, placed: 2);

            using var form = new AutoNestForm(nest);
            var grid = GetGrid(form);
            Assert.Equal(5, grid.Rows[0].Cells[Column(grid, "Required")].Value);
            Assert.Equal(2, grid.Rows[0].Cells[Column(grid, "Placed")].Value);
            Assert.Equal(3, grid.Rows[0].Cells[Column(grid, "Quantity")].Value);
            Assert.True(grid.Columns[Column(grid, "Required")].ReadOnly);
            Assert.True(grid.Columns[Column(grid, "Placed")].ReadOnly);
        });
    }

    [Fact]
    public void LoadButtonsSwitchQtyBetweenRequiredAndRemaining()
    {
        RunSta(() =>
        {
            var (nest, _) = NestWithPlacedParts(required: 5, placed: 2);
            var overPlaced = new Drawing("over", new Program()) { Quantity = { Required = 1 } };
            nest.Drawings.Add(overPlaced);
            var plate = nest.Plates[0];
            plate.Parts.Add(new Part(overPlaced));
            plate.Parts.Add(new Part(overPlaced));

            using var form = new AutoNestForm(nest);
            var grid = GetGrid(form);

            form.LoadPartQuantities(remaining: false);
            Assert.Equal(5, grid.Rows[0].Cells[Column(grid, "Quantity")].Value);
            Assert.Equal(1, grid.Rows[1].Cells[Column(grid, "Quantity")].Value);
            Assert.Equal(new[] { 5, 1 }, form.GetNestItems().Select(i => i.Quantity));

            form.LoadPartQuantities(remaining: true);
            Assert.Equal(3, grid.Rows[0].Cells[Column(grid, "Quantity")].Value);
            Assert.Equal(0, grid.Rows[1].Cells[Column(grid, "Quantity")].Value);
            Assert.Equal(3, Assert.Single(form.GetNestItems()).Quantity);
        });
    }

    private static (Nest Nest, Drawing Drawing) NestWithPlacedParts(int required, int placed)
    {
        var drawing = new Drawing("bracket", new Program()) { Quantity = { Required = required } };
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        var plate = new Plate(48, 96) { Quantity = 1 };
        nest.Plates.Add(plate);
        for (var i = 0; i < placed; i++)
            plate.Parts.Add(new Part(drawing));
        Assert.Equal(placed, drawing.Quantity.Nested);
        return (nest, drawing);
    }

    private static DataGridView GetGrid(AutoNestForm form)
    {
        // Binding needs a context even when the test does not show the form.
        form.BindingContext = new BindingContext();
        var grid = Assert.IsType<DataGridView>(form.Controls.Find("partsGrid", true).Single());
        grid.CreateControl();
        return grid;
    }

    private static int Column(DataGridView grid, string property) =>
        grid.Columns.Cast<DataGridViewColumn>().Single(c => c.DataPropertyName == property).Index;

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(15), "The STA test did not complete.");
}
