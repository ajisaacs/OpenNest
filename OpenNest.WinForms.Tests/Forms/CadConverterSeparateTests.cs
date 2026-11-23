using System.IO;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// A file that draws several parts separates into one drawing per part, named from the file's
/// titles where it has them, each keeping its holes, IDs and settings.
/// </summary>
public class CadConverterSeparateTests
{
    // Three parts in a row: a titled square with a hole, a titled disc with a hole, and an
    // untitled frame with a loose part sitting in its window.
    private const string ThreeParts =
        "<svg xmlns='http://www.w3.org/2000/svg' width='100mm' height='30mm' viewBox='0 0 100 30'>"
        + "<title>sheet</title>"
        + "<g><title>tag</title><path d='M0 0H20V20H0Z M8 4A2 2 0 1 0 12 4A2 2 0 1 0 8 4Z'/></g>"
        + "<g><title>bauble</title><circle cx='40' cy='10' r='10'/><circle cx='40' cy='3' r='1'/></g>"
        + "<path d='M60 0H90V30H60Z M65 5H85V25H65Z'/>"
        + "<path d='M70 10H80V20H70Z'/>"
        + "</svg>";

    [Fact]
    public void SeparateAll_GivesEachPartItsOwnDrawing() => ArchUnitsTests.RunSta(() =>
        WithFile(form =>
        {
            var list = Field<FileListControl>(form, "fileList");
            var source = list.SelectedItem!;
            source.Quantity = 3;
            source.Customer = "Acme";
            var ids = source.Entities.Select(e => e.Id).ToHashSet();

            Assert.True(form.SeparateParts(null));

            Assert.Equal(new[] { "bauble", "parts-3", "parts-4", "tag" }, list.Items.Select(i => i.Name));
            var drawings = form.GetDrawings();
            Assert.Equal(4, drawings.Count);
            Assert.All(drawings, d =>
            {
                Assert.Equal(3, d.Quantity.Required);
                Assert.Equal("Acme", d.Customer);
                Assert.Equal(source.Path, d.Source.Path);
                Assert.All(d.SourceEntities, e => Assert.Contains(e.Id, ids));
            });
            Assert.Equal(ids.Count, drawings.Sum(d => d.SourceEntities.Count));

            var bauble = drawings.Single(d => d.Name == "bauble");
            Assert.Equal(2, bauble.SourceEntities.Count);
            // The frame keeps its window; the loose square inside it is a part of its own.
            Assert.Equal(8, drawings.Single(d => d.Name == "parts-3").SourceEntities.Count);
            Assert.Equal(4, drawings.Single(d => d.Name == "parts-4").SourceEntities.Count);
        }));

    [Fact]
    public void SeparateOne_LeavesTheRestTogether_UnderTheFileName() => ArchUnitsTests.RunSta(() =>
        WithFile(form =>
        {
            var list = Field<FileListControl>(form, "fileList");
            var excluded = list.SelectedItem!.Entities.OfType<Circle>().Single(c => c.Radius < 5 && c.Center.X > 30);
            list.SelectedItem.ExcludedEntityIds.Add(excluded.Id);

            Assert.True(form.SeparateParts(new[] { 1 }));

            Assert.Equal(new[] { "bauble", "parts" }, list.Items.Select(i => i.Name));
            Assert.Equal("bauble", list.SelectedItem!.Name);
            Assert.Equal(new[] { excluded.Id }, list.SelectedItem.ExcludedEntityIds);
            Assert.Empty(list.Items.Single(i => i.Name == "parts").ExcludedEntityIds);
            WaitForParts(form);
            Assert.Equal(1, form.CurrentBodies!.Count);
        }));

    [Fact]
    public void OnePart_OffersNoSeparation() => ArchUnitsTests.RunSta(() =>
        WithFile(form =>
        {
            Assert.Equal(1, form.CurrentBodies!.Count);
            Assert.False(Field<Button>(form, "btnSeparate").Visible);
            Assert.False(form.SeparateParts(null));
        }, "<svg xmlns='http://www.w3.org/2000/svg' width='20mm' height='20mm' viewBox='0 0 20 20'>"
            + "<path d='M0 0H20V20H0Z M8 4A2 2 0 1 0 12 4A2 2 0 1 0 8 4Z'/></svg>"));

    private static void WithFile(Action<CadConverterForm> test, string svg = ThreeParts)
    {
        var directory = Directory.CreateTempSubdirectory("opennest-separate-").FullName;
        try
        {
            var path = Path.Combine(directory, "parts.svg");
            File.WriteAllText(path, svg);
            using var form = new CadConverterForm { DrawingUnits = Units.Millimeters };
            Show(form);
            form.AddFile(path);
            WaitForParts(form);
            if (svg == ThreeParts)
            {
                Assert.Equal(4, form.CurrentBodies!.Count);
                var button = Field<Button>(form, "btnSeparate");
                Assert.True(button.Visible);
                Assert.Equal("Separate (4)", button.Text);
                Assert.Equal(4, Field<EntityView>(form, "entityView1").SeparablePartCount);
            }
            test(form);
            form.Close();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // Loading an item re-checks the filter lists, which posts a filter change that restarts the
    // parts search; wait until the latest search has finished and no newer one has started.
    private static void WaitForParts(CadConverterForm form)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var search = form.BodiesSearch;
            while (!search.IsCompleted && DateTime.UtcNow < deadline)
                Application.DoEvents();
            Application.DoEvents();
            if (search.IsCompleted && search == form.BodiesSearch)
                return;
        }
        Assert.Fail("The parts search did not finish.");
    }

    private static T Field<T>(object owner, string name) =>
        (T)typeof(CadConverterForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
