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
        private static readonly Regex SizePattern = new(
            @"^(\d+\.?\d*)\s*[xX×]\s*(\d+\.?\d*)$",
            RegexOptions.None,
            TimeSpan.FromMilliseconds(250)
        );

        public AutoNestForm(Nest nest)
        {
            InitializeComponent();
            cancelButton.CausesValidation = false;
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

        public double MinRemnantSize
        {
            get
            {
                if (double.TryParse(minRemnantBox.Text, out var val) && val > 0)
                    return val;
                return 12.0;
            }
        }

        private void LoadEngines()
        {
            foreach (var name in EngineSelection.UiEngineNames)
                engineComboBox.Items.Add(name);
            engineComboBox.SelectedItem = EngineSelection.EngineName;
        }

        private void SetupPartsGrid()
        {
            partsGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "DrawingName",
                    HeaderText = "Drawing Name",
                    Width = 160,
                    ReadOnly = true,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );
            partsGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "Quantity",
                    HeaderText = "Qty",
                    Width = 50,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );
            partsGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "Priority",
                    HeaderText = "Priority",
                    Width = 55,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );
            partsGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "RotationStart",
                    HeaderText = "Rot Start",
                    Width = 65,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );
            partsGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "RotationEnd",
                    HeaderText = "Rot End",
                    Width = 60,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );
            partsGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "StepAngle",
                    HeaderText = "Step",
                    Width = 55,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );

            partsGrid.CellValueChanged += PartsGrid_CellValueChanged;
            partsGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (partsGrid.IsCurrentCellDirty)
                    partsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
        }

        private void SetupPlateGrid()
        {
            plateGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "Size",
                    HeaderText = "Size",
                    Width = 120,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );
            plateGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "Cost",
                    HeaderText = "Cost",
                    ToolTipText = "Per physical sheet, in one common unit. Use zero on every row for area scoring, or positive costs on every row. Salvage credits cost proportionally; set salvage to zero for purchase totals.",
                    Width = 70,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );

            plateGrid.CellValidating += PlateGrid_CellValidating;
            plateGrid.DataError += PlateGrid_DataError;
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

        /// <summary>
        /// Returns the validated stock collection. Throws <see cref="FormatException"/> when a
        /// nonblank row is invalid or timed out; all-or-nothing, never a valid prefix. Prefer
        /// <see cref="TryGetPlateOptions"/> for callers that must report the failure.
        /// </summary>
        public List<PlateOption> GetPlateOptions()
        {
            if (!TryGetPlateOptions(out var options, out var error))
                throw new FormatException(error);
            return options;
        }

        public bool TryGetPlateOptions(out List<PlateOption> options, out string error)
        {
            options = new List<PlateOption>();
            error = null;

            // CellValidating cancels an unparsable size and leaves the cell in edit mode;
            // calling EndEdit() on that cancelled edit hangs/throws instead of returning
            // false for it. Catch an unparsable pending edit ourselves first so EndEdit()
            // is never asked to commit or discard it.
            if (
                plateGrid.IsCurrentCellInEditMode
                && plateGrid.CurrentCell != null
                && plateGrid.Columns[plateGrid.CurrentCell.ColumnIndex].DataPropertyName == "Size"
                && plateGrid.EditingControl != null
            )
            {
                var pending = plateGrid.EditingControl.Text;
                if (!string.IsNullOrWhiteSpace(pending) && !TryParseSize(pending, out _, out _))
                {
                    error = $"Invalid stock size '{Preview(pending)}'. Enter positive dimensions as W x L.";
                    return false;
                }
            }

            if (plateGrid.IsCurrentCellInEditMode && plateGrid.CurrentCell != null
                && plateGrid.Columns[plateGrid.CurrentCell.ColumnIndex].DataPropertyName == "Cost"
                && plateGrid.EditingControl != null
                && !ValidCost(plateGrid.EditingControl.Text))
            {
                error = $"Invalid stock cost in row {plateGrid.CurrentCell.RowIndex + 1}. Enter a finite positive cost, or zero on every row.";
                return false;
            }

            bool committed;
            try
            {
                committed = plateGrid.EndEdit();
            }
            catch (InvalidOperationException)
            {
                committed = false;
            }
            if (!committed)
            {
                var value = plateGrid.EditingControl?.Text ?? plateGrid.CurrentCell?.Value?.ToString();
                error = $"Invalid stock size '{Preview(value)}'. Enter positive dimensions as W x L.";
                return false;
            }

            var gridItems = plateGrid.DataSource as BindingList<PlateOptionItem>;
            if (gridItems == null)
                return true;

            var validated = new List<PlateOption>();
            for (var index = 0; index < gridItems.Count; index++)
            {
                var item = gridItems[index];
                if (string.IsNullOrWhiteSpace(item.Size))
                    continue;
                if (!TryParseSize(item.Size, out var width, out var length))
                {
                    error = $"Invalid stock size '{Preview(item.Size)}' in row {index + 1}. Enter positive dimensions as W x L.";
                    if (index < plateGrid.Rows.Count)
                        plateGrid.Rows[index].ErrorText = error;
                    return false;
                }

                if (!double.IsFinite(item.Cost) || item.Cost < 0
                    || validated.Count > 0 && (validated[0].Cost == 0) != (item.Cost == 0))
                {
                    error = $"Invalid stock cost in row {index + 1}. Use finite positive costs on every active row, or zero on every row.";
                    if (index < plateGrid.Rows.Count)
                        plateGrid.Rows[index].ErrorText = error;
                    return false;
                }
                if (index < plateGrid.Rows.Count)
                    plateGrid.Rows[index].ErrorText = "";
                validated.Add(new PlateOption { Width = width, Length = length, Cost = item.Cost });
            }

            // Publish only a completely validated collection, never a valid prefix.
            options = validated;
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK && OptimizePlateSize
                && !TryGetPlateOptions(out _, out var error))
            {
                e.Cancel = true;
                DialogResult = DialogResult.None;
                ReportStockValidationFailure(error);
            }
            base.OnFormClosing(e);
        }

        internal virtual void ReportStockValidationFailure(string error)
        {
            MessageBox.Show(this, error, "Auto Nest", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Bounded single-line preview so a long or multiline cell value cannot produce an
        // unreadable message box; the full value stays in the editable cell.
        private static string Preview(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            var line = value.Replace("\r", " ").Replace("\n", " ");
            return line.Length <= 40 ? line : line[..40] + "...";
        }

        public void LoadPlateOptions(List<PlateOption> options, double salvageRate)
        {
            if (options != null && options.Count > 0)
            {
                var items = options
                    .Select(o => new PlateOptionItem
                    {
                        Size = FormatSize(o.Width, o.Length),
                        Cost = o.Cost,
                    })
                    .ToList();
                plateGrid.DataSource = new BindingList<PlateOptionItem>(items);
                optimizePlateSizeBox.Checked = true;
            }
            SalvageRate = salvageRate;
        }

        private void LoadDefaultPlateOptions()
        {
            // A bound DataGridView needs IBindingList.AddNew support for its blank last row.
            var items = new BindingList<PlateOptionItem>
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
            minRemnantLabel.Visible = visible;
            minRemnantBox.Visible = visible;
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
            if (e.RowIndex < 0)
                return;
            if (partsGrid.Columns[e.ColumnIndex].DataPropertyName == "Quantity")
                UpdateSummary();
        }

        private static bool ValidCost(string text) =>
            double.TryParse(text, out var cost) && double.IsFinite(cost) && cost >= 0;

        private void PlateGrid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (plateGrid.Columns[e.ColumnIndex].DataPropertyName == "Cost")
            {
                e.Cancel = !plateGrid.Rows[e.RowIndex].IsNewRow && !ValidCost(e.FormattedValue?.ToString());
                plateGrid.Rows[e.RowIndex].ErrorText = e.Cancel ? "Enter a finite positive cost, or zero on every row." : "";
                return;
            }
            if (plateGrid.Columns[e.ColumnIndex].DataPropertyName != "Size")
                return;

            var value = e.FormattedValue?.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                plateGrid.Rows[e.RowIndex].ErrorText = "";
                return;
            }

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

        private bool TryParseSize(string value, out double width, out double length)
        {
            width = 0;
            length = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            try
            {
                var match = MatchStockSize(value.Trim());
                if (!match.Success)
                    return false;
                var parsedWidth = double.Parse(
                    match.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture
                );
                var parsedLength = double.Parse(
                    match.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture
                );
                if (!double.IsFinite(parsedWidth) || !double.IsFinite(parsedLength)
                    || parsedWidth <= 0 || parsedLength <= 0)
                    return false;
                width = parsedWidth;
                length = parsedLength;
                return true;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        internal virtual Match MatchStockSize(string value) => SizePattern.Match(value);

        private static string FormatSize(double width, double length)
        {
            // Invariant: TryParseSize requires invariant decimal notation; current-culture
            // formatting would produce comma decimals that the parser then rejects.
            return FormattableString.Invariant($"{width:G} x {length:G}");
        }

        private void PartsGrid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            MessageBox.Show(
                "Invalid input. Expected input type is "
                    + partsGrid[e.ColumnIndex, e.RowIndex].ValueType.Name
            );
        }

        // CellValidating and TryGetPlateOptions already surface invalid sizes to the user;
        // this only stops the grid from throwing when a commit is forced outside that path
        // (e.g. a BindingContext change while a cell is mid-edit).
        private void PlateGrid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
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
            public PlateOptionItem() { }

            public string Size { get; set; }
            public double Cost { get; set; }
        }
    }
}
