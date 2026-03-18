# Pattern Tile Layout Window Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a tool window where the user selects two drawings, arranges them as a unit cell, and tiles the pattern across a configurable plate with an option to apply the result.

**Architecture:** A `PatternTileForm` dialog with a horizontal `SplitContainer` — left panel is a `PlateView` for unit cell editing (plate outline hidden via zero-size plate), right panel is a read-only `PlateView` for tile preview. A `PatternTiler` helper in Engine handles the tiling math (deliberate addition beyond the spec for separation of concerns — the spec says "no engine changes" but the tiler is pure logic with no side-effects). The form is opened from the Tools menu and returns a result to `EditNestForm` for placement.

**Tech Stack:** WinForms (.NET 8), existing `PlateView`/`DrawControl` controls, `Compactor.Push` (angle-based overload), `Part.CloneAtOffset`

**Spec:** `docs/superpowers/specs/2026-03-18-pattern-tile-layout-design.md`

---

### Task 1: PatternTiler — tiling algorithm in Engine

The pure logic component that takes a unit cell (list of parts) and tiles it across a plate work area. No UI dependency.

**Files:**
- Create: `OpenNest.Engine/PatternTiler.cs`
- Test: `OpenNest.Tests/PatternTilerTests.cs`

- [ ] **Step 1: Write failing tests for PatternTiler**

```csharp
// OpenNest.Tests/PatternTilerTests.cs
using OpenNest;
using OpenNest.Engine;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class PatternTilerTests
{
    private static Drawing MakeSquareDrawing(double size)
    {
        var pgm = new CNC.Program();
        pgm.Add(new CNC.LinearMove(size, 0));
        pgm.Add(new CNC.LinearMove(size, size));
        pgm.Add(new CNC.LinearMove(0, size));
        pgm.Add(new CNC.LinearMove(0, 0));
        return new Drawing("square", pgm);
    }

    [Fact]
    public void Tile_SinglePart_FillsGrid()
    {
        var drawing = MakeSquareDrawing(10);
        var cell = new List<Part> { Part.CreateAtOrigin(drawing) };
        // Size(width=X, length=Y) — 30 wide, 20 tall
        var plateSize = new Size(30, 20);
        var partSpacing = 0.0;

        var result = PatternTiler.Tile(cell, plateSize, partSpacing);

        // 3 columns (30/10) x 2 rows (20/10) = 6 parts
        Assert.Equal(6, result.Count);

        // Verify all parts are within plate bounds
        foreach (var part in result)
        {
            Assert.True(part.BoundingBox.Right <= plateSize.Width + 0.001);
            Assert.True(part.BoundingBox.Top <= plateSize.Length + 0.001);
            Assert.True(part.BoundingBox.Left >= -0.001);
            Assert.True(part.BoundingBox.Bottom >= -0.001);
        }
    }

    [Fact]
    public void Tile_TwoParts_TilesUnitCell()
    {
        var drawing = MakeSquareDrawing(10);
        var partA = Part.CreateAtOrigin(drawing);
        var partB = Part.CreateAtOrigin(drawing);
        partB.Offset(10, 0); // side by side, cell = 20x10

        var cell = new List<Part> { partA, partB };
        var plateSize = new Size(40, 20);
        var partSpacing = 0.0;

        var result = PatternTiler.Tile(cell, plateSize, partSpacing);

        // 2 columns (40/20) x 2 rows (20/10) = 4 cells x 2 parts = 8
        Assert.Equal(8, result.Count);
    }

    [Fact]
    public void Tile_WithSpacing_ReducesCount()
    {
        var drawing = MakeSquareDrawing(10);
        var cell = new List<Part> { Part.CreateAtOrigin(drawing) };
        var plateSize = new Size(30, 20);
        var partSpacing = 2.0;

        var result = PatternTiler.Tile(cell, plateSize, partSpacing);

        // cell width = 10 + 2 = 12, cols = floor(30/12) = 2
        // cell height = 10 + 2 = 12, rows = floor(20/12) = 1
        // 2 x 1 = 2 parts
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Tile_EmptyCell_ReturnsEmpty()
    {
        var result = PatternTiler.Tile(new List<Part>(), new Size(100, 100), 0);
        Assert.Empty(result);
    }

    [Fact]
    public void Tile_NonSquarePlate_CorrectAxes()
    {
        var drawing = MakeSquareDrawing(10);
        var cell = new List<Part> { Part.CreateAtOrigin(drawing) };
        // Wide plate: 50 in X (Width), 10 in Y (Length) — should fit 5x1
        var plateSize = new Size(50, 10);

        var result = PatternTiler.Tile(cell, plateSize, 0);

        Assert.Equal(5, result.Count);

        // Verify parts span the X axis, not Y
        var maxRight = result.Max(p => p.BoundingBox.Right);
        var maxTop = result.Max(p => p.BoundingBox.Top);
        Assert.True(maxRight <= 50.001);
        Assert.True(maxTop <= 10.001);
    }

    [Fact]
    public void Tile_CellLargerThanPlate_ReturnsSingleCell()
    {
        var drawing = MakeSquareDrawing(50);
        var cell = new List<Part> { Part.CreateAtOrigin(drawing) };
        var plateSize = new Size(30, 30);

        var result = PatternTiler.Tile(cell, plateSize, 0);

        // Cell doesn't fit at all — 0 parts
        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~PatternTilerTests" -v n`
