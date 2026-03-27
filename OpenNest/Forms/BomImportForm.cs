using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.IO.Bom;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class BomImportForm : Form
    {
        private BomAnalysis _analysis;

        public Form MdiParentForm { get; set; }

        public BomImportForm()
        {
            InitializeComponent();
        }

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

            // Default DXF folder to the same directory as the BOM file
            if (string.IsNullOrWhiteSpace(txtDxfFolder.Text))
                txtDxfFolder.Text = Path.GetDirectoryName(dlg.FileName);

            // Derive job name by stripping " BOM" suffix and extension
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

                _analysis = BomAnalyzer.Analyze(items, txtDxfFolder.Text);

                PopulateGrid(_analysis);
                UpdateSummary(_analysis);
                btnCreateNests.Enabled = _analysis.Groups.Count > 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading BOM: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulateGrid(BomAnalysis analysis)
        {
            var table = new DataTable();
            table.Columns.Add("Material", typeof(string));
            table.Columns.Add("Thickness", typeof(string));
            table.Columns.Add("Parts", typeof(int));
            table.Columns.Add("Total Qty", typeof(int));

            foreach (var group in analysis.Groups)
            {
                var partCount = group.Parts.Count;
                var totalQty = group.Parts.Sum(p => p.Item.Qty ?? 0);
                table.Rows.Add(group.Material, group.Thickness.ToString("0.###"), partCount, totalQty);
            }

            dgvGroups.DataSource = table;
        }

        private void UpdateSummary(BomAnalysis analysis)
        {
            var parts = new List<string>();
            if (analysis.Skipped.Count > 0)
                parts.Add($"{analysis.Skipped.Count} skipped (no DXF file or thickness given)");
            if (analysis.Unmatched.Count > 0)
                parts.Add($"{analysis.Unmatched.Count} unmatched (DXF file not found)");

            lblSummary.Text = parts.Count > 0
                ? string.Join(", ", parts)
                : string.Empty;
        }

        private void CreateNests_Click(object sender, EventArgs e)
        {
            if (_analysis == null || _analysis.Groups.Count == 0)
                return;

            if (!double.TryParse(txtPlateWidth.Text, out var plateWidth) || plateWidth <= 0)
            {
                MessageBox.Show("Plate width must be a positive number.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!double.TryParse(txtPlateLength.Text, out var plateLength) || plateLength <= 0)
            {
                MessageBox.Show("Plate length must be a positive number.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var jobName = txtJobName.Text.Trim();
            var importer = new DxfImporter();
            var nestsCreated = 0;
            var importErrors = new List<string>();

            foreach (var group in _analysis.Groups)
            {
                var nestName = $"{jobName} - {group.Thickness:0.###} {group.Material}";
                var nest = new Nest(nestName);
                nest.DateCreated = DateTime.Now;
                nest.DateLastModified = DateTime.Now;
                nest.PlateDefaults.Size = new Size(plateWidth, plateLength);
                nest.PlateDefaults.Thickness = group.Thickness;
                nest.PlateDefaults.Material = new Material(group.Material);
                nest.PlateDefaults.Quadrant = 1;
                nest.PlateDefaults.PartSpacing = 1;
                nest.PlateDefaults.EdgeSpacing = new Spacing(1, 1, 1, 1);

                foreach (var matched in group.Parts)
                {
                    if (string.IsNullOrWhiteSpace(matched.DxfPath) || !File.Exists(matched.DxfPath))
                    {
                        importErrors.Add($"{matched.Item.FileName}: DXF file not found");
                        continue;
                    }

                    try
                    {
                        var result = importer.Import(matched.DxfPath);

                        var drawingName = Path.GetFileNameWithoutExtension(matched.DxfPath);
                        var drawing = new Drawing(drawingName);
                        drawing.Source.Path = matched.DxfPath;
                        drawing.Quantity.Required = matched.Item.Qty ?? 1;
                        drawing.Material = new Material(group.Material);

                        var pgm = ConvertGeometry.ToProgram(result.Entities);

                        if (pgm.Codes.Count > 0 && pgm[0].Type == CodeType.RapidMove)
                        {
                            var rapid = (RapidMove)pgm[0];
                            drawing.Source.Offset = rapid.EndPoint;
                            pgm.Offset(-rapid.EndPoint);
                            pgm.Codes.RemoveAt(0);
                        }

                        drawing.Program = pgm;
                        nest.Drawings.Add(drawing);
                    }
                    catch (Exception ex)
                    {
                        importErrors.Add($"{matched.Item.FileName}: {ex.Message}");
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

        private void BtnClose_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
