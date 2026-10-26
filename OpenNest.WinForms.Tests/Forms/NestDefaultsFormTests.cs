using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

public class NestDefaultsFormTests
{
    [Fact]
    public void SwitchingUnitsConvertsTheShownValues()
    {
        RunSta(() =>
        {
            var machine = new MachineConfig { Name = "Laser" };
            var defaults = new NestDefaults
            {
                Units = Units.Inches,
                Size = new Size(48, 96),
                Quadrant = 3,
                PartSpacing = 0.25,
                EdgeSpacing = new Spacing(0.5, 1, 1.5, 2),
                MachineId = machine.Id,
            };
            using var form = new NestDefaultsForm(defaults, new[] { machine }, ("Saved on this PC.", false));
            Assert.Equal("in", Control<OpenNest.Controls.NumericUpDown>(form, "widthBox").Suffix);

            Control<RadioButton>(form, "millimetersRadio").Checked = true;

            var converted = form.GetDefaults();
            Assert.Equal(Units.Millimeters, converted.Units);
            Assert.Equal(new Size(1219.2, 2438.4), converted.Size);
            Assert.Equal(6.35, converted.PartSpacing);
            Assert.Equal(new Spacing(12.7, 25.4, 38.1, 50.8), converted.EdgeSpacing);
            Assert.Equal(3, converted.Quadrant);
            Assert.Equal(machine.Id, converted.MachineId);
            Assert.Equal("mm", Control<OpenNest.Controls.NumericUpDown>(form, "edgeTopBox").Suffix);

            Control<RadioButton>(form, "inchesRadio").Checked = true;
            Assert.Equal(new Size(48, 96), form.GetDefaults().Size);
        });
    }

    [Fact]
    public void ReadOnlySourceDisablesSave()
    {
        RunSta(() =>
        {
            using var form = new NestDefaultsForm(new NestDefaults(), Array.Empty<MachineConfig>(),
                ("Shared, read-only: the server cannot be reached.", true));
            Assert.False(Control<Button>(form, "saveButton").Enabled);
            Assert.Equal("Shared, read-only: the server cannot be reached.", Control<Label>(form, "sourceLabel").Text);
        });
    }

    private static T Control<T>(NestDefaultsForm form, string name) where T : Control =>
        Assert.IsType<T>(form.Controls.Find(name, true).Single());

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, Timeout.InfiniteTimeSpan, "The STA test did not complete.");
}