Expected: Build error — `PatternTiler` does not exist.

- [ ] **Step 3: Implement PatternTiler**

```csharp
// OpenNest.Engine/PatternTiler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Engine
{
    public static class PatternTiler
    {
        /// <summary>
        /// Tiles a unit cell across a plate, returning cloned parts.
        /// </summary>
        /// <param name="cell">The unit cell parts (positioned relative to each other).</param>
        /// <param name="plateSize">The plate size to tile across.</param>
        /// <param name="partSpacing">Spacing to add around each cell.</param>
        /// <returns>List of cloned parts filling the plate.</returns>
        public static List<Part> Tile(List<Part> cell, Size plateSize, double partSpacing)
        {
            if (cell == null || cell.Count == 0)
                return new List<Part>();

            var cellBox = cell.GetBoundingBox();
            var halfSpacing = partSpacing / 2;

            var cellWidth = cellBox.Width + partSpacing;
            var cellHeight = cellBox.Length + partSpacing;

            if (cellWidth <= 0 || cellHeight <= 0)
                return new List<Part>();

            // Size.Width = X-axis, Size.Length = Y-axis
            var cols = (int)System.Math.Floor(plateSize.Width / cellWidth);
            var rows = (int)System.Math.Floor(plateSize.Length / cellHeight);

            if (cols <= 0 || rows <= 0)
                return new List<Part>();

            // Offset to normalize cell origin to (halfSpacing, halfSpacing)
            var cellOrigin = cellBox.Location;
            var baseOffset = new Vector(halfSpacing - cellOrigin.X, halfSpacing - cellOrigin.Y);

            var result = new List<Part>(cols * rows * cell.Count);

            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    var tileOffset = baseOffset + new Vector(col * cellWidth, row * cellHeight);

                    foreach (var part in cell)
                    {
                        result.Add(part.CloneAtOffset(tileOffset));
                    }
                }
            }

            return result;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~PatternTilerTests" -v n`
