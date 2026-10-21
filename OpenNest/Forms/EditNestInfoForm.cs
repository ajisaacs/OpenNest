using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Geometry;
using Timer = System.Windows.Forms.Timer;

namespace OpenNest.Forms
{
    public partial class EditNestInfoForm : Form
    {
        private readonly Timer timer;
        private DateTime dateCreated;
        private DateTime dateLastModified;
        private List<MaterialConfig> materialCatalog = new();
        private string gradeMaterialKey = "";

        public EditNestInfoForm()
        {
            InitializeComponent();

            foreach (var name in PostProcessorMaterials.Names)
                materialBox.Items.Add(name);

            foreach (var status in NestStatuses.WorkflowOrder)
                statusBox.Items.Add(status);
            statusBox.SelectedItem = NestStatus.Quote;

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
            nameBox.TextChanged += (sender, e) => EnableCheck();
            materialBox.TextChanged += (sender, e) => RefreshGrades();
            EnableCheck();
        }

        public string NestName
        {
            get { return nameBox.Text.Trim(); }
            private set { nameBox.Text = value; }
        }

        /// <summary>
        /// Database mode only: in File mode the name is the file name, set by Save As.
        /// </summary>
        public bool AllowRename
        {
            get { return !nameBox.ReadOnly; }
            set { nameBox.ReadOnly = !value; }
        }

        /// <summary>
        /// The drawing set release this nest came from (Database mode), shown beside the
        /// buttons; blank for none.
        /// </summary>
        public string ReleaseOrigin
        {
            get { return releaseLabel.Text; }
            set { releaseLabel.Text = value ?? ""; }
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

        public string MaterialGrade
        {
            get { return gradeBox.Text; }
            set { gradeBox.Text = value; }
        }

        /// <summary>
        /// Offers the machine configurations' materials: their names join the material
        /// list, and the grade list shows the grades on file for the chosen material.
        /// </summary>
        public void SetMaterialCatalog(IEnumerable<MaterialConfig> materials)
        {
            materialCatalog = materials.ToList();

            var text = materialBox.Text;
            var names = materialBox.Items.Cast<string>()
                .Concat(materialCatalog.Select(m => m.Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(SharedListNames.Key)
                .Select(group => group.First())
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            materialBox.Items.Clear();
            materialBox.Items.AddRange(names);
            materialBox.Text = text;
            RefreshGrades();
        }

        /// <summary>
        /// Lists the grades on file for the typed material. A grade belongs to its
        /// material, so changing the material clears a grade the new one does not have.
        /// </summary>
        private void RefreshGrades()
        {
            var text = gradeBox.Text;
            var key = SharedListNames.Key(materialBox.Text);
            var grades = materialCatalog
                .Where(m => SharedListNames.Key(m.Name) == key && !string.IsNullOrWhiteSpace(m.Grade))
                .Select(m => m.Grade)
                .GroupBy(SharedListNames.Key)
                .Select(group => group.First())
                .OrderBy(grade => grade, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (key != gradeMaterialKey && !grades.Any(grade => SharedListNames.Key(grade) == SharedListNames.Key(text)))
                text = "";
            gradeMaterialKey = key;
            gradeBox.Items.Clear();
            gradeBox.Items.AddRange(grades);
            gradeBox.Text = text;
        }

        public NestStatus Status
        {
            get { return statusBox.SelectedItem is NestStatus status ? status : NestStatus.Quote; }
            set { statusBox.SelectedItem = value; }
        }

        public string MadeBy
        {
            get { return madeByBox.Text; }
            set { madeByBox.Text = value; }
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
                bottomSpacingBox,
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

            if (NestName.Length == 0)
            {
                applyButton.Enabled = false;
                return;
            }

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
            MaterialGrade = nest.Material?.Grade ?? "";
            Status = nest.Status;
            MadeBy = nest.MadeBy ?? "";
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
            nest.Material = NestMaterialSelection.Apply(nest.Material, MaterialName, MaterialGrade);
            nest.Status = Status;
            nest.MadeBy = MadeBy;
            nest.PlateDefaults.Size = OpenNest.Geometry.Size.Parse(SizeString);
            nest.PlateDefaults.PartSpacing = PartSpacing;
            nest.PlateDefaults.EdgeSpacing = new Spacing(
                LeftSpacing,
                BottomSpacing,
                RightSpacing,
                TopSpacing
            );
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
