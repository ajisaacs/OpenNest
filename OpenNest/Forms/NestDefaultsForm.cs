using System;
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
        public NestDefaultsForm(NestDefaults defaults)
        {
            InitializeComponent();

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
