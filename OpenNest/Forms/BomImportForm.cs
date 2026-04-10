using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.IO.Bom;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class BomImportForm : Form
    {
        private List<BomPartRow> _parts;
        private Dictionary<string, GroupSettings> _groupSettings;
        private bool _suppressRegroup;
        private Nest.PlateSettings _templateDefaults;

        public Form MdiParentForm { get; set; }

        public BomImportForm()
        {
            InitializeComponent();
            _parts = new List<BomPartRow>();
            _groupSettings = new Dictionary<string, GroupSettings>();
            _templateDefaults = LoadTemplateDefaults();
            ApplyTemplateDefaults();
        }

        private Nest.PlateSettings LoadTemplateDefaults()
        {
            var templatePath = Properties.Settings.Default.NestTemplatePath;
            if (File.Exists(templatePath))
            {
                try
                {
                    var nest = new NestReader(templatePath).Read();
                    return nest.PlateDefaults;
                }
                catch { }
            }

            // Fallback defaults matching CreateDefaultNest
            return new Nest.PlateSettings
            {
                Size = new Geometry.Size(100, 100),
                Quadrant = 1,
                PartSpacing = 1,
                EdgeSpacing = new Spacing(1, 1, 1, 1),
            };
        }

        private void ApplyTemplateDefaults()
        {
            txtPlateWidth.Text = _templateDefaults.Size.Width.ToString("0.####");
            txtPlateLength.Text = _templateDefaults.Size.Length.ToString("0.####");
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
                MessageBox.Show("BOM file does not exist.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                List<BomItem> items;
                using (var reader = new BomReader(txtBomFile.Text))
                    items = reader.GetItems();

                var analysis = BomAnalyzer.Analyze(items, txtDxfFolder.Text);
                BuildPartRows(items, analysis);
                PopulatePartsGrid();
                RebuildGroups();
                UpdateSummary();
                btnCreateNests.Enabled = true;
                tabControl.SelectedTab = tabParts;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading BOM: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildPartRows(List<BomItem> items, BomAnalysis analysis)
        {
            var matchedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in analysis.Groups)
                foreach (var part in group.Parts)
                    if (part.DxfPath != null)
                        matchedPaths[part.Item.FileName ?? ""] = part.DxfPath;

            _parts = new List<BomPartRow>();

            foreach (var item in items)
            {
                var row = new BomPartRow
                {
                    ItemNum = item.ItemNum,
                    FileName = item.FileName,
                    Qty = item.Qty,
                    Description = item.Description,
                    Material = item.Material,
                    Thickness = item.Thickness,
                };

                if (string.IsNullOrWhiteSpace(item.FileName))
                {
                    row.Status = "Skipped";
                    row.IsEditable = false;
                }
                else
                {
                    var lookupName = item.FileName;
                    if (lookupName.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
                        lookupName = Path.GetFileNameWithoutExtension(lookupName);

                    if (matchedPaths.TryGetValue(lookupName, out var dxfPath))
                    {
                        row.DxfPath = dxfPath;
                        row.Status = "Matched";
                        row.IsEditable = true;
                    }
                    else
                    {
                        row.Status = "No DXF";
                        row.IsEditable = false;
                    }
                }

                _parts.Add(row);
            }

            _groupSettings.Clear();
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
                    part.Status
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

            RebuildGroups();
            UpdateSummary();
        }

        #endregion

        #region Groups Tab

        private void RebuildGroups()
        {
            // Save existing settings before rebuilding
            SaveGroupSettings();

            var defaultWidth = double.TryParse(txtPlateWidth.Text, out var w) ? w : _templateDefaults.Size.Width;
            var defaultLength = double.TryParse(txtPlateLength.Text, out var l) ? l : _templateDefaults.Size.Length;

            var groups = _parts
                .Where(p => p.IsEditable
                    && !string.IsNullOrWhiteSpace(p.Material)
                    && p.Thickness.HasValue)
                .GroupBy(p => new
                {
                    Material = p.Material.ToUpperInvariant(),
                    Thickness = p.Thickness.Value
                })
                .OrderBy(g => g.First().Material)
                .ThenBy(g => g.Key.Thickness)
                .ToList();

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
                var material = group.First().Material;
                var thickness = group.Key.Thickness;
                var key = GroupKey(material, thickness);

                var existing = _groupSettings.TryGetValue(key, out var gs);

                table.Rows.Add(
                    material,
                    thickness,
                    group.Count(),
                    group.Sum(p => p.Qty ?? 0),
                    existing ? gs.PlateWidth : defaultWidth,
                    existing ? gs.PlateLength : defaultLength,
                    existing ? gs.PartSpacing : _templateDefaults.PartSpacing,
                    existing ? gs.EdgeLeft : _templateDefaults.EdgeSpacing.Left,
                    existing ? gs.EdgeBottom : _templateDefaults.EdgeSpacing.Bottom,
                    existing ? gs.EdgeRight : _templateDefaults.EdgeSpacing.Right,
                    existing ? gs.EdgeTop : _templateDefaults.EdgeSpacing.Top
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
                var key = GroupKey(material, thickness);

                _groupSettings[key] = new GroupSettings
                {
                    PlateWidth = row["Plate Width"] is double pw ? pw : _templateDefaults.Size.Width,
                    PlateLength = row["Plate Length"] is double pl ? pl : _templateDefaults.Size.Length,
                    PartSpacing = row["Part Spacing"] is double ps ? ps : _templateDefaults.PartSpacing,
                    EdgeLeft = row["Edge Left"] is double el ? el : _templateDefaults.EdgeSpacing.Left,
                    EdgeBottom = row["Edge Bottom"] is double eb ? eb : _templateDefaults.EdgeSpacing.Bottom,
                    EdgeRight = row["Edge Right"] is double er ? er : _templateDefaults.EdgeSpacing.Right,
                    EdgeTop = row["Edge Top"] is double et ? et : _templateDefaults.EdgeSpacing.Top,
                };
            }
        }

        private static string GroupKey(string material, double thickness)
            => $"{material?.ToUpperInvariant()}|{thickness}";

        #endregion

        #region Summary

        private void UpdateSummary()
        {
            var skipped = _parts.Count(p => p.Status == "Skipped");
            var noDxf = _parts.Count(p => p.Status == "No DXF");
            var matched = _parts.Count(p => p.Status == "Matched");

            var summaryParts = new List<string>();
            if (skipped > 0)
                summaryParts.Add($"{skipped} skipped (no file name)");
            if (noDxf > 0)
                summaryParts.Add($"{noDxf} no DXF found");

            lblSummary.Text = summaryParts.Count > 0
                ? string.Join(", ", summaryParts)
                : $"{matched} parts matched";
        }

        #endregion

        #region Create Nests

        private void CreateNests_Click(object sender, EventArgs e)
        {
            if (_parts == null || _parts.Count == 0)
                return;

            // Save latest group edits
            SaveGroupSettings();

            var defaultWidth = double.TryParse(txtPlateWidth.Text, out var dw) ? dw : _templateDefaults.Size.Width;
            var defaultLength = double.TryParse(txtPlateLength.Text, out var dl) ? dl : _templateDefaults.Size.Length;

            var groups = _parts
                .Where(p => p.IsEditable
                    && !string.IsNullOrWhiteSpace(p.Material)
                    && p.Thickness.HasValue
                    && !string.IsNullOrWhiteSpace(p.DxfPath))
                .GroupBy(p => new
                {
                    Material = p.Material.ToUpperInvariant(),
                    Thickness = p.Thickness.Value
                })
                .ToList();

            if (groups.Count == 0)
            {
                MessageBox.Show("No groups with matched DXF files to create nests from.", "Nothing to Create",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var jobName = txtJobName.Text.Trim();
            var nestsCreated = 0;
            var importErrors = new List<string>();

            foreach (var group in groups)
            {
                var material = group.First().Material;
                var thickness = group.Key.Thickness;
                var key = GroupKey(material, thickness);

                var hasSettings = _groupSettings.TryGetValue(key, out var gs);
                var plateWidth = hasSettings ? gs.PlateWidth : defaultWidth;
                var plateLength = hasSettings ? gs.PlateLength : defaultLength;
                var partSpacing = hasSettings ? gs.PartSpacing : _templateDefaults.PartSpacing;
                var edgeLeft = hasSettings ? gs.EdgeLeft : _templateDefaults.EdgeSpacing.Left;
                var edgeBottom = hasSettings ? gs.EdgeBottom : _templateDefaults.EdgeSpacing.Bottom;
                var edgeRight = hasSettings ? gs.EdgeRight : _templateDefaults.EdgeSpacing.Right;
                var edgeTop = hasSettings ? gs.EdgeTop : _templateDefaults.EdgeSpacing.Top;

                var nestName = $"{jobName} - {thickness:0.###} {material}";
                var nest = new Nest(nestName);
                nest.DateCreated = DateTime.Now;
                nest.DateLastModified = DateTime.Now;
                nest.PlateDefaults.Size = new Geometry.Size(plateWidth, plateLength);
                nest.Thickness = thickness;
                nest.Material = new Material(material);
                nest.PlateDefaults.Quadrant = _templateDefaults.Quadrant;
                nest.PlateDefaults.PartSpacing = partSpacing;
                nest.PlateDefaults.EdgeSpacing = new Spacing(edgeLeft, edgeBottom, edgeRight, edgeTop);

                foreach (var part in group)
                {
                    if (!File.Exists(part.DxfPath))
                    {
                        importErrors.Add($"{part.FileName}: DXF file not found");
                        continue;
                    }

                    try
                    {
                        var drawing = CadImporter.ImportDrawing(part.DxfPath,
                            new CadImportOptions { Quantity = part.Qty ?? 1 });
                        drawing.Material = new Material(material);
                        nest.Drawings.Add(drawing);
                    }
                    catch (Exception ex)
                    {
                        importErrors.Add($"{part.FileName}: {ex.Message}");
                    }
                }

                if (nest.Drawings.Count == 0)
                    continue;

                nest.CreatePlate();

                var editForm = new EditNestForm(nest);
                editForm.MdiParent = MdiParentForm;
                editForm.Show();
                editForm.PlateView.ZoomToFit();

                nestsCreated++;
            }

            var summary = $"{nestsCreated} nest{(nestsCreated != 1 ? "s" : "")} created.";
            if (importErrors.Count > 0)
                summary += $"\n\n{importErrors.Count} import error(s):\n" + string.Join("\n", importErrors);

            MessageBox.Show(summary, "Import Complete", MessageBoxButtons.OK,
                importErrors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            Close();
        }

        #endregion

        private void BtnClose_Click(object sender, EventArgs e)
        {
            Close();
        }
    }

    internal class BomPartRow
    {
        public int? ItemNum { get; set; }
        public string FileName { get; set; }
        public int? Qty { get; set; }
        public string Description { get; set; }
        public string Material { get; set; }
        public double? Thickness { get; set; }
        public string DxfPath { get; set; }
        public string Status { get; set; }
        public bool IsEditable { get; set; }
    }

    internal class GroupSettings
    {
        public double PlateWidth { get; set; }
        public double PlateLength { get; set; }
        public double PartSpacing { get; set; }
        public double EdgeLeft { get; set; }
        public double EdgeBottom { get; set; }
        public double EdgeRight { get; set; }
        public double EdgeTop { get; set; }
    }
}
