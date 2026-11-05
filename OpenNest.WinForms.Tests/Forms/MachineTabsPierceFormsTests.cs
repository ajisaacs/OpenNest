using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Forms;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// Cutting settings kept per machine thickness: the Tabs &amp; Pierce tab of Machine
/// Configuration, copying them between thicknesses, and the cutting panel's machine modes.
/// </summary>
public class MachineTabsPierceFormsTests
{
    [Fact]
    public void TabsPierceTab_SetsTheThicknessesCuttingSettingsOnlyWhileChecked()
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

                var page = All<TabControl>(form).Single().TabPages.Cast<TabPage>().Single(p => p.Text == "Tabs & Pierce");
                var use = All<CheckBox>(page).Single(c => c.Text == "Use for this thickness");
                var panel = All<CuttingPanel>(page).Single();
                // A thickness edits its leads on its own tabs, so this panel has none.
                Assert.Empty(All<LeadsEditor>(page));
                Assert.False(use.Checked);
                Assert.False(panel.Enabled);
                Assert.Null(shown.Cutting);

                use.Checked = true;
                Assert.NotNull(shown.Cutting);
                Assert.False(shown.Cutting!.Tabs);

                All<CheckBox>(panel).Single(c => c.Text == "Add tabs to hold parts in the sheet").Checked = true;
                All<RadioButton>(panel).Single(r => r.Text == "Tab parts by size").Checked = true;
                Assert.True(shown.Cutting!.Tabs);
                Assert.True(shown.Cutting.TabBySize);

                use.Checked = false;
                Assert.Null(shown.Cutting);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    [Fact]
    public void CopyCutting_GivesEachThicknessItsOwnCopy()
    {
        var source = new ThicknessCuttingConfig { Tabs = true, TabSize = 0.04, TabSizeMaxWidth = 3 };

        var copy = MachineConfigForm.CopyCutting(source);

        Assert.NotSame(source, copy);
        Assert.True(copy.Tabs);
        Assert.Equal(0.04, copy.TabSize);
        Assert.Equal(3, copy.TabSizeMaxWidth);
        Assert.Null(MachineConfigForm.CopyCutting(null));
    }

    [Fact]
    public void CuttingPanel_ShowsWhereItSavesAndOffersOnlyMachineLeads()
    {
        RunSta(() =>
        {
            using var panel = new CuttingPanel
            {
                MachineLeads = true,
                Source = "Saved for Fiber 1 · Mild Steel A36 0.25 in",
            };
            panel.LoadFromParameters(new CuttingParameters());

            Assert.Contains(All<Label>(panel), l => l.Text == "Saved for Fiber 1 · Mild Steel A36 0.25 in");
            var editors = All<LeadsEditor>(panel).ToList();
            Assert.Equal(2, editors.Count);
            Assert.All(editors, e => Assert.False(e.AllowLineLine));
            Assert.All(editors, e => Assert.False(e.ShowArcSweep));
            Assert.False(editors.Single(e => e.Emphasis == ContourType.External).AllowCleanHole);

            panel.Source = null;
            panel.MachineLeads = false;
            Assert.All(editors, e => Assert.True(e.AllowLineLine));
        });
    }

    private static IEnumerable<TreeNode> Descendants(TreeNodeCollection nodes) =>
        nodes.Cast<TreeNode>().SelectMany(n => Descendants(n.Nodes).Prepend(n));

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(root.Controls.Find(name, true).Single());

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, Timeout.InfiniteTimeSpan, "The STA test did not complete.");
}

/// <summary>Where the Cutting Parameters dialog saves: the nest's machine thickness, or this PC.</summary>
// Shares the saved cutting settings with the cutting inspection tests, so runs with them one at a time.
[Collection("Fill operation lifetime")]
public class CuttingSettingsSaveTests
{
    private static MachineConfig Machine() => new()
    {
        Name = "Fiber 1",
        Materials =
        {
            new MaterialConfig
            {
                Name = "Mild Steel",
                Grade = "A36",
                Thicknesses = { new ThicknessConfig { Value = 0.25, LeadIn = new LeadConfig { Type = "Line", Length = 0.1, Angle = 60 } } },
            },
        },
    };

