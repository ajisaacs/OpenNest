using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.IO;
using OpenNest.IO.Bom;

namespace OpenNest.Forms
{
    public partial class BomImportForm : Form
    {
        private static readonly Color NeedsInputBackColor = Color.FromArgb(255, 240, 200);

        private readonly BindingList<BomPartRow> _parts;
        private Dictionary<string, BomGroupPlateSettings> _groupSettings;
        private NestDefaults _defaults;

        public Form MdiParentForm { get; set; }

        public BomImportForm()
        {
            InitializeComponent();
            _parts = new BindingList<BomPartRow>();
            _parts.ListChanged += Parts_ListChanged;
            dgvParts.AutoGenerateColumns = false;
            dgvParts.DataSource = _parts;
            _groupSettings = new Dictionary<string, BomGroupPlateSettings>();
            _defaults = MainForm.LoadSavedNestDefaults(out _);
            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            txtPlateWidth.Text = _defaults.Size.Width.ToString("0.####");
            txtPlateLength.Text = _defaults.Size.Length.ToString("0.####");
        }

        #region File Browsing

        private void BrowseBom_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select BOM File",
                Filter = "Excel Files|*.xlsx|All Files|*.*",
                FilterIndex = 1,
            };

            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            txtBomFile.Text = dlg.FileName;

            if (string.IsNullOrWhiteSpace(txtDxfFolder.Text))
                txtDxfFolder.Text = Path.GetDirectoryName(dlg.FileName);

            if (string.IsNullOrWhiteSpace(txtJobName.Text))
            {
                var name = Path.GetFileNameWithoutExtension(dlg.FileName);
                if (name.EndsWith(" BOM", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 4).TrimEnd();
                txtJobName.Text = name;
            }
        }

