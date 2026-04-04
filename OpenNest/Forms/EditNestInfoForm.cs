using OpenNest.Geometry;
using System;
using System.Windows.Forms;
using Timer = System.Timers.Timer;

namespace OpenNest.Forms
{
    public partial class EditNestInfoForm : Form
    {
        private readonly Timer timer;
        private DateTime dateCreated;
        private DateTime dateLastModified;

        public EditNestInfoForm()
        {
            InitializeComponent();

            timer = new Timer
            {
                SynchronizingObject = this,
                Enabled = true,
                AutoReset = false,
                Interval = SystemInformation.KeyboardDelay + 1
            };
            timer.Elapsed += (sender, e) => EnableCheck();
            EnableCheck();
        }

        public string NestName
        {
            get { return nameBox.Text; }
            private set { nameBox.Text = value; }
        }

        public string Customer
        {
            get { return customerBox.Text; }
            set { customerBox.Text = value; }
        }

        public string Notes
        {
            get { return notesBox.Text; }
            set { notesBox.Text = value; }
        }

        public string SizeString
        {
            get { return sizeBox.Text; }
            set { sizeBox.Text = value; }
        }

        public DateTime DateCreated
        {
            get { return dateCreated; }
            set
            {
                dateCreated = value;
                textBox1.Text = dateCreated.ToShortDateString();
            }
        }

        public DateTime DateLastModified
        {
            get { return dateLastModified; }
            set
            {
                dateLastModified = value;
                textBox2.Text = dateLastModified.ToShortDateString();
            }
        }

        public double LeftSpacing
        {
            get { return (double)leftSpacingBox.Value; }
            set { leftSpacingBox.Value = (decimal)value; }
        }

        public double TopSpacing
        {
            get { return (double)topSpacingBox.Value; }
            set { topSpacingBox.Value = (decimal)value; }
        }

        public double RightSpacing
        {
            get { return (double)rightSpacingBox.Value; }
            set { rightSpacingBox.Value = (decimal)value; }
        }

        public double BottomSpacing
        {
            get { return (double)bottomSpacingBox.Value; }
            set { bottomSpacingBox.Value = (decimal)value; }
        }

        public double PartSpacing
        {
            get { return (double)partSpacingBox.Value; }
            set { partSpacingBox.Value = (decimal)value; }
        }

        public double Thickness
        {
            get { return (double)thicknessBox.Value; }
            set { thicknessBox.Value = (decimal)value; }
        }

        public string MaterialName
        {
            get { return materialBox.Text; }
            set { materialBox.Text = value; }
        }

        public void SetUnits(Units units)
        {
            switch (units)
            {
                case OpenNest.Units.Inches:
                    radioButton1.Checked = true;
                    break;

                case OpenNest.Units.Millimeters:
                    radioButton2.Checked = true;
                    break;
            }

            UpdateUnitSuffix();
        }

        public Units GetUnits()
        {
            if (radioButton1.Checked)
                return Units.Inches;

            if (radioButton2.Checked)
                return Units.Millimeters;

            return OpenNest.Units.Inches;
        }

        private void UpdateUnitSuffix()
        {
            var unitBoxes = new Controls.NumericUpDown[]
            {
                thicknessBox,
                partSpacingBox,
                leftSpacingBox,
                topSpacingBox,
                rightSpacingBox,
                bottomSpacingBox
            };

            var unitString = " " + UnitsHelper.GetShortString(GetUnits());

            foreach (var box in unitBoxes)
                box.Suffix = unitString;
        }

        public int Quadrant
        {
            get { return quadrantSelect1.Quadrant; }
            set { quadrantSelect1.Quadrant = value; }
        }

        public void EnableCheck()
        {
            Size size;

            if (!OpenNest.Geometry.Size.TryParse(sizeBox.Text, out size))
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

        public void LoadNestInfo(Nest nest)
        {
            NestName = nest.Name;
            Notes = nest.Notes;
            Customer = nest.Customer;
            DateCreated = nest.DateCreated;
            DateLastModified = nest.DateLastModified;
            Thickness = nest.Thickness;
            MaterialName = nest.Material?.Name ?? "";
            SizeString = nest.PlateDefaults.Size.ToString();
            PartSpacing = nest.PlateDefaults.PartSpacing;
            LeftSpacing = nest.PlateDefaults.EdgeSpacing.Left;
            TopSpacing = nest.PlateDefaults.EdgeSpacing.Top;
            RightSpacing = nest.PlateDefaults.EdgeSpacing.Right;
            BottomSpacing = nest.PlateDefaults.EdgeSpacing.Bottom;
            Quadrant = nest.PlateDefaults.Quadrant;

            SetUnits(nest.Units);
        }

        public void SaveNestInfo(Nest nest)
        {
            nest.Name = NestName;
            nest.Units = GetUnits();
            nest.Notes = Notes;
            nest.Customer = Customer;
            nest.DateCreated = DateCreated;
            nest.DateLastModified = DateLastModified;
            nest.Thickness = Thickness;
            nest.Material = new Material(MaterialName);
            nest.PlateDefaults.Size = OpenNest.Geometry.Size.Parse(SizeString);
            nest.PlateDefaults.PartSpacing = PartSpacing;
            nest.PlateDefaults.EdgeSpacing = new Spacing(LeftSpacing, BottomSpacing, RightSpacing, TopSpacing);
            nest.PlateDefaults.Quadrant = Quadrant;
        }

        private void sizeBox_TextChanged(object sender, System.EventArgs e)
        {
            timer.Stop();
            timer.Start();
        }

        private void Units_Changed(object sender, EventArgs e)
        {
            var radioButton = sender as RadioButton;

            if (radioButton == null || !radioButton.Checked)
                return;

            UpdateUnitSuffix();
        }
    }
}
