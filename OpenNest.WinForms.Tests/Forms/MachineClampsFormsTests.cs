using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// Machine clamps in the desktop: the Clamps tab of Machine Configuration edits a machine's rail,
/// and Nest &gt; Clamps saves its machine defaults to the nest's machine.
/// </summary>
public class MachineClampsFormsTests
{
    [Fact]
    public void ClampsTab_EditsTheMachineRail()
    {
        RunSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(directory);
            try
            {
                var provider = new LocalJsonProvider(directory);
                var machine = new MachineConfig { Name = "Laser" };
                provider.SaveMachine(machine);

                using var form = new MachineConfigForm(provider);
                var node = Find<TreeView>(form, "_tree").Nodes.Cast<TreeNode>().Single(n => n.Tag is MachineConfig);
                // A hidden tree raises no AfterSelect, so show the machine as selecting it would.
                typeof(MachineConfigForm)
                    .GetMethod("Tree_AfterSelect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(form, new object[] { null, new TreeViewEventArgs(node) });
                var shown = (MachineConfig)node.Tag;
                Assert.Null(shown.Clamps);

                var tab = All<TabControl>(form).Single().TabPages.Cast<TabPage>().Single(p => p.Text == "Clamps");
                All<CheckBox>(tab).Single(c => c.Text == "Give new nests these clamps").Checked = true;
                var add = All<Button>(tab).Single(b => b.Text == "Add");
                All<System.Windows.Forms.NumericUpDown>(add.Parent).Single().Value = 24;
                Click(add);

                Assert.NotNull(shown.Clamps);
                Assert.True(shown.Clamps!.Enabled);
                Assert.Equal(new double[] { 24 }, shown.Clamps.Centers);
                Assert.Equal(ClampSettings.DefaultWidth, shown.Clamps.Width);
                Assert.NotNull(shown.GetClampSettings().RailFor(Units.Inches));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    [Fact]
    public void ClampsDialog_WithoutAMachine_CannotSaveMachineDefaults()
    {
        RunSta(() =>
        {
            using var form = new ClampsForm(null, Units.Inches, ClampSettings.Fallback, null, null);

            Assert.False(All<Button>(form).Single(b => b.Text == "Save as Machine Defaults").Enabled);
            Assert.Contains(All<GroupBox>(form), g => g.Text == "Machine defaults (no machine assigned)");
        });
    }

    [Fact]
    public void ClampsDialog_SavesToTheNestsMachine_AndLoadsWhatItSaved()
    {
        RunSta(() =>
        {
            var rail = new ClampRail(1.5, 0.625, new[] { new RailClamp(ClampEdge.OriginSide, 6) });
            ClampSettings saved = null;
            using var form = new ClampsForm(rail, Units.Inches, ClampSettings.Fallback, "Fiber", s => saved = s);
            Assert.Contains(All<GroupBox>(form), g => g.Text == "Machine defaults (Fiber)");

            All<CheckBox>(form).Single(c => c.Text == "Give new nests these clamps").Checked = true;
            Click(All<Button>(form).Single(b => b.Text == "Save as Machine Defaults"));

            Assert.NotNull(saved);
            Assert.True(saved.Enabled);
            Assert.Equal(new double[] { 6 }, saved.Centers);

            form.Editor.RemoveCenterAt(0);
            Click(All<Button>(form).Single(b => b.Text == "Load Machine Defaults"));
            Assert.Equal(6, Assert.Single(form.Editor.Clamps).Center);
        });
    }

    // PerformClick does nothing on a form that is not shown.
    private static void Click(Button button) =>
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(button, new object[] { EventArgs.Empty });

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
