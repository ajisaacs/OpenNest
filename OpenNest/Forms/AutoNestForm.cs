using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class AutoNestForm : Form
    {
        public AutoNestForm(Nest nest)
        {
            InitializeComponent();
            LoadDrawings(nest);

            dataGridView1.DataError += dataGridView1_DataError;
            LoadDefaultPlateOptions();
        }

        public bool AllowPlateCreation
        {
            get { return createNewPlatesAsNeededBox.Checked; }
            set { createNewPlatesAsNeededBox.Checked = value; }
        }

        public bool OptimizePlateSize
        {
            get { return optimizePlateSizeBox.Checked; }
            set { optimizePlateSizeBox.Checked = value; }
        }

        public double SalvageRate
        {
            get
            {
                if (double.TryParse(salvageRateBox.Text, out var val))
                    return System.Math.Clamp(val / 100.0, 0, 1);
                return 0.5;
            }
            set { salvageRateBox.Text = (value * 100).ToString("F0"); }
        }

        public List<PlateOption> GetPlateOptions()
        {
            var result = new List<PlateOption>();
            var gridItems = plateOptionsGrid.DataSource as List<PlateOptionItem>;
            if (gridItems == null) return result;

            foreach (var item in gridItems)
            {
                if (item.Width <= 0 || item.Length <= 0) continue;
                result.Add(new PlateOption
                {
                    Width = item.Width,
                    Length = item.Length,
                    Cost = item.Cost,
                });
            }

            return result;
        }

        private void LoadDefaultPlateOptions()
        {
            var items = new List<PlateOptionItem>
            {
                new() { Width = 48, Length = 96, Cost = 0 },
                new() { Width = 48, Length = 120, Cost = 0 },
                new() { Width = 48, Length = 144, Cost = 0 },
                new() { Width = 60, Length = 96, Cost = 0 },
                new() { Width = 60, Length = 120, Cost = 0 },
                new() { Width = 60, Length = 144, Cost = 0 },
                new() { Width = 72, Length = 96, Cost = 0 },
                new() { Width = 72, Length = 120, Cost = 0 },
                new() { Width = 72, Length = 144, Cost = 0 },
            };
            plateOptionsGrid.DataSource = items;
        }

        private void optimizePlateSizeBox_CheckedChanged(object sender, EventArgs e)
        {
            plateOptionsPanel.Visible = optimizePlateSizeBox.Checked;
        }

        internal class PlateOptionItem
        {
            public double Width { get; set; }
            public double Length { get; set; }
            public double Cost { get; set; }
        }

        private void LoadDrawings(Nest nest)
        {
            var items = new List<DataGridViewItem>();
            dataGridView1.Rows.Clear();

            foreach (var drawing in nest.Drawings)
                items.Add(GetDataGridViewItem(drawing));

            dataGridView1.DataSource = items;
        }

        public List<NestItem> GetNestItems()
        {
            var nestItems = new List<NestItem>();
            var gridItems = dataGridView1.DataSource as List<DataGridViewItem>;

            if (gridItems == null)
                return nestItems;

            foreach (var gridItem in gridItems)
            {
                if (gridItem.Quantity < 1)
                    continue;

                var nestItem = new NestItem();
                nestItem.Drawing = gridItem.RefDrawing;
                nestItem.Priority = gridItem.Priority;
                nestItem.Quantity = gridItem.Quantity;
                nestItem.RotationEnd = gridItem.RotationEnd;
                nestItem.RotationStart = gridItem.RotationStart;
                nestItem.StepAngle = gridItem.StepAngle;

                nestItems.Add(nestItem);
            }

            return nestItems;
        }

        private DataGridViewItem GetDataGridViewItem(Drawing dwg)
        {
            var item = new DataGridViewItem();
            item.RefDrawing = dwg;
            item.Quantity = dwg.Quantity.Remaining > 0 ? dwg.Quantity.Remaining : 0;
            item.Priority = dwg.Priority;
            item.RotationStart = dwg.Constraints.StartAngle;
            item.RotationEnd = dwg.Constraints.EndAngle;
            item.StepAngle = dwg.Constraints.StepAngle;

            return item;
        }

        private void dataGridView1_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            MessageBox.Show("Invalid input. Expected input type is " + dataGridView1[e.ColumnIndex, e.RowIndex].ValueType.Name);
        }

        private class DataGridViewItem
        {
            internal Drawing RefDrawing { get; set; }

            [ReadOnly(true)]
            [DisplayName("Drawing Name")]
            public string DrawingName
            {
                get { return RefDrawing.Name; }
                set { RefDrawing.Name = value; }
            }

            public int Quantity { get; set; }

            public int Priority { get; set; }

            [Browsable(false)] // hide until implemented
            [DisplayName("Rotation Start")]
            public double RotationStart { get; set; }

            [Browsable(false)] // hide until implemented
            [DisplayName("Rotation End")]
            public double RotationEnd { get; set; }

            [Browsable(false)] // hide until implemented
            [DisplayName("Step Angle")]
            public double StepAngle { get; set; }
        }
    }
}