        private void BrowseDxf_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select DXF Folder",
                SelectedPath = txtDxfFolder.Text,
            };

            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            txtDxfFolder.Text = dlg.SelectedPath;
        }

        #endregion

        #region Analyze

        private void Analyze_Click(object sender, EventArgs e)
        {
            if (!File.Exists(txtBomFile.Text))
            {
                MessageBox.Show(
                    "BOM file does not exist.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            try
            {
                List<BomItem> items;
                using (var reader = new BomReader(txtBomFile.Text))
                    items = reader.GetItems();

                LoadRows(BomImportRows.Build(items, txtDxfFolder.Text));
                btnCreateNests.Enabled = true;
                tabControl.SelectedTab = tabParts;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error reading BOM: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        #endregion

        #region Parts Tab

        /// <summary>
        /// Shows <paramref name="rows"/> in the Parts table, in their order, and
        /// rebuilds the groups and summary from them.
        /// </summary>
        internal void LoadRows(IEnumerable<BomPartRow> rows)
        {
            _groupSettings.Clear();
            _parts.RaiseListChangedEvents = false;
            _parts.Clear();
            foreach (var row in rows)
                _parts.Add(row);
            _parts.RaiseListChangedEvents = true;
            _parts.ResetBindings();

            RebuildGroups();
            UpdateSummary();
        }

        private BomPartRow PartAt(int rowIndex) =>
            rowIndex >= 0 && rowIndex < dgvParts.Rows.Count
                ? dgvParts.Rows[rowIndex].DataBoundItem as BomPartRow
                : null;

        private void Parts_ListChanged(object sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType != ListChangedType.ItemChanged)
                return;

            switch (e.PropertyDescriptor?.Name)
            {
                case nameof(BomPartRow.Material):
                case nameof(BomPartRow.Thickness):
                case nameof(BomPartRow.Qty):
                    RebuildGroups();
                    UpdateSummary();
                    break;
            }
        }

        private void DgvParts_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            // Rows without a drawing can never be imported, so they stay locked.
            if (PartAt(e.RowIndex) is not { IsEditable: true })
                e.Cancel = true;
        }

        private void DgvParts_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (!dgvParts.IsCurrentCellInEditMode)
                return;

            var text = e.FormattedValue?.ToString();
            string error = null;

            if (e.ColumnIndex == colQty.Index && !BomQuantity.TryParse(text, out _))
                error = "Enter a whole number of 1 or more.";
            else if (
                e.ColumnIndex == colThickness.Index
                && !string.IsNullOrEmpty(text)
                && !TryParseThickness(text, out _)
            )
                error = "Enter a thickness greater than 0, or leave it blank.";

            dgvParts.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? "";
            if (error != null)
                e.Cancel = true;
        }

        // CellValidating refuses bad text when the operator commits a cell. A
        // programmatic EndEdit() skips validation, so parsing keeps the row's
        // current value rather than storing text validation would refuse. The
        // grid ignores a null parsed value, so a blank thickness is left to the
        // column's DataSourceNullValue (null).
        private void DgvParts_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            var text = e.Value?.ToString() ?? "";
            var part = PartAt(e.RowIndex);

            if (e.ColumnIndex == colQty.Index)
            {
                if (BomQuantity.TryParse(text, out var qty))
                    Parsed(e, qty);
                else if (part?.Qty is int current)
                    Parsed(e, current);
            }
            else if (e.ColumnIndex == colThickness.Index && text.Length > 0)
            {
                if (TryParseThickness(text, out var thickness))
                    Parsed(e, thickness);
                else if (part?.Thickness is double current)
                    Parsed(e, current);
            }
            else if (e.ColumnIndex == colMaterial.Index)
            {
                Parsed(e, text.Trim());
            }
        }

        private static void Parsed(DataGridViewCellParsingEventArgs e, object value)
        {
            e.Value = value;
            e.ParsingApplied = true;
        }

        private void DgvParts_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            dgvParts.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "";
        }

        private void DgvParts_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            var part = PartAt(e.RowIndex);
            if (part == null)
                return;

            if (!part.IsEditable)
                e.CellStyle.ForeColor = Color.Gray;
            else if (part.Status != BomRowStatus.Ready)
                e.CellStyle.BackColor = NeedsInputBackColor;
        }

        private void DgvParts_CellToolTipTextNeeded(
            object sender,
            DataGridViewCellToolTipTextNeededEventArgs e
        )
        {
            if (e.ColumnIndex == colQty.Index && PartAt(e.RowIndex) is { QtyAssumed: true })
                e.ToolTipText = "The BOM has no quantity for this part; 1 is used.";
        }

        private void DgvParts_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            // Never show the grid's default error dialog; keep the edit open instead.
            e.ThrowException = false;
            e.Cancel = true;
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                dgvParts.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "This value cannot be used.";
        }

        private static bool TryParseThickness(string text, out double thickness) =>
            double.TryParse(text, out thickness) && double.IsFinite(thickness) && thickness > 0;

        #endregion

        #region Groups Tab

        private void RebuildGroups()
        {
            // Save existing settings before rebuilding
            SaveGroupSettings();

            var defaultWidth = double.TryParse(txtPlateWidth.Text, out var w)
                ? w
                : _defaults.Size.Width;
            var defaultLength = double.TryParse(txtPlateLength.Text, out var l)
                ? l
                : _defaults.Size.Length;

            var groups = BomImportGroups.Build(_parts);

            var table = new DataTable();
            table.Columns.Add("Material", typeof(string));
            table.Columns.Add("Thickness", typeof(double));
            table.Columns.Add("Parts", typeof(int));
            table.Columns.Add("Total Qty", typeof(int));
            table.Columns.Add("Plate Width", typeof(double));
            table.Columns.Add("Plate Length", typeof(double));
            table.Columns.Add("Part Spacing", typeof(double));
            table.Columns.Add("Edge Left", typeof(double));
            table.Columns.Add("Edge Bottom", typeof(double));
            table.Columns.Add("Edge Right", typeof(double));
            table.Columns.Add("Edge Top", typeof(double));

            foreach (var group in groups)
            {
                var existing = _groupSettings.TryGetValue(group.Key, out var gs);

                table.Rows.Add(
                    group.Material,
                    group.Thickness,
                    group.Parts.Count,
                    group.TotalQty,
                    existing ? gs.PlateWidth : defaultWidth,
                    existing ? gs.PlateLength : defaultLength,
                    existing ? gs.PartSpacing : _defaults.PartSpacing,
                    existing ? gs.EdgeLeft : _defaults.EdgeSpacing.Left,
                    existing ? gs.EdgeBottom : _defaults.EdgeSpacing.Bottom,
                    existing ? gs.EdgeRight : _defaults.EdgeSpacing.Right,
                    existing ? gs.EdgeTop : _defaults.EdgeSpacing.Top
                );
            }

            dgvGroups.DataSource = table;

            // Material, Thickness, Parts, Total Qty are read-only
            if (dgvGroups.Columns.Count > 0)
            {
                dgvGroups.Columns["Material"].ReadOnly = true;
                dgvGroups.Columns["Thickness"].ReadOnly = true;
                dgvGroups.Columns["Parts"].ReadOnly = true;
                dgvGroups.Columns["Total Qty"].ReadOnly = true;
            }

            btnCreateNests.Enabled = table.Rows.Count > 0;
        }

        private void SaveGroupSettings()
        {
            if (dgvGroups.DataSource is not DataTable table)
                return;

            _groupSettings.Clear();
            foreach (DataRow row in table.Rows)
            {
                var material = row["Material"]?.ToString() ?? "";
                var thickness = row["Thickness"] is double t ? t : 0;
                var key = BomImportGroups.Key(material, thickness);

                _groupSettings[key] = new BomGroupPlateSettings
                {
                    PlateWidth = row["Plate Width"] is double pw
                        ? pw
                        : _defaults.Size.Width,
                    PlateLength = row["Plate Length"] is double pl
                        ? pl
                        : _defaults.Size.Length,
                    PartSpacing = row["Part Spacing"] is double ps
                        ? ps
                        : _defaults.PartSpacing,
                    EdgeLeft = row["Edge Left"] is double el
                        ? el
                        : _defaults.EdgeSpacing.Left,
                    EdgeBottom = row["Edge Bottom"] is double eb
                        ? eb
                        : _defaults.EdgeSpacing.Bottom,
                    EdgeRight = row["Edge Right"] is double er
                        ? er
                        : _defaults.EdgeSpacing.Right,
                    EdgeTop = row["Edge Top"] is double et ? et : _defaults.EdgeSpacing.Top,
                };
            }
        }

        #endregion

        #region Summary

        private void UpdateSummary()
        {
            lblSummary.Text = BomImportSummary.Describe(_parts);
        }

        #endregion

        #region Create Nests

        private void CreateNests_Click(object sender, EventArgs e)
        {
            if (_parts.Count == 0 || !dgvParts.EndEdit())
                return;

            // Save latest group edits
            SaveGroupSettings();

            var defaultWidth = double.TryParse(txtPlateWidth.Text, out var dw)
                ? dw
                : _defaults.Size.Width;
            var defaultLength = double.TryParse(txtPlateLength.Text, out var dl)
                ? dl
                : _defaults.Size.Length;

            var groups = BomImportGroups.Build(_parts);

            if (groups.Count == 0)
            {
                MessageBox.Show(
                    "No groups with matched DXF files to create nests from.",
                    "Nothing to Create",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            var jobName = txtJobName.Text.Trim();
            var nestsCreated = 0;
            var importErrors = new List<string>();

            foreach (var group in groups)
            {
                if (!_groupSettings.TryGetValue(group.Key, out var plate))
                {
                    plate = new BomGroupPlateSettings
                    {
                        PlateWidth = defaultWidth,
                        PlateLength = defaultLength,
                        PartSpacing = _defaults.PartSpacing,
                        EdgeLeft = _defaults.EdgeSpacing.Left,
                        EdgeBottom = _defaults.EdgeSpacing.Bottom,
                        EdgeRight = _defaults.EdgeSpacing.Right,
                        EdgeTop = _defaults.EdgeSpacing.Top,
                    };
                }

                var result = BomNestBuilder.Build(group, plate, jobName, _defaults.ApplyTo);
                importErrors.AddRange(result.Errors);

                if (result.Nest == null)
                    continue;

                var editForm = new EditNestForm(result.Nest);
                editForm.MdiParent = MdiParentForm;
                editForm.Show();
                editForm.PlateView.ZoomToFit();

                nestsCreated++;
            }

            var summary = $"{nestsCreated} nest{(nestsCreated != 1 ? "s" : "")} created.";
            if (importErrors.Count > 0)
                summary +=
                    $"\n\n{importErrors.Count} import error(s):\n"
                    + string.Join("\n", importErrors);

            MessageBox.Show(
                summary,
                "Import Complete",
                MessageBoxButtons.OK,
                importErrors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information
            );

            Close();
        }

        #endregion

        private void BtnClose_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
