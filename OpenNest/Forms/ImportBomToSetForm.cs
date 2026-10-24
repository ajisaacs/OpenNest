using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.IO;
using OpenNest.IO.Bom;

namespace OpenNest.Forms
{
    /// <summary>
    /// Drawing Sets &gt; More &gt; Import BOM: reads a BOM and its drawing folder
    /// (<see cref="BomImportRows"/>, <see cref="BomSetDrawings"/>), matches each drawing to the
    /// library by its identity and geometry (<see cref="DrawingSetImport"/>) and lists what
    /// that would do. Reading stores nothing. <b>Create Set</b> asks before it uploads new
    /// drawings or revisions, stores them, and returns a new set prefilled with the drawings
    /// and their BOM quantities in <see cref="Edit"/>; the set is stored only when the
    /// operator saves it in the set editor.
    /// </summary>
    public partial class ImportBomToSetForm : Form
    {
        private const string NeedsDetailsNote = "No material or thickness: set it with Edit Drawing in the set editor before making nests.";

        private readonly IDrawingSetStore sets;
        private readonly IDrawingLibraryStore library;
        private readonly Units units;
        private readonly CutType cutType;
        private readonly string savedBy;
        private BomSetDrawings drawings;
        private DrawingSetImport plan;
        private string plannedBom = "";
        private bool busy;
        private bool writing;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <param name="units">Units new library drawings are packaged in (the new-nest default).</param>
        /// <param name="cutType">Cut type of new library drawings, and part of each drawing's identity.</param>
        public ImportBomToSetForm(IDrawingSetStore sets, IDrawingLibraryStore library, Units units, CutType cutType, string savedBy)
        {
            InitializeComponent();
            GridStyle.Apply(drawingGrid);
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.units = units;
            this.cutType = cutType;
            this.savedBy = savedBy ?? "";
            ShowMessage = (text, buttons, icon) => MessageBox.Show(this, text, Text, buttons, icon);
        }

        /// <summary>The matched drawings, once the BOM has been read; null before.</summary>
        internal DrawingSetImport Plan => plan;

        /// <summary>The new set prefilled with the imported drawings, after Create Set; null before.</summary>
        public DrawingSetEditSession Edit { get; private set; }

