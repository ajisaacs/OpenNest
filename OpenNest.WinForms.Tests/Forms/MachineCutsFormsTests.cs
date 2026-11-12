using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// The Outside Cuts and Inside Cuts tabs of a thickness in Machine Configuration: inside cuts
/// follow the outside leads until "Same as outside cuts" is cleared, which gives them a copy.
/// </summary>
public class MachineCutsFormsTests
{
    [Fact]
    public void SpacingTab_SetsOnlyTheCheckedSpacing()
    {
        RunSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(directory);
            try
            {
                var provider = new LocalJsonProvider(directory);
                provider.SaveMachine(new MachineConfig
                {
                    Name = "Laser",
                    Materials = { new MaterialConfig { Name = "Mild Steel", Thicknesses = { new ThicknessConfig { Value = 0.25 } } } },
                });

                using var form = new MachineConfigForm(provider);
                var node = Descendants(Find<TreeView>(form, "_tree").Nodes).Single(n => n.Tag is ThicknessConfig);
                typeof(MachineConfigForm)
                    .GetMethod("Tree_AfterSelect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(form, new object[] { null, new TreeViewEventArgs(node) });
                var shown = (ThicknessConfig)node.Tag;

                var page = All<TabControl>(form).Single().TabPages.Cast<TabPage>().Single(p => p.Text == "Spacing");
                var checks = All<CheckBox>(page).ToList();
                var boxes = All<NumericUpDown>(page).ToList();
                Assert.Equal(2, checks.Count);
                Assert.Equal(5, boxes.Count);
                Assert.All(boxes, b => Assert.False(b.Enabled));

                checks[0].Checked = true;
                boxes[0].Value = 0.5m;
                Assert.Equal(0.5, shown.PartSpacing);
                Assert.Null(shown.EdgeSpacing);

                checks[1].Checked = true;
                boxes[4].Value = 2m;
                Assert.Equal(2, shown.EdgeSpacing!.Top);

                checks[0].Checked = false;
                checks[1].Checked = false;
                Assert.Null(shown.PartSpacing);
                Assert.Null(shown.EdgeSpacing);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
    }

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

    [Fact]
    public void GeneralTab_BlankNameFollowsTheValueToItsStandardGaugeUntilOneIsTyped()
    {
        RunSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(directory);
            try
            {
                var provider = new LocalJsonProvider(directory);
                provider.SaveMachine(new MachineConfig
                {
                    Name = "Laser",
                    Units = UnitSystem.Inches,
                    Materials = { new MaterialConfig { Name = "Mild Steel", Thicknesses = { new ThicknessConfig { Value = 0.3 } } } },
                });

                using var form = new MachineConfigForm(provider);
                var node = Descendants(Find<TreeView>(form, "_tree").Nodes).Single(n => n.Tag is ThicknessConfig);
                typeof(MachineConfigForm)
                    .GetMethod("Tree_AfterSelect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(form, new object[] { null, new TreeViewEventArgs(node) });
                var shown = (ThicknessConfig)node.Tag;

                var page = All<TabControl>(form).Single().TabPages.Cast<TabPage>().Single(p => p.Text == "General");
                var value = All<NumericUpDown>(page).First();
                var name = All<TextBox>(page).Single(b => b.PlaceholderText == "e.g. 16 ga");
                Assert.Equal("", shown.Name);

                value.Value = 0.0598m;
                Assert.Equal("16 ga", shown.Name);
                Assert.Equal("0.0598 (16 ga)", node.Text);

                // Still the old value's standard name, so it follows; no standard match clears it.
                value.Value = 0.25m;
                Assert.Equal("1/4\"", shown.Name);
                value.Value = 0.3m;
                Assert.Equal("", shown.Name);

                name.Text = "Shop plate";
                value.Value = 0.0598m;
                Assert.Equal("Shop plate", shown.Name);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    [Fact]
    public void Opening_NamesBlankStandardThicknessesAndOffersToSaveThem()
    {
        RunSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(directory);
            try
            {
                var provider = new LocalJsonProvider(directory);
                provider.SaveMachine(new MachineConfig
                {
                    Name = "Laser",
                    Units = UnitSystem.Inches,
                    Materials =
                    {
                        new MaterialConfig
                        {
                            Name = "Mild Steel",
                            Thicknesses =
                            {
                                new ThicknessConfig { Value = 0.25 },
                                new ThicknessConfig { Value = 0.0598, Name = "Shop sheet" },
                                new ThicknessConfig { Value = 0.3 },
                            },
                        },
                    },
                });

                using (var form = new MachineConfigForm(provider))
                {
                    var names = Descendants(Find<TreeView>(form, "_tree").Nodes)
                        .Where(n => n.Tag is ThicknessConfig).Select(n => n.Text).ToList();
                    Assert.Equal(new[] { "0.25 (1/4\")", "0.0598 (Shop sheet)", "0.3" }, names);
                    Assert.True(Dirty(form));
                }

                // Closed without saving, so the file is still blank; once the names are saved,
                // opening again finds nothing to name and changes nothing.
                var saved = provider.GetMachine(provider.GetMachines().Single().Id)!;
                Assert.True(MachineConfigForm.FillBlankThicknessNames(saved));
                provider.SaveMachine(saved);
                Assert.Equal("1/4\"", provider.GetMachine(saved.Id)!.Materials[0].Thicknesses[0].Name);
                using (var reopened = new MachineConfigForm(provider))
                    Assert.False(Dirty(reopened));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    private static bool Dirty(MachineConfigForm form) =>
        (bool)typeof(MachineConfigForm)
            .GetField("_dirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(form)!;

    private static IEnumerable<TreeNode> Descendants(TreeNodeCollection nodes) =>
        nodes.Cast<TreeNode>().SelectMany(n => Descendants(n.Nodes).Prepend(n));

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(root.Controls.Find(name, true).Single());

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, Timeout.InfiniteTimeSpan, "The STA test did not complete.");
}
