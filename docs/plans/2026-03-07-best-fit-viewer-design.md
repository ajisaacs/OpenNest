# Best-Fit Viewer Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a BestFitViewerForm that shows all pair candidates in a dense 5-column grid with metadata overlay, similar to PEP's best-fit viewer.

**Architecture:** A modal `Form` with a scrollable `TableLayoutPanel` (5 columns). Each cell is a read-only `PlateView` with the pair's two parts placed on it. Metadata is painted as overlay text on each cell. Dropped candidates use a different background color. Invoked from Tools menu when a drawing and plate are available.

**Tech Stack:** WinForms, `BestFitFinder` from `OpenNest.Engine.BestFit`, `PlateView` control.

---

### Task 1: Extract BuildPairParts to a static helper

`NestEngine.BuildPairParts` is private and contains the pair-building logic we need. Extract it to a public static method so both `NestEngine` and the new form can use it.

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs`

**Step 1: Make BuildPairParts internal static**

In `OpenNest.Engine/NestEngine.cs`, change the method signature from private instance to internal static. It doesn't use any instance state — only `BestFitResult` and `Drawing` parameters.

Change:
```csharp
private List<Part> BuildPairParts(BestFitResult bestFit, Drawing drawing)
```
To:
```csharp
internal static List<Part> BuildPairParts(BestFitResult bestFit, Drawing drawing)
```

**Step 2: Build and verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds with no errors.

**Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "refactor: make BuildPairParts internal static for reuse"
```

---

### Task 2: Create BestFitViewerForm

**Files:**
- Create: `OpenNest/Forms/BestFitViewerForm.cs`
- Create: `OpenNest/Forms/BestFitViewerForm.Designer.cs`

**Step 1: Create the Designer file**

Create `OpenNest/Forms/BestFitViewerForm.Designer.cs` with a `TableLayoutPanel` (5 columns, auto-scroll, dock-fill) inside the form. Form should be sizable, start centered on parent, ~1200x800 default size, title "Best-Fit Viewer".

```csharp
namespace OpenNest.Forms
{
    partial class BestFitViewerForm
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
            this.gridPanel = new System.Windows.Forms.TableLayoutPanel();
            this.SuspendLayout();
            //
            // gridPanel
            //
            this.gridPanel.AutoScroll = true;
            this.gridPanel.ColumnCount = 5;
            this.gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.gridPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridPanel.Location = new System.Drawing.Point(0, 0);
            this.gridPanel.Name = "gridPanel";
            this.gridPanel.RowCount = 1;
            this.gridPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.gridPanel.Size = new System.Drawing.Size(1200, 800);
            this.gridPanel.TabIndex = 0;
            //
            // BestFitViewerForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 800);
            this.Controls.Add(this.gridPanel);
            this.KeyPreview = true;
            this.Name = "BestFitViewerForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Best-Fit Viewer";
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TableLayoutPanel gridPanel;
    }
}
```

**Step 2: Create the code-behind file**

Create `OpenNest/Forms/BestFitViewerForm.cs`. The constructor takes a `Drawing` and a `Plate`. It calls `BestFitFinder.FindBestFits()` to get all candidates, then for each result:
1. Creates a `PlateView` configured read-only (no pan/zoom/select/origin, no plate outline)
2. Sizes the PlateView's plate to the pair bounding box
3. Builds pair parts via `NestEngine.BuildPairParts()` and adds them to the plate
4. Sets background color based on `Keep` (kept = default, dropped = maroon)
5. Subscribes to `Paint` to overlay metadata text

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Forms
{
    public partial class BestFitViewerForm : Form
    {
        private static readonly Color KeptColor = Color.FromArgb(0, 0, 100);
        private static readonly Color DroppedColor = Color.FromArgb(100, 0, 0);

        public BestFitViewerForm(Drawing drawing, Plate plate)
        {
            InitializeComponent();
            PopulateGrid(drawing, plate);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void PopulateGrid(Drawing drawing, Plate plate)
        {
            var finder = new BestFitFinder(plate.Size.Width, plate.Size.Height);
            var results = finder.FindBestFits(drawing, plate.PartSpacing);

            var rows = (int)System.Math.Ceiling(results.Count / 5.0);
            gridPanel.RowCount = rows;
            gridPanel.RowStyles.Clear();

            for (var i = 0; i < rows; i++)
                gridPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));

            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                var view = CreateCellView(result, drawing);
                gridPanel.Controls.Add(view, i % 5, i / 5);
            }
        }

        private PlateView CreateCellView(BestFitResult result, Drawing drawing)
        {
            var bgColor = result.Keep ? KeptColor : DroppedColor;

            var colorScheme = new ColorScheme
            {
                BackgroundColor = bgColor,
                LayoutOutlineColor = bgColor,
                LayoutFillColor = bgColor,
                BoundingBoxColor = bgColor,
                RapidColor = Color.DodgerBlue,
                OriginColor = bgColor,
                EdgeSpacingColor = bgColor
            };

            var view = new PlateView(colorScheme);
            view.DrawOrigin = false;
            view.DrawBounds = false;
            view.AllowPan = false;
            view.AllowSelect = false;
            view.AllowZoom = false;
            view.AllowDrop = false;
            view.Dock = DockStyle.Fill;
            view.Plate.Size = new Geometry.Size(
                result.BoundingWidth,
                result.BoundingHeight);

            var parts = NestEngine.BuildPairParts(result, drawing);

            foreach (var part in parts)
                view.Plate.Parts.Add(part);

            view.Paint += (sender, e) =>
            {
                PaintMetadata(e.Graphics, view, result);
            };

            view.HandleCreated += (sender, e) =>
            {
                view.ZoomToFit(true);
            };

            return view;
        }

        private void PaintMetadata(Graphics g, PlateView view, BestFitResult result)
        {
            var font = view.Font;
            var brush = Brushes.White;
            var y = 2f;
            var lineHeight = font.GetHeight(g) + 1;

            var lines = new[]
            {
                string.Format("RotatedArea={0:F4}", result.RotatedArea),
                string.Format("{0:F4}x{1:F4}={2:F4}",
                    result.BoundingWidth, result.BoundingHeight, result.RotatedArea),
                string.Format("Why={0}", result.Keep ? "0" : result.Reason),
                string.Format("Type={0}  Test={1}  Spacing={2}",
                    result.Candidate.StrategyType,
                    result.Candidate.TestNumber,
                    result.Candidate.Spacing),
                string.Format("Util={0:P0}  Rot={1:F1}°",
                    result.Utilization,
                    Angle.ToDegrees(result.OptimalRotation))
            };

            foreach (var line in lines)
            {
                g.DrawString(line, font, brush, 2, y);
                y += lineHeight;
            }
        }
    }
}
```

**Step 3: Build and verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add OpenNest/Forms/BestFitViewerForm.cs OpenNest/Forms/BestFitViewerForm.Designer.cs
git commit -m "feat: add BestFitViewerForm with pair candidate grid"
```