        private async void ReadButton_Click(object sender, EventArgs e)
        {
            var path = bomBox.Text.Trim();
            if (!File.Exists(path))
            {
                ShowMessage("The BOM file does not exist.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<BomItem> items;
            try
            {
                items = await Task.Run(() =>
                {
                    using var reader = new BomReader(path);
                    return reader.GetItems();
                });
            }
            catch (Exception ex)
            {
                ShowMessage($"Could not read the BOM: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (IsDisposed)
                return;
            if (string.IsNullOrWhiteSpace(nameBox.Text))
                nameBox.Text = BomImportRows.JobName(path);
            await AnalyzeAsync(items, folderBox.Text.Trim(), Path.GetFileName(path));
        }

        /// <summary>
        /// Imports and packages the BOM's drawings, then matches them to the library and
        /// lists the outcome. Only reads from the library.
        /// </summary>
        internal async Task AnalyzeAsync(List<BomItem> items, string folder, string bomName)
        {
            if (busy)
                return;

            SetBusy(true, "Reading the drawings...");
            plan = null;
            drawingGrid.Rows.Clear();
            var customer = customerBox.Text.Trim();
            try
            {
                drawings = await Task.Run(() => BomSetDrawings.Build(BomImportRows.Build(items, folder), units));
                if (IsDisposed)
                    return;
                summaryLabel.Text = "Matching the drawings to the library...";
                plan = await DrawingSetImport.PlanAsync(library, drawings.Drawings.Select(d => ImportItem(d, customer)),
                    DrawingPackage.GeometryKey);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                // ServerTooOldException is an IOException too.
                if (!IsDisposed)
                    summaryLabel.Text = $"Could not match the drawings to the library: {ex.Message}";
                return;
            }
            finally
            {
                if (!IsDisposed)
                    SetBusy(false, null);
            }

            if (IsDisposed)
                return;
            plannedBom = bomName ?? "";
            Populate();
        }

        private DrawingSetImportItem ImportItem(BomSetDrawing drawing, string customer) =>
            new(drawing.Label, new DrawingLibraryRecord
            {
                Name = drawing.Name,
                Customer = customer,
                MaterialName = drawing.Material,
                Thickness = drawing.Thickness,
                Units = units,
                CutType = cutType,
                Settings = DrawingNestingSettings.FromDrawing(drawing.Drawing),
                SavedBy = savedBy,
            }, drawing.Package, drawing.Quantity);

        private void Populate()
        {
            drawingGrid.Rows.Clear();
            for (var i = 0; i < plan.Entries.Count; i++)
            {
                var entry = plan.Entries[i];
                var drawing = drawings.Drawings[i];
                var index = drawingGrid.Rows.Add(
                    entry.Item.Label,
                    drawing.Material,
                    drawing.Thickness > 0 ? drawing.Thickness.ToString("0.####") : "",
                    drawing.Quantity,
                    entry.Describe(),
                    entry.Action != DrawingSetImportAction.Skip && drawing.NeedsDetails ? NeedsDetailsNote : "");
                var row = drawingGrid.Rows[index];
                row.Tag = entry;
                if (entry.Action == DrawingSetImportAction.Skip)
                    row.DefaultCellStyle.ForeColor = Color.Gray;
                else if (drawing.NeedsDetails)
                    row.Cells[noteColumn.Index].ToolTipText = NeedsDetailsNote;
            }

            foreach (var skip in drawings.Skipped)
            {
                var index = drawingGrid.Rows.Add(
                    skip.Row.ItemNum is int item ? $"{skip.Name} (item {item})" : skip.Name,
                    skip.Row.Material ?? "",
                    skip.Row.Thickness is double t ? t.ToString("0.####") : "",
                    skip.Row.Qty?.ToString() ?? "",
                    "Not imported: " + skip.Reason,
                    "");
                drawingGrid.Rows[index].DefaultCellStyle.ForeColor = Color.Gray;
            }

            summaryLabel.Text = Describe(plan, drawings);
            UpdateCommands();
        }

        /// <summary>One line on what Create Set would do and what to fix later.</summary>
        internal static string Describe(DrawingSetImport plan, BomSetDrawings drawings)
        {
            var imported = plan.Imported.Count();
            var parts = new List<string>
            {
                $"{imported} drawing{(imported == 1 ? "" : "s")} for the set: "
                + $"{plan.Count(DrawingSetImportAction.Reuse)} already in the library, "
                + $"{plan.Count(DrawingSetImportAction.Revise)} new revision{(plan.Count(DrawingSetImportAction.Revise) == 1 ? "" : "s")}, "
                + $"{plan.Count(DrawingSetImportAction.Create)} new.",
            };
            var left = plan.Count(DrawingSetImportAction.Skip) + drawings.Skipped.Count;
            if (left > 0)
                parts.Add($"{left} not imported (greyed).");
            var bare = plan.Entries.Where((e, i) => e.Action != DrawingSetImportAction.Skip && drawings.Drawings[i].NeedsDetails).Count();
            if (bare > 0)
                parts.Add($"{bare} {(bare == 1 ? "has" : "have")} no material or thickness; fix {(bare == 1 ? "it" : "them")} with Edit Drawing in the set editor before making nests.");
            return string.Join(" ", parts);
        }

        private async void CreateButton_Click(object sender, EventArgs e) => await CreateSetAsync();

        /// <summary>
        /// After the operator confirms any library writes, stores them and prefills
        /// <see cref="Edit"/>; closes with OK. Returns false when nothing was done.
        /// </summary>
        internal async Task<bool> CreateSetAsync()
        {
            if (busy || plan == null || !plan.Imported.Any())
                return false;

            if (plan.HasWrites && ShowMessage(
                    "Store these in the drawing library now?" + Environment.NewLine + Environment.NewLine
                    + plan.DescribeWrites() + Environment.NewLine + Environment.NewLine
                    + "They are stored when you click OK. The set itself is stored only when you click Save in the "
                    + "set editor; if you cancel there, these library drawings stay, and importing the BOM again reuses them.",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return false;

            var edit = new DrawingSetEditSession(sets, null, savedBy)
            {
                Name = nameBox.Text.Trim(),
                Customer = customerBox.Text.Trim(),
                Notes = plannedBom.Length == 0 ? "" : $"Imported from BOM {plannedBom}.",
            };
            writing = true;
            SetBusy(true, plan.HasWrites ? "Storing the drawings in the library..." : null);
            try
            {
                await plan.ApplyAsync(library, edit);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                writing = false;
                if (IsDisposed)
                    return false;
                // The library changed part-way, so the plan no longer describes it.
                plan = null;
                SetBusy(false, "Read the BOM again to see what the library holds now.");
                ShowMessage(ex.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            busy = writing = false;
            if (IsDisposed)
                return false;
            Edit = edit;
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        private void ImportBomToSetForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Library writes in flight finish first, so the operator learns what was stored.
            if (writing && e.CloseReason == CloseReason.UserClosing)
                e.Cancel = true;
        }

        private void Inputs_Changed(object sender, EventArgs e)
        {
            // The BOM, folder and customer decide what matches; a plan for others is stale.
            if (plan == null || busy)
                return;
            plan = null;
            summaryLabel.Text = "Read the BOM again to match the drawings with these details.";
            UpdateCommands();
        }

        private void BrowseBomButton_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select BOM File",
                Filter = "Excel Files|*.xlsx|All Files|*.*",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            bomBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(folderBox.Text))
                folderBox.Text = Path.GetDirectoryName(dialog.FileName);
            nameBox.Text = BomImportRows.JobName(dialog.FileName);
        }

        private void BrowseFolderButton_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the folder of the BOM's drawings",
                SelectedPath = folderBox.Text,
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                folderBox.Text = dialog.SelectedPath;
        }

        private void SetBusy(bool value, string status)
        {
            busy = value;
            if (status != null)
                summaryLabel.Text = status;
            UseWaitCursor = value;
            UpdateCommands();
        }

        private void UpdateCommands()
        {
            readButton.Enabled = !busy;
            browseBomButton.Enabled = !busy;
            browseFolderButton.Enabled = !busy;
            cancelButton.Enabled = !writing;
            createButton.Enabled = !busy && plan != null && plan.Imported.Any();
        }
    }
}
