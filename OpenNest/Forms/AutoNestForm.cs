using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data.MaterialLedger;
using OpenNest.Engine;
using OpenNest.Geometry;
using OpenNest.Help;
using OpenNest.Math;

namespace OpenNest.Forms
{
    public partial class AutoNestForm : Form
    {
        private static readonly Regex SizePattern = new(
            @"^(\d+\.?\d*)\s*[xX×]\s*(\d+\.?\d*)$",
            RegexOptions.None,
            TimeSpan.FromMilliseconds(250)
        );

        private readonly Nest nest;
        private CancellationTokenSource ledgerCancellation;
        private bool loadingLedger;
        private string ledgerSummary = "";

        /// <summary>A dark amber that reads on the grid's white rows.</summary>
        internal static readonly System.Drawing.Color SubstituteColor = System.Drawing.Color.FromArgb(160, 82, 0);

        public AutoNestForm(Nest nest)
        {
            this.nest = nest;
            InitializeComponent();
            GridStyle.Apply(partsGrid);
            GridStyle.Apply(plateGrid);
            cancelButton.CausesValidation = false;
            SetupPartsGrid();
            SetupPlateGrid();
            LoadEngines();
            LoadDrawings(nest);
            LoadDefaultPlateOptions();
            SetPlateOptimizerVisible(false);
            UpdateLedgerButton();
            HelpTopics.Attach(this, HelpTopics.AutoNest);

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

        /// <summary>The smallest offcut credited as salvage unless the operator types another.</summary>
        public const double DefaultMinRemnantSize = Nest.DefaultMinimumRemnantDimension;

        public double MinRemnantSize
        {
            get
            {
                if (double.TryParse(minRemnantBox.Text, out var val) && val > 0)
                    return val;
                return DefaultMinRemnantSize;
            }
            set { minRemnantBox.Text = value.ToString("G"); }
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
                new DataGridViewCheckBoxColumn
                {
                    DataPropertyName = "Use",
                    HeaderText = "Use",
                    ToolTipText = "Untick to leave the row out of this Auto Nest.",
                    Width = 40,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );
            plateGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "Size",
                    HeaderText = "Size",
                    Width = 120,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );
            var quantityColumn = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Quantity",
                HeaderText = "Qty",
                ToolTipText = "Sheets on hand. Blank offers the size without limit.",
                Width = 55,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            };
            // A blank cell binds as null (unlimited), not DBNull.
            quantityColumn.DefaultCellStyle.DataSourceNullValue = null;
            quantityColumn.DefaultCellStyle.NullValue = "";
            plateGrid.Columns.Add(quantityColumn);
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

            plateGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "Source",
                    HeaderText = "Source",
                    ToolTipText = "Where a ledger row's stock comes from. \"for {grade}\" marks a substitute MaterialLedger accepts in place of the nest's grade.",
                    ReadOnly = true,
                    Width = 160,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                }
            );

            plateGrid.CellValidating += PlateGrid_CellValidating;
            plateGrid.DataError += PlateGrid_DataError;
            plateGrid.CellBeginEdit += PlateGrid_CellBeginEdit;
            plateGrid.CellFormatting += PlateGrid_CellFormatting;
            plateGrid.CellToolTipTextNeeded += PlateGrid_CellToolTipTextNeeded;
            plateGrid.CellValueChanged += (s, e) => UpdateLedgerSummary();
            // Commit a Use tick at once so the summary line follows it.
            plateGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (plateGrid.IsCurrentCellDirty && plateGrid.CurrentCell is DataGridViewCheckBoxCell)
                    plateGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
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
                nestItem.RotationEnd = gridItem.RotationEndRadians;
                nestItem.RotationStart = gridItem.RotationStartRadians;
                nestItem.StepAngle = gridItem.StepAngleRadians;

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

        /// <summary>The ledger rows in the grid, ticked or not; their ids name the run's ledger stock.</summary>
        internal IReadOnlyList<LedgerStockRow> LedgerRows =>
            plateGrid.DataSource is BindingList<PlateOptionItem> items
                ? items.Where(i => i.LedgerRow != null).Select(i => i.LedgerRow).ToList()
                : Array.Empty<LedgerStockRow>();

        public bool TryGetPlateOptions(out List<PlateOption> options, out string error) =>
            TryGetPlateOptions(out options, out _, out error);

        /// <summary>
        /// <paramref name="options"/> is the stock for this run: ticked rows with their
        /// quantities, ledger rows under their <c>ledger:{n}</c> ids, and no costs while ledger
        /// stock is offered. <paramref name="savedOptions"/> is what the nest keeps: every
        /// hand-typed row with its typed cost, never ledger rows or quantities.
        /// </summary>
        public bool TryGetPlateOptions(out List<PlateOption> options, out List<PlateOption> savedOptions,
            out string error)
        {
            options = new List<PlateOption>();
            savedOptions = new List<PlateOption>();
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

            if (plateGrid.IsCurrentCellInEditMode && plateGrid.CurrentCell != null
                && plateGrid.Columns[plateGrid.CurrentCell.ColumnIndex].DataPropertyName == "Quantity"
                && plateGrid.EditingControl != null
                && !ValidQuantity(plateGrid.EditingControl.Text, ItemAt(plateGrid.CurrentCell.RowIndex)))
            {
                error = QuantityError(plateGrid.CurrentCell.RowIndex);
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

            // Ledger costs are per lot unit (a drop carries its source sheet's), so a run with
            // ledger stock scores by area; hand-typed costs stay saved for later runs.
            var ledgerOffered = gridItems.Any(i => i.LedgerRow != null && i.Use);
            var validated = new List<PlateOption>();
            var saved = new List<PlateOption>();
            for (var index = 0; index < gridItems.Count; index++)
            {
                var item = gridItems[index];
                if (item.LedgerRow != null)
                {
                    if (!ValidQuantity(item.Quantity, item))
                    {
                        error = QuantityError(index);
                        if (index < plateGrid.Rows.Count)
                            plateGrid.Rows[index].ErrorText = error;
                        return false;
                    }
                    if (index < plateGrid.Rows.Count)
                        plateGrid.Rows[index].ErrorText = "";
                    if (item.Use)
                        validated.Add(new PlateOption
                        {
                            Width = item.LedgerRow.Size.Width,
                            Length = item.LedgerRow.Size.Length,
                            Quantity = item.Quantity ?? item.LedgerRow.Quantity,
                            Id = item.LedgerRow.Id,
                        });
                    continue;
                }
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
                    || saved.Count > 0 && (saved[0].Cost == 0) != (item.Cost == 0))
                {
                    error = $"Invalid stock cost in row {index + 1}. Use finite positive costs on every active row, or zero on every row.";
                    if (index < plateGrid.Rows.Count)
                        plateGrid.Rows[index].ErrorText = error;
                    return false;
                }
                if (!ValidQuantity(item.Quantity, item))
                {
                    error = QuantityError(index);
                    if (index < plateGrid.Rows.Count)
                        plateGrid.Rows[index].ErrorText = error;
                    return false;
                }
                if (index < plateGrid.Rows.Count)
                    plateGrid.Rows[index].ErrorText = "";
                saved.Add(new PlateOption { Width = width, Length = length, Cost = item.Cost });
                if (item.Use)
                    validated.Add(new PlateOption
                    {
                        Width = width,
                        Length = length,
                        Cost = ledgerOffered ? 0 : item.Cost,
                        Quantity = item.Quantity,
                    });
            }

            // Publish only a completely validated collection, never a valid prefix.
            options = validated;
            savedOptions = saved;
            return true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Closing the dialog abandons a ledger request still in flight.
            ledgerCancellation?.Cancel();
            base.OnFormClosed(e);
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
                        Quantity = o.Quantity,
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
            ledgerButton.Visible = visible;
            ledgerSummaryLabel.Visible = visible;
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

        /// <summary>True when the ledger is set up on this PC; the form's tests replace it.</summary>
        internal virtual bool LedgerConfigured => MaterialLedgerStore.Settings.IsConfigured;

        /// <summary>Reads the nest material's free stock; the form's tests replace it.</summary>
        internal virtual Task<LedgerStockResult> FetchLedgerStockAsync(string material, string grade,
            double thicknessIn, CancellationToken cancellationToken) =>
            MaterialLedgerStore.CreateClient().GetStockAsync(material, grade, thicknessIn, cancellationToken);

        /// <summary>
        /// The nest server id of the nest being nested, whose own codes stay offered; empty for
        /// a nest not saved to the server.
        /// </summary>
        public Guid ServerNestId { get; set; }

        /// <summary>Reads the codes on other open server nests; the form's tests replace it.</summary>
        internal virtual Task<LedgerCodesInUse> FetchCodesInUseAsync(Guid excludeNestId, CancellationToken cancellationToken) =>
            LedgerCodesOnOtherNests.ReadAsync(excludeNestId, cancellationToken);

        private string MaterialName => nest?.Material?.Name?.Trim() ?? "";

        internal void UpdateLedgerButton()
        {
            string reason = null;
            if (!LedgerConfigured)
                reason = "Set the MaterialLedger URL and API key under Tools > Options first.";
            else if (MaterialName.Length == 0 || !(nest.Thickness > 0))
                reason = "Give the nest a material and a thickness first.";
            ledgerButton.Enabled = reason == null && !loadingLedger;
            ledgerToolTip.SetToolTip(ledgerButton, reason
                ?? "Replace the ledger rows with the free sheets and drops of this nest's material. "
                + "Stock is a snapshot, not a reservation.");
        }

        private async void ledgerButton_Click(object sender, EventArgs e) => await LoadLedgerStockAsync();

        /// <summary>Requests the nest material's stock and applies the answer, unless the form closed first.</summary>
        internal async Task LoadLedgerStockAsync()
        {
            ledgerCancellation?.Cancel();
            ledgerCancellation = new CancellationTokenSource();
            var token = ledgerCancellation.Token;
            loadingLedger = true;
            UpdateLedgerButton();
            ledgerSummaryLabel.Text = "Loading stock from MaterialLedger...";
            try
            {
                // Codes on other open nests are not held in ML yet, so they are left out here.
                var inUseTask = FetchCodesInUseAsync(ServerNestId, token);
                var result = await FetchLedgerStockAsync(MaterialName, nest.Material?.Grade?.Trim() ?? "",
                    LedgerStockMapper.ThicknessInches(nest.Thickness, nest.Units), token);
                var inUse = await inUseTask;
                if (token.IsCancellationRequested || IsDisposed)
                    return;
                ApplyLedgerStock(result, inUse);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The form closed or a newer load started.
            }
            finally
            {
                if (!IsDisposed && !token.IsCancellationRequested)
                {
                    loadingLedger = false;
                    UpdateLedgerButton();
                }
            }
        }

        /// <summary>
        /// Replaces the ledger rows with <paramref name="result"/>'s stock (an unmapped material
        /// clears them; a failed request keeps them) and reports the outcome under the grid.
        /// Codes in <paramref name="inUse"/> are left out; when it is unknown the stock loads
        /// whole and the summary says other nests were not checked.
        /// </summary>
        internal void ApplyLedgerStock(LedgerStockResult result, LedgerCodesInUse inUse = null)
        {
            switch (result)
            {
                case LedgerStockResult.Ok ok:
                    var mapping = LedgerStockMapper.Map(ok.Items, nest.Units, PartEnvelopes(), ok.Specs);
                    if (inUse is { IsKnown: true })
                        mapping = LedgerCodeFilter.Exclude(mapping, inUse.Codes);
                    ReplaceLedgerRows(mapping.Rows);
                    ledgerSummary = mapping.Rows.Count == 0
                        && mapping.SkippedUnknownShape + mapping.SkippedTooSmall + mapping.OnOtherNests == 0
                        ? $"Ledger: no free stock of {DescribeMaterial()}."
                        : mapping.Summary;
                    if (inUse is { IsKnown: false })
                        ledgerSummary += $" (other nests not checked: {inUse.Unavailable})";
                    break;
                case LedgerStockResult.Unmapped:
                    ReplaceLedgerRows(Array.Empty<LedgerStockRow>());
                    ledgerSummary = $"MaterialLedger has no mapping for {DescribeMaterial()}.";
                    break;
                case LedgerStockResult.Unauthorized:
                    ledgerSummary = "MaterialLedger rejected the API key (Tools > Options).";
                    break;
                case LedgerStockResult.Unreachable unreachable:
                    ledgerSummary = $"MaterialLedger unreachable: {unreachable.Reason}";
                    break;
                default:
                    ledgerSummary = "Set the MaterialLedger URL under Tools > Options.";
                    break;
            }
            UpdateLedgerSummary();
        }

        private string DescribeMaterial()
        {
            var grade = nest.Material?.Grade?.Trim();
            var unit = nest.Units == Units.Millimeters ? "mm" : "in";
            return FormattableString.Invariant(
                $"{MaterialName}{(string.IsNullOrEmpty(grade) ? "" : " " + grade)} {nest.Thickness:G} {unit}");
        }

        private List<Size> PartEnvelopes()
        {
            var envelopes = new List<Size>();
            if (partsGrid.DataSource is not List<DataGridViewItem> gridItems)
                return envelopes;
            foreach (var item in gridItems.Where(i => i.Quantity > 0 && i.RefDrawing?.Program != null))
            {
                var box = item.RefDrawing.Program.BoundingBox();
                if (double.IsFinite(box.Width) && double.IsFinite(box.Length) && box.Width > 0 && box.Length > 0)
                    envelopes.Add(new Size(box.Width, box.Length));
            }
            return envelopes;
        }

        private void ReplaceLedgerRows(IReadOnlyList<LedgerStockRow> rows)
        {
            plateGrid.EndEdit();
            if (plateGrid.DataSource is not BindingList<PlateOptionItem> items)
            {
                items = new BindingList<PlateOptionItem>();
                plateGrid.DataSource = items;
            }
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].LedgerRow != null)
                    items.RemoveAt(i);
            }
            // Substitutes load ticked like the rest; the programmer unticks any not wanted.
            var grade = nest.Material?.Grade?.Trim();
            foreach (var row in rows)
            {
                items.Add(new PlateOptionItem
                {
                    Size = FormatSize(System.Math.Round(row.Size.Width, 3), System.Math.Round(row.Size.Length, 3)),
                    Quantity = row.Quantity,
                    LedgerRow = row,
                    Source = LedgerSource(row, grade),
                    Tooltip = LedgerTooltip(row, grade),
                });
            }
        }

        private void UpdateLedgerSummary()
        {
            var text = ledgerSummary;
            if (plateGrid.DataSource is BindingList<PlateOptionItem> items
                && items.Any(i => i.LedgerRow != null && i.Use)
                && items.Any(i => i.LedgerRow == null && i.Use && i.Cost > 0))
                text = (text.Length > 0 ? text + "; " : "") + "costs ignored while ledger stock is loaded";
            ledgerSummaryLabel.Text = text;
        }

        private PlateOptionItem ItemAt(int rowIndex) =>
            plateGrid.DataSource is BindingList<PlateOptionItem> items && rowIndex >= 0 && rowIndex < items.Count
                ? items[rowIndex]
                : null;

        private string QuantityError(int rowIndex)
        {
            var ledger = ItemAt(rowIndex)?.LedgerRow;
            return ledger == null
                ? $"Invalid stock quantity in row {rowIndex + 1}. Enter a whole number of sheets, or leave it blank for no limit."
                : $"Invalid stock quantity in row {rowIndex + 1}. The ledger has {ledger.Quantity} free; enter 1 to {ledger.Quantity}, or untick the row.";
        }

        private static bool ValidQuantity(int? quantity, PlateOptionItem item) =>
            quantity == null || quantity >= 1 && (item?.LedgerRow == null || quantity <= item.LedgerRow.Quantity);

        private static bool ValidQuantity(string text, PlateOptionItem item)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;
            return int.TryParse(text.Trim(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.CurrentCulture, out var quantity)
                && ValidQuantity(quantity, item);
        }

        // Ledger rows keep the ledger's size and carry no cost; only Use and Qty are editable.
        private void PlateGrid_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            var property = plateGrid.Columns[e.ColumnIndex].DataPropertyName;
            if (ItemAt(e.RowIndex)?.LedgerRow != null && property is "Size" or "Cost")
                e.Cancel = true;
        }

        private void PlateGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            var property = plateGrid.Columns[e.ColumnIndex].DataPropertyName;
            var ledger = ItemAt(e.RowIndex)?.LedgerRow;
            if (ledger == null)
                return;
            // A substitute's source stays in full colour so it stands out from the preferred stock.
            if (property == "Source" && ledger.IsSubstitute)
                e.CellStyle.ForeColor = SubstituteColor;
            else if (property is "Size" or "Cost" or "Source")
                e.CellStyle.ForeColor = System.Drawing.SystemColors.GrayText;
        }

        private void PlateGrid_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            var item = ItemAt(e.RowIndex);
            if (item?.LedgerRow != null)
                e.ToolTipText = item.Tooltip;
        }

        /// <summary>
        /// A ledger row's Source cell: its grade and finish ("A1011 P&amp;O", or "Ledger" when the
        /// ledger names neither), "for HR" on a substitute, and "(irr.)" on an irregular drop.
        /// </summary>
        internal static string LedgerSource(LedgerStockRow row, string requestedGrade)
        {
            var text = row.Label ?? "Ledger";
            if (row.IsSubstitute)
                text += string.IsNullOrEmpty(requestedGrade) ? " (substitute)" : $" for {requestedGrade}";
            return row.IsIrregular ? text + " (irr.)" : text;
        }

        internal static string LedgerTooltip(LedgerStockRow row, string requestedGrade = null)
        {
            const int Shown = 12;
            var codes = row.ItemCodes.Distinct(StringComparer.Ordinal).ToList();
            var codeText = string.Join(", ", codes.Take(Shown)) + (codes.Count > Shown ? $", +{codes.Count - Shown} more" : "");
            var text = "";
            if (row.IsSubstitute)
                text = $"Substitute: MaterialLedger accepts {row.Label ?? "this stock"} in place of "
                    + $"{(string.IsNullOrEmpty(requestedGrade) ? "the nest's grade" : requestedGrade)} (rank {row.Rank}). "
                    + "Untick Use to leave it out.\n";
            else if (row.Label != null)
                text = $"Source: {row.Label}\n";
            text += $"Items: {codeText}";
            if (row.Locations.Count > 0)
                text += $"\nLocations: {string.Join(", ", row.Locations.Take(Shown))}"
                    + (row.Locations.Count > Shown ? $", +{row.Locations.Count - Shown} more" : "");
            if (row.IsIrregular)
                text += "\nIrregular: offered at its conservative usable rectangle.";
            return text;
        }

        private static bool ValidCost(string text) =>
            double.TryParse(text, out var cost) && double.IsFinite(cost) && cost >= 0;

        private void PlateGrid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (plateGrid.Columns[e.ColumnIndex].DataPropertyName == "Quantity")
            {
                e.Cancel = !plateGrid.Rows[e.RowIndex].IsNewRow
                    && !ValidQuantity(e.FormattedValue?.ToString(), ItemAt(e.RowIndex));
                plateGrid.Rows[e.RowIndex].ErrorText = e.Cancel ? QuantityError(e.RowIndex) : "";
                return;
            }
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
            item.RotationStartRadians = dwg.Constraints.StartAngle;
            item.RotationEndRadians = dwg.Constraints.EndAngle;
            item.StepAngleRadians = dwg.Constraints.StepAngle;

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

            // Rotations are kept in radians and shown in degrees, so an unedited row passes
            // the drawing's limits through without a conversion round trip.
            internal double RotationStartRadians { get; set; }
            internal double RotationEndRadians { get; set; }
            internal double StepAngleRadians { get; set; }

            [DisplayName("Rot Start")]
            public double RotationStart
            {
                get => ToDisplayDegrees(RotationStartRadians);
                set => RotationStartRadians = Angle.ToRadians(value);
            }

            [DisplayName("Rot End")]
            public double RotationEnd
            {
                get => ToDisplayDegrees(RotationEndRadians);
                set => RotationEndRadians = Angle.ToRadians(value);
            }

            [DisplayName("Step")]
            public double StepAngle
            {
                get => ToDisplayDegrees(StepAngleRadians);
                set => StepAngleRadians = Angle.ToRadians(value);
            }

            private static double ToDisplayDegrees(double radians) =>
                System.Math.Round(Angle.ToDegrees(radians), 4);
        }

        private class PlateOptionItem
        {
            public PlateOptionItem() { }

            public bool Use { get; set; } = true;
            public string Size { get; set; }

            /// <summary>Null offers the size without limit; a ledger row starts at its free count.</summary>
            public int? Quantity { get; set; }

            public double Cost { get; set; }

            /// <summary>Blank for hand-typed rows; see <see cref="LedgerSource"/>.</summary>
            public string Source { get; set; } = "";

            /// <summary>Set for rows loaded from MaterialLedger; these are never saved with the nest.</summary>
            internal LedgerStockRow LedgerRow { get; set; }

            internal string Tooltip { get; set; }
        }
    }
}
