using System.Reflection;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Shapes;

namespace OpenNest.WinForms.Tests.Forms;

[Collection("Fill operation lifetime")]
public class EditNestLibraryChangeTests
{
    [Fact]
    public void ALibraryAdd_IsListedOnTheDrawingsTabAndSelectedThere() => RunSta(() =>
    {
        var nest = new Nest("library add");
        var existing = new RectangleShape { Name = "A-EXISTING", Length = 3, Width = 1 }.GetDrawing();
        existing.Quantity.Required = 1;
        nest.Drawings.Add(existing);

        using var form = new EditNestForm(nest) { Width = 1100, Height = 700 };
        form.PlateView.SetOverlapAutoCheck(null);
        form.Show();
        Application.DoEvents();

        var tabs = Field<TabControl>(form, "tabControl1");
        var drawingsTab = Field<TabPage>(form, "tabPage2");
        var list = Field<DrawingListBox>(form, "drawingListBox1");
        tabs.SelectedTab = Field<TabPage>(form, "tabPage1");

        // What the Drawing Library dialog does to the nest before it raises NestChanged.
        var added = new RectangleShape { Name = "B-LIBRARY", Length = 4, Width = 2 }.GetDrawing();
        added.Quantity.Required = 3;
        nest.Drawings.Add(added);
        form.ShowLibraryChange(added);

        Assert.Same(drawingsTab, tabs.SelectedTab);
        Assert.Equal(new object[] { existing, added }, list.Items.Cast<object>());
        Assert.Same(added, list.SelectedItem);
    });

    private static T Field<T>(EditNestForm form, string name) =>
        (T)typeof(EditNestForm)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromMinutes(3), "The STA test did not complete.");
}
