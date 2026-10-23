using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Forms
{
    /// <summary>
    /// Edits the persisted nest defaults (Tools &gt; Nest Defaults), written
    /// to the JSON file used when creating a new nest.
    /// </summary>
    public partial class NestDefaultsForm : Form
    {
        private sealed record MachineChoice(MachineConfig Machine)
        {
            public override string ToString() => Machine.Name;
        }

        private readonly Guid? savedMachineId;

        /// <param name="machines">The machines new nests can be assigned.</param>
        public NestDefaultsForm(NestDefaults defaults, IEnumerable<MachineConfig> machines)
        {
            InitializeComponent();

            savedMachineId = defaults.MachineId;
            var sorted = machines.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var machine in sorted)
                machineCombo.Items.Add(new MachineChoice(machine));
            var selected = MachineSettings.DefaultMachine(defaults, sorted);
            machineCombo.SelectedItem = machineCombo.Items.Cast<MachineChoice>().FirstOrDefault(c => c.Machine == selected);
            machineCombo.Enabled = machineCombo.Items.Count > 0;

            unitsCombo.SelectedIndex = defaults.Units == Units.Millimeters ? 1 : 0;
            widthBox.Value = Clamp(widthBox, defaults.Size.Width);
            lengthBox.Value = Clamp(lengthBox, defaults.Size.Length);
            partSpacingBox.Value = Clamp(partSpacingBox, defaults.PartSpacing);
            edgeLeftBox.Value = Clamp(edgeLeftBox, defaults.EdgeSpacing.Left);
            edgeBottomBox.Value = Clamp(edgeBottomBox, defaults.EdgeSpacing.Bottom);
            edgeRightBox.Value = Clamp(edgeRightBox, defaults.EdgeSpacing.Right);
            edgeTopBox.Value = Clamp(edgeTopBox, defaults.EdgeSpacing.Top);
            quadrantSelect1.Quadrant = defaults.Quadrant;
        }

        public Units Units => unitsCombo.SelectedIndex == 1 ? Units.Millimeters : Units.Inches;

        public NestDefaults GetDefaults()
        {
            return new NestDefaults
            {
                Units = Units,
                Size = new Size((double)widthBox.Value, (double)lengthBox.Value),
                Quadrant = quadrantSelect1.Quadrant,
                PartSpacing = (double)partSpacingBox.Value,
                EdgeSpacing = new Spacing(
                    (double)edgeLeftBox.Value,
                    (double)edgeBottomBox.Value,
                    (double)edgeRightBox.Value,
                    (double)edgeTopBox.Value
                ),
                // With no machines on file there is nothing to choose, so keep the saved choice.
                MachineId = (machineCombo.SelectedItem as MachineChoice)?.Machine.Id ?? savedMachineId,
            };
        }

        private static decimal Clamp(NumericUpDown box, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return box.Minimum;

            if (value < (double)box.Minimum)
                return box.Minimum;

            if (value > (double)box.Maximum)
                return box.Maximum;

            return (decimal)value;
        }
    }
}
