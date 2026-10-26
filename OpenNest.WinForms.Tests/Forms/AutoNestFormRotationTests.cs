using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class AutoNestFormRotationTests
{
    [Fact]
    public void RotationColumnsShowDegreesAndNestItemsKeepRadians()
    {
        RunSta(() =>
        {
            var drawing = new Drawing("grain", new Program()) { Quantity = { Required = 2 } };
            drawing.Constraints.StartAngle = 0;
            drawing.Constraints.EndAngle = System.Math.PI / 2;
            drawing.Constraints.StepAngle = System.Math.PI / 2;
            var nest = new Nest();
            nest.Drawings.Add(drawing);

            using var form = new AutoNestForm(nest);
            var grid = GetGrid(form);
            Assert.Equal(0.0, grid.Rows[0].Cells[Column(grid, "RotationStart")].Value);
            Assert.Equal(90.0, grid.Rows[0].Cells[Column(grid, "RotationEnd")].Value);
            Assert.Equal(90.0, grid.Rows[0].Cells[Column(grid, "StepAngle")].Value);

            var item = Assert.Single(form.GetNestItems());
            Assert.Equal(System.Math.PI / 2, item.RotationEnd);
            Assert.Equal(System.Math.PI / 2, item.StepAngle);

            grid.Rows[0].Cells[Column(grid, "RotationEnd")].Value = 180.0;
            grid.Rows[0].Cells[Column(grid, "StepAngle")].Value = 45.0;
            item = Assert.Single(form.GetNestItems());
            Assert.Equal(System.Math.PI, item.RotationEnd, 12);
            Assert.Equal(System.Math.PI / 4, item.StepAngle, 12);
        });
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