---

### Task 3: Add menu item to MainForm

**Files:**
- Modify: `OpenNest/Forms/MainForm.Designer.cs`
- Modify: `OpenNest/Forms/MainForm.cs`

**Step 1: Add the menu item field and wire it up in Designer**

In `MainForm.Designer.cs`:

1. Add field declaration near the other `mnuTools*` fields (~line 1198):
```csharp
private System.Windows.Forms.ToolStripMenuItem mnuToolsBestFitViewer;
```

2. Add instantiation in `InitializeComponent()` near other mnuTools instantiations (~line 62):
```csharp
this.mnuToolsBestFitViewer = new System.Windows.Forms.ToolStripMenuItem();
```

3. Add to the Tools menu `DropDownItems` array (after `mnuToolsMeasureArea`, ~line 413-420). Insert `mnuToolsBestFitViewer` before the `toolStripMenuItem14` separator:
```csharp
this.mnuTools.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
this.mnuToolsMeasureArea,
this.mnuToolsBestFitViewer,
this.mnuToolsAlign,
this.toolStripMenuItem14,
this.mnuSetOffsetIncrement,
this.mnuSetRotationIncrement,
this.toolStripMenuItem15,
this.mnuToolsOptions});
```

4. Add menu item configuration after the `mnuToolsMeasureArea` block (~line 431):
```csharp
//
// mnuToolsBestFitViewer
//
this.mnuToolsBestFitViewer.Name = "mnuToolsBestFitViewer";
this.mnuToolsBestFitViewer.Size = new System.Drawing.Size(214, 22);
this.mnuToolsBestFitViewer.Text = "Best-Fit Viewer";
this.mnuToolsBestFitViewer.Click += new System.EventHandler(this.BestFitViewer_Click);
```

**Step 2: Add the click handler in MainForm.cs**

Add a method to `MainForm.cs` that opens the form. It needs the active `EditNestForm` to get the current plate and a selected drawing. If no drawing is available from the selected plate's parts, show a message.

```csharp
private void BestFitViewer_Click(object sender, EventArgs e)
{
    if (activeForm == null)
        return;

    var plate = activeForm.PlateView.Plate;
    var drawing = activeForm.Nest.Drawings.Count > 0
        ? activeForm.Nest.Drawings[0]
        : null;

    if (drawing == null)
    {
        MessageBox.Show("No drawings available.", "Best-Fit Viewer",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
    }

    using (var form = new BestFitViewerForm(drawing, plate))
    {
        form.ShowDialog(this);
    }
}
```

**Step 3: Build and verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add OpenNest/Forms/MainForm.Designer.cs OpenNest/Forms/MainForm.cs
git commit -m "feat: add Best-Fit Viewer menu item under Tools"
```

---

### Task 4: Manual smoke test

**Step 1: Run the application**

Run: `dotnet run --project OpenNest`

**Step 2: Test the flow**

1. Open or create a nest file
2. Import a DXF drawing
3. Go to Tools > Best-Fit Viewer
4. Verify the grid appears with pair candidates
5. Verify kept candidates have dark blue background
6. Verify dropped candidates have dark red/maroon background
7. Verify metadata text is readable on each cell
8. Verify ESC closes the dialog
9. Verify scroll works when many results exist

**Step 3: Fix any visual issues**

Adjust cell heights, font sizes, or zoom-to-fit timing if needed.

**Step 4: Final commit**

```bash
git add -A
git commit -m "fix: polish BestFitViewerForm layout and appearance"
```