    private static Nest NestFor(MachineConfig machine, double thickness) => new()
    {
        Thickness = thickness,
        Material = new Material("Mild Steel", "A36", 0.2836),
        Machine = new NestMachine(machine.Id, machine.Name),
    };

    private static CuttingParameters Edited() => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 0.2, ApproachAngle = 45 },
        SupportTabs = true,
        TabBySize = true,
        TabSizeMaxWidth = 2,
        TabConfig = new NormalTab { Size = 0.03 },
    };

    [Fact]
    public void SavesToTheMachineThicknessAndDropsThisPcsCopyForThatJob()
    {
        WithMachines((provider, machine) =>
        {
            var nest = NestFor(machine, 0.25);
            var stale = new CuttingParameters();
            MachineCuttingDefaults.ApplyTo(stale, machine, nest);
            MachineSettings.SaveLastCuttingParameters(stale);
            var warnings = new List<string>();

            Assert.True(MachineSettings.SaveCuttingParameters(nest, Edited(), provider, warnings.Add));

            Assert.Empty(warnings);
            var thickness = provider.GetMachine(machine.Id)!.Materials[0].Thicknesses[0];
            Assert.Equal(0.2, thickness.LeadIn.Length);
            Assert.True(thickness.Cutting!.TabBySize);
            Assert.Equal(2, thickness.Cutting.TabSizeMaxWidth);
            Assert.False(MachineCuttingDefaults.IsRecordedFor(MachineSettings.LastCuttingParameters(), machine, nest));
        });
    }

    [Fact]
    public void SavesAsThisPcsSettingsWhenTheMachineHasNoSuchThickness()
    {
        WithMachines((provider, machine) =>
        {
            var nest = NestFor(machine, 0.5);

            Assert.False(MachineSettings.SaveCuttingParameters(nest, Edited(), provider, _ => Assert.Fail("No warning expected.")));

            Assert.Null(provider.GetMachine(machine.Id)!.Materials[0].Thicknesses[0].Cutting);
            Assert.True(MachineSettings.LastCuttingParameters().TabBySize);
        });
    }

    [Fact]
    public void KeepsTheEditOnThisPcForThatJobWhenTheMachineCannotBeSaved()
    {
        WithMachines((provider, machine) =>
        {
            var nest = NestFor(machine, 0.25);
            var warnings = new List<string>();

            Assert.False(MachineSettings.SaveCuttingParameters(nest, Edited(), new FailingProvider(provider), warnings.Add));

            Assert.Contains("Fiber 1", Assert.Single(warnings));
            var last = MachineSettings.LastCuttingParameters();
            Assert.True(MachineCuttingDefaults.IsRecordedFor(last, machine, nest));
            Assert.True(last.TabBySize);
            // Recorded for the job, so the machine's settings do not replace the edit here.
            Assert.Equal(0.2, ((LineLeadIn)MachineSettings.LastCuttingParameters().ExternalLeadIn).Length);
        });
    }

    private static void WithMachines(Action<LocalJsonProvider, MachineConfig> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var settings = OpenNest.Properties.Settings.Default;
        var saved = settings.CuttingParametersJson;
        try
        {
            var provider = new LocalJsonProvider(directory);
            var machine = Machine();
            provider.SaveMachine(machine);
            test(provider, machine);
        }
        finally
        {
            settings.CuttingParametersJson = saved;
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FailingProvider(IDataProvider inner) : IDataProvider
    {
        public IReadOnlyList<MachineSummary> GetMachines() => inner.GetMachines();
        public MachineConfig? GetMachine(Guid id) => inner.GetMachine(id);
        public void SaveMachine(MachineConfig machine) => throw new IOException("The nest server could not be reached.");
        public void DeleteMachine(Guid id) => inner.DeleteMachine(id);
    }
}
