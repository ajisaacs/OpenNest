using System;
using System.Windows.Forms;
using OpenNest.Geometry;
using Timer = System.Timers.Timer;

namespace OpenNest.Forms
{
    public partial class EditPlateForm : Form
    {
        private readonly Plate plate;
        private readonly Timer timer;

        private Units units;

        public EditPlateForm(Plate plate)
        {
            InitializeComponent();
            
            this.plate = plate;

            timer = new Timer
            {
                SynchronizingObject = this,
                Enabled = true,
                AutoReset = false,
                Interval = SystemInformation.KeyboardDelay + 1
            };
            timer.Elapsed += (sender, e) => EnableCheck();

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

            var controls = new []
            {
                numericUpDownThickness,
                numericUpDownPartSpacing,
                numericUpDownEdgeSpacingBottom,
                numericUpDownEdgeSpacingLeft,
                numericUpDownEdgeSpacingRight,
                numericUpDownEdgeSpacingTop
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

        public double Thickness
        {
            get { return (double)numericUpDownThickness.Value; }
            set { numericUpDownThickness.Value = (decimal)value; }
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

        public void EnableCheck()
        {
            OpenNest.Geometry.Size size;

            if (!OpenNest.Geometry.Size.TryParse(SizeString, out size))
            {
                applyButton.Enabled = false;
                return;
            }

            if (LeftSpacing + RightSpacing >= size.Width)
            {
                applyButton.Enabled = false;
                return;
            }

            if (TopSpacing + BottomSpacing >= size.Height)
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
            Thickness = plate.Thickness;
        }

        private void Save()
        {
            plate.Size = OpenNest.Geometry.Size.Parse(SizeString);
            plate.EdgeSpacing.Left = LeftSpacing;
            plate.EdgeSpacing.Top = TopSpacing;
            plate.EdgeSpacing.Right = RightSpacing;
            plate.EdgeSpacing.Bottom = BottomSpacing;
            plate.PartSpacing = PartSpacing;
            plate.Quantity = Quantity;
            plate.Quadrant = Quadrant;
            plate.Thickness = Thickness;
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
    }
}
