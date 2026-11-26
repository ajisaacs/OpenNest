using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// Opening Machine Configuration from the status bar's material selects the nest machine's
/// settings for the nest's material: the thickness when it has one on file, else the grade,
/// else the material, else the machine.
/// </summary>
public class MachineConfigNestMaterialTests
{
    private static readonly Guid LaserId = Guid.NewGuid();

    [Theory]
    [InlineData("mild steel", "a36", 0.5, "0.5")]
    [InlineData("Mild Steel", "A36", 0.375, "A36")]
    [InlineData("Mild Steel", "", 0.25, "0.25")]
    [InlineData("Mild Steel", "A572", 0.25, "Mild Steel")]
    [InlineData("Aluminum", "", 0.25, "Laser")]
    public void SelectNestMaterial_SelectsTheMostSpecificNodeOnFile(
        string material, string grade, double thickness, string expected)
    {
        RunSta(() => WithForm(form =>
        {
            var nest = new Nest
            {
                Machine = new NestMachine(LaserId, "Laser"),
                Material = new Material(material, grade),
                Thickness = thickness,
            };

            var node = form.SelectNestMaterial(nest);

            Assert.NotNull(node);
            Assert.Same(node, Find<TreeView>(form, "_tree").SelectedNode);
            Assert.StartsWith(expected, node.Text);
            Assert.Equal("Laser", Root(node).Text);
        }));
    }

    [Fact]
    public void SelectNestMaterial_LeavesTheSelectionForAMachineNotOnFile()
    {
        RunSta(() => WithForm(form =>
        {
            var tree = Find<TreeView>(form, "_tree");
            var before = tree.SelectedNode;
            var nest = new Nest
            {
                Machine = new NestMachine(Guid.NewGuid(), "Other"),
                Material = new Material("Mild Steel", "A36"),
                Thickness = 0.5,
            };

            Assert.Null(form.SelectNestMaterial(nest));
            Assert.Same(before, tree.SelectedNode);
        }));
    }

    private static void WithForm(Action<MachineConfigForm> test)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var provider = new LocalJsonProvider(directory);
            // A machine listed first, so a default selection never passes for the nest's one.
            provider.SaveMachine(new MachineConfig
            {
                Name = "Another",
                Materials =
                {
                    new MaterialConfig
                    {
                        Name = "Mild Steel",
                        Grade = "A36",
                        Thicknesses = { new ThicknessConfig { Value = 0.5 } },
                    },
                },
            });
            provider.SaveMachine(new MachineConfig
            {
                Id = LaserId,
                Name = "Laser",
                Materials =
                {
                    new MaterialConfig
                    {
                        Name = "Mild Steel",
                        Thicknesses = { new ThicknessConfig { Value = 0.25 } },
                    },
                    new MaterialConfig
                    {
                        Name = "Mild Steel",
                        Grade = "A36",
                        Thicknesses =
                        {
                            new ThicknessConfig { Value = 0.25 },
                            new ThicknessConfig { Value = 0.5 },
                        },
                    },
                },
            });

            using var form = new MachineConfigForm(provider);
            test(form);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static TreeNode Root(TreeNode node) => node.Parent is null ? node : Root(node.Parent);

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(root.Controls.Find(name, true).Single());

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, Timeout.InfiniteTimeSpan, "The STA test did not complete.");
}
