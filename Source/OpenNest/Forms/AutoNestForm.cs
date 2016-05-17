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
        }

        public bool AllowPlateCreation
        {
            get { return createNewPlatesAsNeededBox.Checked; }
            set { createNewPlatesAsNeededBox.Checked = value; }
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
