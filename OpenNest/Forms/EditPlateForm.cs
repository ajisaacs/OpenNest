using System;
using System.IO;
using System.Windows.Forms;
using OpenNest.IO;
using Timer = System.Windows.Forms.Timer;

namespace OpenNest.Forms
{
    public partial class EditPlateForm : Form
    {
        private readonly Plate plate;
        private readonly Timer timer;

        private Units units;
        private SheetOutline outline;

        public EditPlateForm(Plate plate)
        {
            InitializeComponent();

            this.plate = plate;

            // A WinForms timer ticks on the UI thread. System.Timers.Timer's SynchronizingObject
            // cannot marshal before the dialog has a handle, so a busy UI thread (e.g. an open
            // cut simulator) let it run EnableCheck on a pool thread while the handle was created.
            timer = new Timer { Interval = SystemInformation.KeyboardDelay + 1 };
            timer.Tick += (sender, e) =>
            {
                timer.Stop();
                EnableCheck();
            };
            Disposed += (sender, e) => timer.Dispose();

            Load(plate);
            EnableCheck();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            SetUnits(units);
        }

        public Units Units
        {
            get { return units; }
            set
            {
                units = value;
                SetUnits(value);
            }
        }

        private void SetUnits(Units units)
        {
            string suffix = " " + UnitsHelper.GetShortString(units);
            decimal increment;

            if (units == Units.Inches)
                increment = 0.25m;
            else
                increment = 1;

            var controls = new[]
            {
                numericUpDownPartSpacing,
                numericUpDownEdgeSpacingBottom,
                numericUpDownEdgeSpacingLeft,
                numericUpDownEdgeSpacingRight,
                numericUpDownEdgeSpacingTop,
            };

            foreach (var control in controls)
            {
                control.Suffix = suffix;
                control.Increment = increment;
                control.Invalidate(true);
            }
        }

        public string SizeString
        {
            get { return textBoxSize.Text; }
            set { textBoxSize.Text = value; }
        }

        public double LeftSpacing
        {
            get { return (double)numericUpDownEdgeSpacingLeft.Value; }
            set { numericUpDownEdgeSpacingLeft.Value = (decimal)value; }
        }

        public double TopSpacing
        {
            get { return (double)numericUpDownEdgeSpacingTop.Value; }
            set { numericUpDownEdgeSpacingTop.Value = (decimal)value; }
        }

        public double RightSpacing
        {
            get { return (double)numericUpDownEdgeSpacingRight.Value; }
            set { numericUpDownEdgeSpacingRight.Value = (decimal)value; }
        }

        public double BottomSpacing
        {
            get { return (double)numericUpDownEdgeSpacingBottom.Value; }
            set { numericUpDownEdgeSpacingBottom.Value = (decimal)value; }
        }

        public double PartSpacing
        {
            get { return (double)numericUpDownPartSpacing.Value; }
            set { numericUpDownPartSpacing.Value = (decimal)value; }
        }

        public int Quantity
        {
            get { return (int)numericUpDownQty.Value; }
            set { numericUpDownQty.Value = value; }
        }

        public int Quadrant
        {
            get { return quadrantSelect1.Quadrant; }
            set { quadrantSelect1.Quadrant = value; }
        }

        /// <summary>
        /// The sheet outline the plate gets when the dialog is accepted: the plate's own until
        /// one is imported or cleared. An outlined sheet's size is its outline's bounds, so the
        /// size box is read-only while there is one.
        /// </summary>
        public SheetOutline Outline
        {
            get { return outline; }
            set
            {
                outline = value;
                textBoxSize.ReadOnly = value != null;
                buttonClearOutline.Enabled = value != null;
                if (value != null)
                    textBoxSize.Text = value.Size.ToString();
                labelOutline.Text = value == null
                    ? "Rectangular sheet"
                    : $"{value.Points.Count} vertices" + (value.Obstacles.Count == 0 ? "" : $", {value.Obstacles.Count} obstacle(s)");
                EnableCheck();
            }
        }

        public void EnableCheck()
        {
            OpenNest.Geometry.Size size;

            if (!OpenNest.Geometry.Size.TryParse(SizeString, out size))
            {
                applyButton.Enabled = false;
                return;
            }

            if (LeftSpacing + RightSpacing >= size.Length)
            {
                applyButton.Enabled = false;
                return;
            }

            if (TopSpacing + BottomSpacing >= size.Width)
            {
                applyButton.Enabled = false;
                return;
            }

            applyButton.Enabled = true;
        }

        private new void Load(Plate plate)
        {
            textBoxSize.Text = plate.Size.ToString();
            LeftSpacing = plate.EdgeSpacing.Left;
            TopSpacing = plate.EdgeSpacing.Top;
            RightSpacing = plate.EdgeSpacing.Right;
            BottomSpacing = plate.EdgeSpacing.Bottom;
            PartSpacing = plate.PartSpacing;
            Quantity = plate.Quantity;
            Quadrant = plate.Quadrant;
            Outline = plate.Outline;
        }

        /// <summary>Writes the dialog's values to the plate, as Done does.</summary>
        internal void Apply() => Save();

        private void Save()
        {
            // The outline carries its own size; a parsed size would drop it.
            if (Outline != null)
                plate.Outline = Outline;
            else
                plate.Size = OpenNest.Geometry.Size.Parse(SizeString);
            if (Outline == null)
                plate.Outline = null;
            plate.EdgeSpacing.Left = LeftSpacing;
            plate.EdgeSpacing.Top = TopSpacing;
            plate.EdgeSpacing.Right = RightSpacing;
            plate.EdgeSpacing.Bottom = BottomSpacing;
            plate.PartSpacing = PartSpacing;
            plate.Quantity = Quantity;
            plate.Quadrant = Quadrant;
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            timer.Stop();
            timer.Start();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Save();
        }

        private void spacing_ValueChanged(object sender, EventArgs e)
        {
            EnableCheck();
        }

        /// <summary>
        /// Reads a sheet outline from a CAD file: the largest closed contour is the sheet and
        /// every other closed contour an obstacle, in the file's units (as drawings import).
        /// </summary>
        private void buttonImportOutline_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Import Sheet Outline",
                Filter = CadFormats.OpenFileFilter,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                var options = new CadImportOptions { DrawingUnits = Units };
                if (!SvgScaleForm.TryResolve(this, dialog.FileName, options))
                    return;
                Outline = SheetOutline.FromGeometry(CadImporter.Import(dialog.FileName, options).Entities);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException or InvalidDataException)
            {
                MessageBox.Show(this, $"The file does not hold a sheet outline: {exception.Message}",
                    "Import Sheet Outline", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void buttonClearOutline_Click(object sender, EventArgs e)
        {
            Outline = null;
        }
    }
}
