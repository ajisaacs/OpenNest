using System;
using System.Collections.Generic;
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
        private List<BomPartRow> _parts;
        private Dictionary<string, BomGroupPlateSettings> _groupSettings;
        private bool _suppressRegroup;
        private NestDefaults _defaults;

        public Form MdiParentForm { get; set; }

        public BomImportForm()
        {
            InitializeComponent();
            _parts = new List<BomPartRow>();
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

                _parts = BomImportRows.Build(items, txtDxfFolder.Text);
                _groupSettings.Clear();
                PopulatePartsGrid();
                RebuildGroups();
                UpdateSummary();
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

        private void PopulatePartsGrid()
        {
            _suppressRegroup = true;

            var table = new DataTable();
            table.Columns.Add("Item #", typeof(string));
            table.Columns.Add("File Name", typeof(string));
            table.Columns.Add("Qty", typeof(string));
            table.Columns.Add("Description", typeof(string));
            table.Columns.Add("Material", typeof(string));
            table.Columns.Add("Thickness", typeof(string));
            table.Columns.Add("Status", typeof(string));

            foreach (var part in _parts)
            {
                table.Rows.Add(
                    part.ItemNum?.ToString() ?? "",
                    part.FileName ?? "",
                    part.Qty?.ToString() ?? "",
                    part.Description ?? "",
                    part.Material ?? "",
                    part.Thickness?.ToString("0.####") ?? "",
                    part.StatusText
                );
            }

            dgvParts.DataSource = table;

            // Make non-editable columns read-only
            foreach (DataGridViewColumn col in dgvParts.Columns)
            {
                if (col.Name != "Material" && col.Name != "Thickness")
                    col.ReadOnly = true;
            }

            // Style rows by status
            for (var i = 0; i < _parts.Count; i++)
            {
                if (!_parts[i].IsEditable)
                {
                    dgvParts.Rows[i].ReadOnly = true;
                    dgvParts.Rows[i].DefaultCellStyle.ForeColor = Color.Gray;
                }
            }

            dgvParts.CellValueChanged -= DgvParts_CellValueChanged;
            dgvParts.CellValueChanged += DgvParts_CellValueChanged;

            _suppressRegroup = false;
        }

        private void DgvParts_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressRegroup || e.RowIndex < 0)
                return;

            var colName = dgvParts.Columns[e.ColumnIndex].Name;
            if (colName != "Material" && colName != "Thickness")
                return;

            var part = _parts[e.RowIndex];
            if (!part.IsEditable)
                return;

            if (colName == "Material")
                part.Material = dgvParts.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();

            if (colName == "Thickness")
            {
                var text = dgvParts.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
                part.Thickness = double.TryParse(text, out var t) ? t : (double?)null;
            }

            _suppressRegroup = true;
            dgvParts.Rows[e.RowIndex].Cells["Status"].Value = part.StatusText;
            _suppressRegroup = false;

            RebuildGroups();
            UpdateSummary();
        }

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
            if (_parts == null || _parts.Count == 0)
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
