using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// The Outside Cuts and Inside Cuts tabs of a thickness in Machine Configuration: inside cuts
/// follow the outside leads until "Same as outside cuts" is cleared, which gives them a copy.
/// </summary>
public class MachineCutsFormsTests
{
    [Fact]
    public void InsideCutsTab_GivesHolesTheirOwnLeads()
    {
        RunSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(directory);
            try
            {
                var provider = new LocalJsonProvider(directory);
                var thickness = new ThicknessConfig
                {
                    Value = 0.25,
                    LeadIn = new LeadConfig { Type = "Arc", Radius = 0.125 },
                    LeadOut = new LeadConfig { Type = "None" },
                };
                provider.SaveMachine(new MachineConfig
                {
                    Name = "Laser",
                    Materials = { new MaterialConfig { Name = "Mild Steel", Thicknesses = { thickness } } },
                });

                using var form = new MachineConfigForm(provider);
                var node = Descendants(Find<TreeView>(form, "_tree").Nodes).Single(n => n.Tag is ThicknessConfig);
                // A hidden tree raises no AfterSelect, so show the thickness as selecting it would.
                typeof(MachineConfigForm)
                    .GetMethod("Tree_AfterSelect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(form, new object[] { null, new TreeViewEventArgs(node) });
                var shown = (ThicknessConfig)node.Tag;

                var pages = All<TabControl>(form).Single().TabPages.Cast<TabPage>().ToList();
                Assert.DoesNotContain(pages, p => p.Text is "Lead In" or "Lead Out");
                var outside = pages.Single(p => p.Text == "Outside Cuts");
                Assert.DoesNotContain(All<ComboBox>(outside), c => c.Items.Contains("Clean Hole"));

                var inside = pages.Single(p => p.Text == "Inside Cuts");
                var same = All<CheckBox>(inside).Single(c => c.Text == "Same as outside cuts");
                Assert.True(same.Checked);
                Assert.All(All<ComboBox>(inside), c => Assert.False(c.Enabled));
                Assert.NotNull(All<OpenNest.Controls.LeadPreview>(inside).Single().Parameters);

                same.Checked = false;
                Assert.Equal("Arc", shown.InsideLeadIn!.Type);
                Assert.Equal(0.125, shown.InsideLeadIn.Radius);
                Assert.NotSame(shown.LeadIn, shown.InsideLeadIn);
                Assert.All(All<ComboBox>(inside), c => Assert.True(c.Enabled));

                All<ComboBox>(inside).Single(c => c.Items.Contains("Clean Hole")).SelectedItem = "Clean Hole";
                Assert.Equal("Clean Hole", shown.InsideLeadIn.Type);
                Assert.Equal("Arc", shown.LeadIn.Type);
                // Only the chosen type's fields show: line length and arc radius.
                var leadInGrid = All<NumericUpDown>(inside).ToList();
                Assert.Equal(2, leadInGrid.Count);
                leadInGrid[1].Value = 0.07m;
                Assert.Equal(0.07, shown.InsideLeadIn.Radius, 6);
                Assert.Equal(0.125, shown.LeadIn.Radius);
                var preview = All<OpenNest.Controls.LeadPreview>(inside).Single().Parameters;
                Assert.Equal(0.07, Assert.IsType<OpenNest.CNC.CuttingStrategy.CleanHoleLeadIn>(preview.InternalLeadIn).ArcRadius, 6);

                same.Checked = true;
                Assert.Null(shown.InsideLeadIn);
                Assert.Null(shown.InsideLeadOut);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    private static IEnumerable<TreeNode> Descendants(TreeNodeCollection nodes) =>
        nodes.Cast<TreeNode>().SelectMany(n => Descendants(n.Nodes).Prepend(n));

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(root.Controls.Find(name, true).Single());

    private static IEnumerable<T> All<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;
            foreach (var nested in All<T>(child))
                yield return nested;
        }
    }

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, Timeout.InfiniteTimeSpan, "The STA test did not complete.");
}