Expected: All 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/PatternTiler.cs OpenNest.Tests/PatternTilerTests.cs
git commit -m "feat(engine): add PatternTiler for unit cell tiling across plates"
```

---

### Task 2: PatternTileForm — the dialog window

The WinForms dialog with split layout, drawing pickers, plate size controls, and the two `PlateView` panels. This task builds the form shell and layout — interaction logic comes in Task 3.

**Files:**
- Create: `OpenNest/Forms/PatternTileForm.cs`
- Create: `OpenNest/Forms/PatternTileForm.Designer.cs`

**Key reference files:**
- `OpenNest/Forms/BestFitViewerForm.cs` — similar standalone tool form pattern
- `OpenNest/Controls/PlateView.cs` — the control used in both panels
- `OpenNest/Forms/EditPlateForm.cs` — plate size input pattern

- [ ] **Step 1: Create PatternTileForm.Designer.cs**

```csharp
// OpenNest/Forms/PatternTileForm.Designer.cs
namespace OpenNest.Forms
{
    partial class PatternTileForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.topPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.lblDrawingA = new System.Windows.Forms.Label();
            this.cboDrawingA = new System.Windows.Forms.ComboBox();
            this.lblDrawingB = new System.Windows.Forms.Label();
            this.cboDrawingB = new System.Windows.Forms.ComboBox();
            this.lblPlateSize = new System.Windows.Forms.Label();
            this.txtPlateSize = new System.Windows.Forms.TextBox();
            this.lblPartSpacing = new System.Windows.Forms.Label();
            this.nudPartSpacing = new System.Windows.Forms.NumericUpDown();
            this.btnAutoArrange = new System.Windows.Forms.Button();
            this.btnApply = new System.Windows.Forms.Button();
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudPartSpacing)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.SuspendLayout();
            this.SuspendLayout();
            //
            // topPanel — FlowLayoutPanel for correct left-to-right ordering
            //
            this.topPanel.Controls.Add(this.lblDrawingA);
            this.topPanel.Controls.Add(this.cboDrawingA);
            this.topPanel.Controls.Add(this.lblDrawingB);
            this.topPanel.Controls.Add(this.cboDrawingB);
            this.topPanel.Controls.Add(this.lblPlateSize);
            this.topPanel.Controls.Add(this.txtPlateSize);
            this.topPanel.Controls.Add(this.lblPartSpacing);
            this.topPanel.Controls.Add(this.nudPartSpacing);
            this.topPanel.Controls.Add(this.btnAutoArrange);
            this.topPanel.Controls.Add(this.btnApply);
            this.topPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.topPanel.Height = 36;
            this.topPanel.Padding = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.topPanel.WrapContents = false;
            //
            // lblDrawingA
            //
            this.lblDrawingA.Text = "Drawing A:";
            this.lblDrawingA.AutoSize = true;
            this.lblDrawingA.Margin = new System.Windows.Forms.Padding(3, 5, 0, 0);
            //
            // cboDrawingA
            //
            this.cboDrawingA.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboDrawingA.Width = 130;
            //
            // lblDrawingB
            //
            this.lblDrawingB.Text = "Drawing B:";
            this.lblDrawingB.AutoSize = true;
            this.lblDrawingB.Margin = new System.Windows.Forms.Padding(10, 5, 0, 0);
            //
            // cboDrawingB
            //
            this.cboDrawingB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboDrawingB.Width = 130;
            //
            // lblPlateSize
            //
            this.lblPlateSize.Text = "Plate Size:";
            this.lblPlateSize.AutoSize = true;
            this.lblPlateSize.Margin = new System.Windows.Forms.Padding(10, 5, 0, 0);
            //
            // txtPlateSize
            //
            this.txtPlateSize.Width = 80;
            //
            // lblPartSpacing
            //
            this.lblPartSpacing.Text = "Spacing:";
            this.lblPartSpacing.AutoSize = true;
            this.lblPartSpacing.Margin = new System.Windows.Forms.Padding(10, 5, 0, 0);
            //
            // nudPartSpacing
            //
            this.nudPartSpacing.Width = 60;
            this.nudPartSpacing.DecimalPlaces = 2;
            this.nudPartSpacing.Increment = 0.25m;
            this.nudPartSpacing.Maximum = 100;
            this.nudPartSpacing.Minimum = 0;
            //
            // btnAutoArrange
            //
            this.btnAutoArrange.Text = "Auto-Arrange";
            this.btnAutoArrange.Width = 100;
            this.btnAutoArrange.Margin = new System.Windows.Forms.Padding(10, 0, 0, 0);
            //
            // btnApply
            //
            this.btnApply.Text = "Apply...";
            this.btnApply.Width = 80;
            //
            // splitContainer
            //
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.SplitterDistance = 350;
            //
            // PatternTileForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(900, 550);
            this.Controls.Add(this.splitContainer);
            this.Controls.Add(this.topPanel);
            this.MinimumSize = new System.Drawing.Size(700, 400);
            this.Name = "PatternTileForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Pattern Tile Layout";
            this.topPanel.ResumeLayout(false);
            this.topPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudPartSpacing)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.FlowLayoutPanel topPanel;
        private System.Windows.Forms.Label lblDrawingA;
        private System.Windows.Forms.ComboBox cboDrawingA;
        private System.Windows.Forms.Label lblDrawingB;
        private System.Windows.Forms.ComboBox cboDrawingB;
        private System.Windows.Forms.Label lblPlateSize;
        private System.Windows.Forms.TextBox txtPlateSize;
        private System.Windows.Forms.Label lblPartSpacing;
        private System.Windows.Forms.NumericUpDown nudPartSpacing;
        private System.Windows.Forms.Button btnAutoArrange;
        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.SplitContainer splitContainer;
    }
}
```

- [ ] **Step 2: Create PatternTileForm.cs — form shell with PlateViews**

```csharp
// OpenNest/Forms/PatternTileForm.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.Forms
{
    public enum PatternTileTarget
    {
        CurrentPlate,
        NewPlate
    }

    public class PatternTileResult
    {
        public List<Part> Parts { get; set; }
        public PatternTileTarget Target { get; set; }
        public Size PlateSize { get; set; }
    }

    public partial class PatternTileForm : Form
    {
        private readonly Nest nest;
        private readonly PlateView cellView;
        private readonly PlateView previewView;

        public PatternTileResult Result { get; private set; }

        public PatternTileForm(Nest nest)
        {
            this.nest = nest;
            InitializeComponent();

            // Unit cell editor — plate outline hidden via zero-size plate
            cellView = new PlateView();
            cellView.Plate.Size = new Size(0, 0);
            cellView.Plate.Quantity = 0; // prevent Drawing.Quantity.Nested side-effects
            cellView.DrawOrigin = false;
            cellView.DrawBounds = false; // hide selection bounding box overlay
            cellView.Dock = DockStyle.Fill;
            splitContainer.Panel1.Controls.Add(cellView);

            // Tile preview — plate visible, read-only
            previewView = new PlateView();
            previewView.Plate.Quantity = 0; // prevent Drawing.Quantity.Nested side-effects
            previewView.AllowSelect = false;
            previewView.AllowDrop = false;
            previewView.DrawBounds = false;
            previewView.Dock = DockStyle.Fill;
            splitContainer.Panel2.Controls.Add(previewView);

            // Populate drawing dropdowns
            var drawings = nest.Drawings.OrderBy(d => d.Name).ToList();
            cboDrawingA.Items.Add("(none)");
            cboDrawingB.Items.Add("(none)");
            foreach (var d in drawings)
            {
                cboDrawingA.Items.Add(d);
                cboDrawingB.Items.Add(d);
            }
            cboDrawingA.SelectedIndex = 0;
            cboDrawingB.SelectedIndex = 0;

            // Default plate size from nest defaults
            var defaults = nest.PlateDefaults;
            txtPlateSize.Text = defaults.Size.ToString();
            nudPartSpacing.Value = (decimal)defaults.PartSpacing;

            // Wire events
            cboDrawingA.SelectedIndexChanged += OnDrawingChanged;
            cboDrawingB.SelectedIndexChanged += OnDrawingChanged;
            txtPlateSize.TextChanged += OnPlateSettingsChanged;
            nudPartSpacing.ValueChanged += OnPlateSettingsChanged;
            btnAutoArrange.Click += OnAutoArrangeClick;
            btnApply.Click += OnApplyClick;
            cellView.MouseUp += OnCellMouseUp;
        }

        private Drawing SelectedDrawingA =>
            cboDrawingA.SelectedItem as Drawing;

        private Drawing SelectedDrawingB =>
            cboDrawingB.SelectedItem as Drawing;

        private double PartSpacing =>
            (double)nudPartSpacing.Value;

        private bool TryGetPlateSize(out Size size)
        {
            return Size.TryParse(txtPlateSize.Text, out size);
        }

        private void OnDrawingChanged(object sender, EventArgs e)
        {
            RebuildCell();
            RebuildPreview();
        }

        private void OnPlateSettingsChanged(object sender, EventArgs e)
        {
            UpdatePreviewPlateSize();
            RebuildPreview();
        }

        private void OnCellMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && cellView.Plate.Parts.Count == 2)
            {
                CompactCellParts();
            }

            RebuildPreview();
        }

        private void RebuildCell()
        {
            cellView.Plate.Parts.Clear();

            var drawingA = SelectedDrawingA;
            var drawingB = SelectedDrawingB;

            if (drawingA == null && drawingB == null)
                return;

            if (drawingA != null)
            {
                var partA = Part.CreateAtOrigin(drawingA);
                cellView.Plate.Parts.Add(partA);
            }

            if (drawingB != null)
            {
                var partB = Part.CreateAtOrigin(drawingB);

                // Place B to the right of A (or at origin if A is null)
                if (drawingA != null && cellView.Plate.Parts.Count > 0)
                {
                    var aBox = cellView.Plate.Parts[0].BoundingBox;
                    partB.Offset(aBox.Right + PartSpacing, 0);
                }

                cellView.Plate.Parts.Add(partB);
            }

            cellView.ZoomToFit();
        }

        private void CompactCellParts()
        {
            var parts = cellView.Plate.Parts.ToList();
            if (parts.Count < 2)
                return;

            var combinedBox = parts.GetBoundingBox();
            var centroid = combinedBox.Center;
            var syntheticWorkArea = new Box(-10000, -10000, 20000, 20000);

            for (var iteration = 0; iteration < 10; iteration++)
            {
                var totalMoved = 0.0;

                foreach (var part in parts)
                {
                    var partCenter = part.BoundingBox.Center;
                    var dx = centroid.X - partCenter.X;
                    var dy = centroid.Y - partCenter.Y;
                    var dist = System.Math.Sqrt(dx * dx + dy * dy);

                    if (dist < 0.01)
                        continue;

                    var angle = System.Math.Atan2(dy, dx);
                    var single = new List<Part> { part };
                    var obstacles = parts.Where(p => p != part).ToList();

                    totalMoved += Compactor.Push(single, obstacles,
                        syntheticWorkArea, PartSpacing, angle);
                }

                if (totalMoved < 0.01)
                    break;
            }

            cellView.Refresh();
        }

        private void UpdatePreviewPlateSize()
        {
            if (TryGetPlateSize(out var size))
                previewView.Plate.Size = size;
        }

        private void RebuildPreview()
        {
            previewView.Plate.Parts.Clear();

            if (!TryGetPlateSize(out var plateSize))
                return;

            previewView.Plate.Size = plateSize;
            previewView.Plate.PartSpacing = PartSpacing;

            var cellParts = cellView.Plate.Parts.ToList();
            if (cellParts.Count == 0)
                return;

            var tiled = Engine.PatternTiler.Tile(cellParts, plateSize, PartSpacing);

            foreach (var part in tiled)
                previewView.Plate.Parts.Add(part);

            previewView.ZoomToFit();
        }

        private void OnAutoArrangeClick(object sender, EventArgs e)
        {
            var drawingA = SelectedDrawingA;
            var drawingB = SelectedDrawingB;

            if (drawingA == null || drawingB == null)
                return;

            if (!TryGetPlateSize(out var plateSize))
                return;

            Cursor = Cursors.WaitCursor;
            try
            {
                var angles = new[] { 0.0, Math.Angle.ToRadians(90), Math.Angle.ToRadians(180), Math.Angle.ToRadians(270) };
                var bestCell = (List<Part>)null;
                var bestArea = double.MaxValue;

                foreach (var angleA in angles)
                {
                    foreach (var angleB in angles)
                    {
                        var partA = Part.CreateAtOrigin(drawingA, angleA);
                        var partB = Part.CreateAtOrigin(drawingB, angleB);
                        partB.Offset(partA.BoundingBox.Right + PartSpacing, 0);

                        var cell = new List<Part> { partA, partB };

                        // Compact toward center
                        var box = cell.GetBoundingBox();
                        var centroid = box.Center;
                        var syntheticWorkArea = new Box(-10000, -10000, 20000, 20000);

                        for (var i = 0; i < 10; i++)
                        {
                            var moved = 0.0;
                            foreach (var part in cell)
                            {
                                var pc = part.BoundingBox.Center;
                                var dx = centroid.X - pc.X;
                                var dy = centroid.Y - pc.Y;
                                if (System.Math.Sqrt(dx * dx + dy * dy) < 0.01)
                                    continue;

                                var angle = System.Math.Atan2(dy, dx);
                                var single = new List<Part> { part };
                                var obstacles = cell.Where(p => p != part).ToList();
                                moved += Compactor.Push(single, obstacles, syntheticWorkArea, PartSpacing, angle);
                            }
                            if (moved < 0.01) break;
                        }

                        var finalBox = cell.GetBoundingBox();
                        var area = finalBox.Width * finalBox.Length;

                        if (area < bestArea)
                        {
                            bestArea = area;
                            bestCell = cell;
                        }
                    }
                }

                if (bestCell != null)
                {
                    cellView.Plate.Parts.Clear();
                    foreach (var part in bestCell)
                        cellView.Plate.Parts.Add(part);
                    cellView.ZoomToFit();
                    RebuildPreview();
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            if (previewView.Plate.Parts.Count == 0)
                return;

            if (!TryGetPlateSize(out var plateSize))
                return;

            var choice = MessageBox.Show(
                "Apply pattern to current plate?\n\nYes = Current plate (clears existing parts)\nNo = New plate",
                "Apply Pattern",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
                return;

            // Rebuild a fresh set of tiled parts for the caller
            var cellParts = cellView.Plate.Parts.ToList();
            var tiledParts = Engine.PatternTiler.Tile(cellParts, plateSize, PartSpacing);

            Result = new PatternTileResult
            {
                Parts = tiledParts,
                Target = choice == DialogResult.Yes
                    ? PatternTileTarget.CurrentPlate
                    : PatternTileTarget.NewPlate,
                PlateSize = plateSize
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add OpenNest/Forms/PatternTileForm.cs OpenNest/Forms/PatternTileForm.Designer.cs
git commit -m "feat(ui): add PatternTileForm dialog with unit cell editor and tile preview"
```

---

### Task 3: Wire up menu entry and apply logic in MainForm

Add a "Pattern Tile" menu item under Tools, wire it to open `PatternTileForm`, and handle the result by placing parts on the target plate.

**Files:**
- Modify: `OpenNest/Forms/MainForm.Designer.cs` — add menu item
- Modify: `OpenNest/Forms/MainForm.cs` — add click handler and apply logic

- [ ] **Step 1: Add menu item to MainForm.Designer.cs**

In the `InitializeComponent` method:

1. Add field declaration at end of class (near the other `mnuTools*` fields):
```csharp
private System.Windows.Forms.ToolStripMenuItem mnuToolsPatternTile;
```

2. In `InitializeComponent`, add initialization (near the other `mnuTools*` instantiations):
```csharp
this.mnuToolsPatternTile = new System.Windows.Forms.ToolStripMenuItem();
```

3. Add to the `mnuTools.DropDownItems` array — insert `mnuToolsPatternTile` after `mnuToolsBestFitViewer`:
```csharp
mnuTools.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
    mnuToolsMeasureArea, mnuToolsBestFitViewer, mnuToolsPatternTile, mnuToolsAlign,
    toolStripMenuItem14, mnuSetOffsetIncrement, mnuSetRotationIncrement,
    toolStripMenuItem15, mnuToolsOptions });
```

4. Add configuration block:
```csharp
// mnuToolsPatternTile
this.mnuToolsPatternTile.Name = "mnuToolsPatternTile";
this.mnuToolsPatternTile.Size = new System.Drawing.Size(214, 22);
this.mnuToolsPatternTile.Text = "Pattern Tile";
this.mnuToolsPatternTile.Click += PatternTile_Click;
```

- [ ] **Step 2: Add click handler and apply logic to MainForm.cs**

Add in the `#region Tools Menu Events` section, after `BestFitViewer_Click`:

```csharp
private void PatternTile_Click(object sender, EventArgs e)
{
    if (activeForm == null)
        return;

    if (activeForm.Nest.Drawings.Count == 0)
    {
        MessageBox.Show("No drawings available.", "Pattern Tile",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
    }

    using (var form = new PatternTileForm(activeForm.Nest))
    {
        if (form.ShowDialog(this) != DialogResult.OK || form.Result == null)
            return;

        var result = form.Result;

        if (result.Target == PatternTileTarget.CurrentPlate)
        {
            activeForm.PlateView.Plate.Parts.Clear();

            foreach (var part in result.Parts)
                activeForm.PlateView.Plate.Parts.Add(part);

            activeForm.PlateView.ZoomToFit();
        }
        else
        {
            var plate = activeForm.Nest.CreatePlate();
            plate.Size = result.PlateSize;

            foreach (var part in result.Parts)
                plate.Parts.Add(part);

            activeForm.LoadLastPlate();
        }

        activeForm.Nest.UpdateDrawingQuantities();
    }
}
```

- [ ] **Step 3: Build and manually test**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds. Launch the app, create a nest, import some DXFs, then Tools > Pattern Tile opens the form.

- [ ] **Step 4: Commit**

```bash
git add OpenNest/Forms/MainForm.cs OpenNest/Forms/MainForm.Designer.cs
git commit -m "feat(ui): wire Pattern Tile menu item and apply logic in MainForm"
```

---

### Task 4: Manual testing and polish

Final integration testing and any adjustments.

**Files:**
- Possibly modify: `OpenNest/Forms/PatternTileForm.cs`, `OpenNest/Forms/PatternTileForm.Designer.cs`

- [ ] **Step 1: End-to-end test workflow**

1. Launch the app, create a new nest
2. Import 2+ DXF drawings
3. Open Tools > Pattern Tile
4. Select Drawing A and Drawing B
5. Verify parts appear in left panel, can be dragged
6. Verify compaction on mouse release closes gaps
7. Verify tile preview updates on the right
8. Change plate size — verify preview updates
9. Change spacing — verify preview updates
10. Click Auto-Arrange — verify it picks a tight arrangement
11. Click Apply > Yes (current plate) — verify parts placed
12. Reopen, Apply > No (new plate) — verify new plate created with parts
13. Test single drawing only (one dropdown set, other on "(none)")
14. Test same drawing in both dropdowns

- [ ] **Step 2: Fix any issues found during testing**

Address any layout, interaction, or rendering issues discovered.

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "fix(ui): polish PatternTileForm after manual testing"
```
