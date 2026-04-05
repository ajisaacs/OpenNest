using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OpenNest.Engine;

namespace OpenNest.Forms
{
    public partial class AutoNestForm : Form
    {
        private static readonly Regex SizePattern = new(@"^(\d+\.?\d*)\s*[xX×]\s*(\d+\.?\d*)$");

        public AutoNestForm(Nest nest)
        {
            InitializeComponent();
            SetupPartsGrid();
            SetupPlateGrid();
            LoadEngines();
            LoadDrawings(nest);
            LoadDefaultPlateOptions();
            SetPlateOptimizerVisible(false);

            partsGrid.DataError += PartsGrid_DataError;
        }

        public string EngineName
        {
            get { return engineComboBox.SelectedItem as string; }
            set { engineComboBox.SelectedItem = value; }
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

        private void LoadEngines()
        {
            foreach (var engine in NestEngineRegistry.AvailableEngines)
                engineComboBox.Items.Add(engine.Name);
            engineComboBox.SelectedItem = NestEngineRegistry.ActiveEngineName;
        }

        private void SetupPartsGrid()
        {
            partsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "DrawingName",
                HeaderText = "Drawing Name",
                Width = 160,
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            partsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Quantity",
                HeaderText = "Qty",
                Width = 50,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            partsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Priority",
                HeaderText = "Priority",
                Width = 55,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            partsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "RotationStart",
                HeaderText = "Rot Start",
                Width = 65,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            partsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "RotationEnd",
                HeaderText = "Rot End",
                Width = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            partsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "StepAngle",
                HeaderText = "Step",
                Width = 55,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });

            partsGrid.CellValueChanged += PartsGrid_CellValueChanged;
            partsGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (partsGrid.IsCurrentCellDirty)
                    partsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
        }

        private void SetupPlateGrid()
        {
            plateGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Size",
                HeaderText = "Size",
                Width = 120,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            plateGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Cost",
                HeaderText = "Cost",
                Width = 70,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });

            plateGrid.CellValidating += PlateGrid_CellValidating;
        }

        private void LoadDrawings(Nest nest)
        {
            var items = new List<DataGridViewItem>();

            foreach (var drawing in nest.Drawings)
                items.Add(GetDataGridViewItem(drawing));

            partsGrid.DataSource = items;
            UpdateSummary();
        }

        public List<NestItem> GetNestItems()
        {
            var nestItems = new List<NestItem>();
            var gridItems = partsGrid.DataSource as List<DataGridViewItem>;

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

        public List<PlateOption> GetPlateOptions()
        {
            var result = new List<PlateOption>();
            var gridItems = plateGrid.DataSource as List<PlateOptionItem>;
            if (gridItems == null) return result;

            foreach (var item in gridItems)
            {
                if (!TryParseSize(item.Size, out var width, out var length))
                    continue;
                if (width <= 0 || length <= 0)
                    continue;

                result.Add(new PlateOption
                {
                    Width = width,
                    Length = length,
                    Cost = item.Cost,
                });
            }

            return result;
        }

        public void LoadPlateOptions(List<PlateOption> options, double salvageRate)
        {
            if (options != null && options.Count > 0)
            {
                var items = options.Select(o => new PlateOptionItem
                {
                    Size = FormatSize(o.Width, o.Length),
                    Cost = o.Cost,
                }).ToList();
                plateGrid.DataSource = items;
                optimizePlateSizeBox.Checked = true;
            }
            SalvageRate = salvageRate;
        }

        private void LoadDefaultPlateOptions()
        {
            var items = new List<PlateOptionItem>
            {
                new() { Size = "48 x 96", Cost = 0 },
                new() { Size = "48 x 120", Cost = 0 },
                new() { Size = "48 x 144", Cost = 0 },
                new() { Size = "60 x 96", Cost = 0 },
                new() { Size = "60 x 120", Cost = 0 },
                new() { Size = "60 x 144", Cost = 0 },
                new() { Size = "72 x 96", Cost = 0 },
                new() { Size = "72 x 120", Cost = 0 },
                new() { Size = "72 x 144", Cost = 0 },
            };
            plateGrid.DataSource = items;
        }

        private void optimizePlateSizeBox_CheckedChanged(object sender, EventArgs e)
        {
            SetPlateOptimizerVisible(optimizePlateSizeBox.Checked);
        }

        private void SetPlateOptimizerVisible(bool visible)
        {
            plateGrid.Visible = visible;
            salvageRateLabel.Visible = visible;
            salvageRateBox.Visible = visible;
            salvageRatePercentLabel.Visible = visible;
        }

        private void UpdateSummary()
        {
            var gridItems = partsGrid.DataSource as List<DataGridViewItem>;
            if (gridItems == null)
            {
                summaryLabel.Text = "";
                return;
            }

            var totalQty = gridItems.Sum(i => System.Math.Max(0, i.Quantity));
            var drawingCount = gridItems.Count(i => i.Quantity > 0);
            summaryLabel.Text = $"{totalQty} parts across {drawingCount} drawings";
        }

        private void PartsGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (partsGrid.Columns[e.ColumnIndex].DataPropertyName == "Quantity")
                UpdateSummary();
        }

        private void PlateGrid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (plateGrid.Columns[e.ColumnIndex].DataPropertyName != "Size")
                return;

            var value = e.FormattedValue?.ToString();
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (!TryParseSize(value, out _, out _))
            {
                e.Cancel = true;
                plateGrid.Rows[e.RowIndex].ErrorText = "Enter size as W x L (e.g. 48 x 96)";
            }
            else
            {
                plateGrid.Rows[e.RowIndex].ErrorText = "";
            }
        }

        private static bool TryParseSize(string value, out double width, out double length)
        {
            width = 0;
            length = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var match = SizePattern.Match(value.Trim());
            if (!match.Success) return false;
            width = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            length = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        private static string FormatSize(double width, double length)
        {
            return $"{width:G} x {length:G}";
        }

        private void PartsGrid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            MessageBox.Show("Invalid input. Expected input type is " +
                partsGrid[e.ColumnIndex, e.RowIndex].ValueType.Name);
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

            [DisplayName("Rot Start")]
            public double RotationStart { get; set; }

            [DisplayName("Rot End")]
            public double RotationEnd { get; set; }

            [DisplayName("Step")]
            public double StepAngle { get; set; }
        }

        private class PlateOptionItem
        {
            public string Size { get; set; }
            public double Cost { get; set; }
        }
    }
}
