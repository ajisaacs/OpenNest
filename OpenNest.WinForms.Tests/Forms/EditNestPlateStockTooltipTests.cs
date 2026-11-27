using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Shapes;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>The plate list names each coded plate's inventory item, with its ledger location when known.</summary>
public class EditNestPlateStockTooltipTests
{
    [Fact]
    public void CodedPlateShowsItsStockInThePlateListTooltip() => RunSta(() =>
    {
        var nest = new Nest("stock tooltip");
        var drawing = new RectangleShape { Name = "SYN", Length = 4, Width = 2 }.GetDrawing();
        nest.Drawings.Add(drawing);
        var plate = nest.CreatePlate();
        plate.Parts.Add(new Part(drawing));
        plate.StockItemCodes = ["DROP-1182"];
        using var form = new EditNestForm(nest);
        form.PlateView.SetOverlapAutoCheck(null);

        form.LedgerLocations["DROP-1182"] = "B3";
        form.UpdatePlateList();

        var plates = Assert.IsType<PlateListBox>(Assert.Single(form.Controls.Find("platesListView", true)));
        Assert.Equal("Stock: DROP-1182 (B3)", plates.GetToolTipText(0));
        Assert.Equal("", plates.GetToolTipText(plates.Items.Count - 1));
    });

    private static void RunSta(System.Action testBody) =>
        StaTestThread.Run(testBody, TimeSpan.FromSeconds(60), "The stock tooltip STA test did not complete.");
}
