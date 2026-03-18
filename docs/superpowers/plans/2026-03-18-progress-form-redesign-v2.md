# NestProgressForm Redesign v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the NestProgressForm with a phase stepper, grouped panels, density sparkline, color-coded flash & fade, and Accept/Stop buttons.

**Architecture:** Four new/modified components: PhaseStepperControl (owner-drawn phase circles), DensityBar (owner-drawn sparkline), NestProgressForm (rewritten layout integrating both controls with grouped panels and dual buttons), and caller changes for Accept support. All controls use WinForms owner-draw with cached fonts/brushes.

**Tech Stack:** C# .NET 8 WinForms, System.Drawing for GDI+ rendering

**Spec:** `docs/superpowers/specs/2026-03-18-progress-form-redesign-v2-design.md`

---

### Task 1: PhaseStepperControl

**Files:**
- Create: `OpenNest/Controls/PhaseStepperControl.cs`

- [ ] **Step 1: Create the PhaseStepperControl**

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class PhaseStepperControl : UserControl
    {
        private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
        private static readonly Color GlowColor = Color.FromArgb(60, 0, 120, 212);
        private static readonly Color PendingBorder = Color.FromArgb(192, 192, 192);
        private static readonly Color LineColor = Color.FromArgb(208, 208, 208);
        private static readonly Color PendingTextColor = Color.FromArgb(153, 153, 153);
        private static readonly Color ActiveTextColor = Color.FromArgb(51, 51, 51);

        private static readonly Font LabelFont = new Font("Segoe UI", 8f, FontStyle.Regular);
        private static readonly Font BoldLabelFont = new Font("Segoe UI", 8f, FontStyle.Bold);

        private static readonly NestPhase[] Phases = (NestPhase[])Enum.GetValues(typeof(NestPhase));

        private readonly HashSet<NestPhase> visitedPhases = new();
        private NestPhase? activePhase;
        private bool isComplete;

        public PhaseStepperControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Height = 60;
        }

        public NestPhase? ActivePhase
        {
            get => activePhase;
            set
            {
                activePhase = value;
                if (value.HasValue)
                    visitedPhases.Add(value.Value);
                Invalidate();
            }
        }

        public bool IsComplete
        {
            get => isComplete;
            set
            {
                isComplete = value;
                if (value)
                {
                    foreach (var phase in Phases)
                        visitedPhases.Add(phase);
                    activePhase = null;
                }
                Invalidate();
            }
        }

        private static string GetDisplayName(NestPhase phase)
        {
            switch (phase)
            {
                case NestPhase.RectBestFit: return "BestFit";
                case NestPhase.Nfp: return "NFP";
                default: return phase.ToString();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var count = Phases.Length;
            if (count == 0) return;

            var padding = 30;
            var usableWidth = Width - padding * 2;
            var spacing = usableWidth / (count - 1);
            var circleY = 18;
            var normalRadius = 9;
            var activeRadius = 11;

            using var linePen = new Pen(LineColor, 2f);
            using var accentBrush = new SolidBrush(AccentColor);
            using var glowBrush = new SolidBrush(GlowColor);
            using var pendingPen = new Pen(PendingBorder, 2f);
            using var activeTextBrush = new SolidBrush(ActiveTextColor);
            using var pendingTextBrush = new SolidBrush(PendingTextColor);

            // Draw connecting lines
            for (var i = 0; i < count - 1; i++)
            {
                var x1 = padding + i * spacing;
                var x2 = padding + (i + 1) * spacing;
                g.DrawLine(linePen, x1, circleY, x2, circleY);
            }

            // Draw circles and labels
            for (var i = 0; i < count; i++)
            {
                var phase = Phases[i];
                var cx = padding + i * spacing;
                var isActive = activePhase == phase && !isComplete;
                var isVisited = visitedPhases.Contains(phase) || isComplete;

                if (isActive)
                {
                    // Glow
                    g.FillEllipse(glowBrush,
                        cx - activeRadius - 3, circleY - activeRadius - 3,
                        (activeRadius + 3) * 2, (activeRadius + 3) * 2);
                    // Filled circle
                    g.FillEllipse(accentBrush,
                        cx - activeRadius, circleY - activeRadius,
                        activeRadius * 2, activeRadius * 2);
                }
                else if (isVisited)
                {
                    g.FillEllipse(accentBrush,
                        cx - normalRadius, circleY - normalRadius,
                        normalRadius * 2, normalRadius * 2);
                }
                else
                {
                    g.DrawEllipse(pendingPen,
                        cx - normalRadius, circleY - normalRadius,
                        normalRadius * 2, normalRadius * 2);
                }

                // Label
                var label = GetDisplayName(phase);
                var font = isVisited || isActive ? BoldLabelFont : LabelFont;
                var brush = isVisited || isActive ? activeTextBrush : pendingTextBrush;
                var labelSize = g.MeasureString(label, font);
                g.DrawString(label, font, brush,
                    cx - labelSize.Width / 2, circleY + activeRadius + 5);
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest/OpenNest.csproj --no-restore -v q`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add OpenNest/Controls/PhaseStepperControl.cs
git commit -m "feat(ui): add PhaseStepperControl for nesting progress phases"
```

---

### Task 2: DensityBar Control

**Files:**
- Create: `OpenNest/Controls/DensityBar.cs`

- [ ] **Step 1: Create the DensityBar control**

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class DensityBar : Control
    {
        private static readonly Color TrackColor = Color.FromArgb(224, 224, 224);
        private static readonly Color LowColor = Color.FromArgb(245, 166, 35);
        private static readonly Color HighColor = Color.FromArgb(76, 175, 80);

        private double value;

        public DensityBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Size = new Size(60, 8);
        }

        public double Value
        {
            get => value;
            set
            {
                this.value = System.Math.Clamp(value, 0.0, 1.0);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            // Track background
            using var trackPath = CreateRoundedRect(rect, 4);
            using var trackBrush = new SolidBrush(TrackColor);
            g.FillPath(trackBrush, trackPath);

            // Fill
            var fillWidth = (int)(rect.Width * value);
            if (fillWidth > 0)
            {
                var fillRect = new Rectangle(rect.X, rect.Y, fillWidth, rect.Height);
                using var fillPath = CreateRoundedRect(fillRect, 4);
                using var gradientBrush = new LinearGradientBrush(
                    new Point(rect.X, 0), new Point(rect.Right, 0),
                    LowColor, HighColor);
                g.FillPath(gradientBrush, fillPath);
            }
        }

        private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest/OpenNest.csproj --no-restore -v q`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add OpenNest/Controls/DensityBar.cs
git commit -m "feat(ui): add DensityBar sparkline control for density visualization"
```

---

### Task 3: Rewrite NestProgressForm Designer

**Files:**
- Modify: `OpenNest/Forms/NestProgressForm.Designer.cs` (full rewrite)

The designer file creates the grouped layout: phase stepper at top, Results panel (Parts, Density + DensityBar, Nested), Status panel (Plate, Elapsed, Detail), and button bar (Accept + Stop).

- [ ] **Step 1: Rewrite the designer**

Replace the entire `InitializeComponent` method and field declarations. Key changes:
- Remove `phaseLabel`, `phaseValue`, `remnantLabel`, `remnantValue`, and the single flat `table`
- Add `phaseStepper` (PhaseStepperControl)
- Add `resultsPanel`, `statusPanel` (white Panels with header labels)
- Add `resultsTable`, `statusTable` (TableLayoutPanels inside panels)
- Add `densityBar` (DensityBar control, placed in density row)
- Add `acceptButton` alongside `stopButton`
- Fonts: Segoe UI 9pt bold for headers, 8.25pt bold for row labels, Consolas 8.25pt for values
- Form `ClientSize`: 450 x 315

Full designer code:

```csharp
namespace OpenNest.Forms
{
    partial class NestProgressForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            phaseStepper = new Controls.PhaseStepperControl();
            resultsPanel = new System.Windows.Forms.Panel();
            resultsHeader = new System.Windows.Forms.Label();
            resultsTable = new System.Windows.Forms.TableLayoutPanel();
            partsLabel = new System.Windows.Forms.Label();
            partsValue = new System.Windows.Forms.Label();
            densityLabel = new System.Windows.Forms.Label();
            densityPanel = new System.Windows.Forms.FlowLayoutPanel();
            densityValue = new System.Windows.Forms.Label();
            densityBar = new Controls.DensityBar();
            nestedAreaLabel = new System.Windows.Forms.Label();
            nestedAreaValue = new System.Windows.Forms.Label();
            statusPanel = new System.Windows.Forms.Panel();
            statusHeader = new System.Windows.Forms.Label();
            statusTable = new System.Windows.Forms.TableLayoutPanel();
            plateLabel = new System.Windows.Forms.Label();
            plateValue = new System.Windows.Forms.Label();
            elapsedLabel = new System.Windows.Forms.Label();
            elapsedValue = new System.Windows.Forms.Label();
            descriptionLabel = new System.Windows.Forms.Label();
            descriptionValue = new System.Windows.Forms.Label();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            acceptButton = new System.Windows.Forms.Button();
            stopButton = new System.Windows.Forms.Button();

            resultsPanel.SuspendLayout();
            resultsTable.SuspendLayout();
            densityPanel.SuspendLayout();
            statusPanel.SuspendLayout();
            statusTable.SuspendLayout();
            buttonPanel.SuspendLayout();
            SuspendLayout();

            //
            // phaseStepper
            //
            phaseStepper.Dock = System.Windows.Forms.DockStyle.Top;
            phaseStepper.Height = 60;
            phaseStepper.Name = "phaseStepper";
            phaseStepper.TabIndex = 0;

            //
            // resultsPanel
            //
            resultsPanel.BackColor = System.Drawing.Color.White;
            resultsPanel.Controls.Add(resultsTable);
            resultsPanel.Controls.Add(resultsHeader);
            resultsPanel.Dock = System.Windows.Forms.DockStyle.Top;
            resultsPanel.Location = new System.Drawing.Point(0, 60);
            resultsPanel.Margin = new System.Windows.Forms.Padding(10, 4, 10, 4);
            resultsPanel.Name = "resultsPanel";
            resultsPanel.Padding = new System.Windows.Forms.Padding(14, 10, 14, 10);
            resultsPanel.Size = new System.Drawing.Size(450, 105);
            resultsPanel.TabIndex = 1;

            //
            // resultsHeader
            //
            resultsHeader.AutoSize = true;
            resultsHeader.Dock = System.Windows.Forms.DockStyle.Top;
            resultsHeader.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            resultsHeader.ForeColor = System.Drawing.Color.FromArgb(85, 85, 85);
            resultsHeader.Name = "resultsHeader";
            resultsHeader.Padding = new System.Windows.Forms.Padding(0, 0, 0, 4);
            resultsHeader.Size = new System.Drawing.Size(63, 19);
            resultsHeader.TabIndex = 0;
            resultsHeader.Text = "RESULTS";

            //
            // resultsTable
            //
            resultsTable.AutoSize = true;
            resultsTable.ColumnCount = 2;
            resultsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            resultsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            resultsTable.Controls.Add(partsLabel, 0, 0);
            resultsTable.Controls.Add(partsValue, 1, 0);
            resultsTable.Controls.Add(densityLabel, 0, 1);
            resultsTable.Controls.Add(densityPanel, 1, 1);
            resultsTable.Controls.Add(nestedAreaLabel, 0, 2);
            resultsTable.Controls.Add(nestedAreaValue, 1, 2);
            resultsTable.Dock = System.Windows.Forms.DockStyle.Top;
            resultsTable.Name = "resultsTable";
            resultsTable.RowCount = 3;
            resultsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            resultsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            resultsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            resultsTable.TabIndex = 1;

            //
            // partsLabel
            //
            partsLabel.AutoSize = true;
            partsLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            partsLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            partsLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            partsLabel.Name = "partsLabel";
            partsLabel.Text = "Parts:";

            //
            // partsValue
            //
            partsValue.AutoSize = true;
            partsValue.Font = new System.Drawing.Font("Consolas", 8.25F);
            partsValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            partsValue.Name = "partsValue";
            partsValue.Text = "\u2014";

            //
            // densityLabel
            //
            densityLabel.AutoSize = true;
            densityLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            densityLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            densityLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            densityLabel.Name = "densityLabel";
            densityLabel.Text = "Density:";

            //
            // densityPanel
            //
            densityPanel.AutoSize = true;
            densityPanel.Controls.Add(densityValue);
            densityPanel.Controls.Add(densityBar);
            densityPanel.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            densityPanel.Margin = new System.Windows.Forms.Padding(0);
            densityPanel.Name = "densityPanel";
            densityPanel.WrapContents = false;

            //
            // densityValue
            //
            densityValue.AutoSize = true;
            densityValue.Font = new System.Drawing.Font("Consolas", 8.25F);
            densityValue.Margin = new System.Windows.Forms.Padding(0, 3, 8, 3);
            densityValue.Name = "densityValue";
            densityValue.Text = "\u2014";

            //
            // densityBar
            //
            densityBar.Margin = new System.Windows.Forms.Padding(0, 5, 0, 0);
            densityBar.Name = "densityBar";
            densityBar.Size = new System.Drawing.Size(60, 8);

            //
            // nestedAreaLabel
            //
            nestedAreaLabel.AutoSize = true;
            nestedAreaLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            nestedAreaLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            nestedAreaLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            nestedAreaLabel.Name = "nestedAreaLabel";
            nestedAreaLabel.Text = "Nested:";

            //
            // nestedAreaValue
            //
            nestedAreaValue.AutoSize = true;
            nestedAreaValue.Font = new System.Drawing.Font("Consolas", 8.25F);
            nestedAreaValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            nestedAreaValue.Name = "nestedAreaValue";
            nestedAreaValue.Text = "\u2014";

            //
            // statusPanel
            //
            statusPanel.BackColor = System.Drawing.Color.White;
            statusPanel.Controls.Add(statusTable);
            statusPanel.Controls.Add(statusHeader);
            statusPanel.Dock = System.Windows.Forms.DockStyle.Top;
            statusPanel.Location = new System.Drawing.Point(0, 169);
            statusPanel.Name = "statusPanel";
            statusPanel.Padding = new System.Windows.Forms.Padding(14, 10, 14, 10);
            statusPanel.Size = new System.Drawing.Size(450, 100);
            statusPanel.TabIndex = 2;

            //
            // statusHeader
            //
            statusHeader.AutoSize = true;
            statusHeader.Dock = System.Windows.Forms.DockStyle.Top;
            statusHeader.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            statusHeader.ForeColor = System.Drawing.Color.FromArgb(85, 85, 85);
            statusHeader.Name = "statusHeader";
            statusHeader.Padding = new System.Windows.Forms.Padding(0, 0, 0, 4);
            statusHeader.Size = new System.Drawing.Size(55, 19);
            statusHeader.TabIndex = 0;
            statusHeader.Text = "STATUS";

            //
            // statusTable
            //
            statusTable.AutoSize = true;
            statusTable.ColumnCount = 2;
            statusTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            statusTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            statusTable.Controls.Add(plateLabel, 0, 0);
            statusTable.Controls.Add(plateValue, 1, 0);
            statusTable.Controls.Add(elapsedLabel, 0, 1);
            statusTable.Controls.Add(elapsedValue, 1, 1);
            statusTable.Controls.Add(descriptionLabel, 0, 2);
            statusTable.Controls.Add(descriptionValue, 1, 2);
            statusTable.Dock = System.Windows.Forms.DockStyle.Top;
            statusTable.Name = "statusTable";
            statusTable.RowCount = 3;
            statusTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            statusTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            statusTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            statusTable.TabIndex = 1;

            //
            // plateLabel
            //
            plateLabel.AutoSize = true;
            plateLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            plateLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            plateLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            plateLabel.Name = "plateLabel";
            plateLabel.Text = "Plate:";

            //
            // plateValue
            //
            plateValue.AutoSize = true;
            plateValue.Font = new System.Drawing.Font("Consolas", 8.25F);
            plateValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            plateValue.Name = "plateValue";
            plateValue.Text = "\u2014";

            //
            // elapsedLabel
            //
            elapsedLabel.AutoSize = true;
            elapsedLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            elapsedLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            elapsedLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            elapsedLabel.Name = "elapsedLabel";
            elapsedLabel.Text = "Elapsed:";

            //
            // elapsedValue
            //
            elapsedValue.AutoSize = true;
            elapsedValue.Font = new System.Drawing.Font("Consolas", 8.25F);
            elapsedValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            elapsedValue.Name = "elapsedValue";
            elapsedValue.Text = "0:00";

            //
            // descriptionLabel
            //
            descriptionLabel.AutoSize = true;
            descriptionLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            descriptionLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            descriptionLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            descriptionLabel.Name = "descriptionLabel";
            descriptionLabel.Text = "Detail:";

            //
            // descriptionValue
            //
            descriptionValue.AutoSize = true;
            descriptionValue.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            descriptionValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            descriptionValue.Name = "descriptionValue";
            descriptionValue.Text = "\u2014";

            //
            // buttonPanel
            //
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(stopButton);
            buttonPanel.Controls.Add(acceptButton);
            buttonPanel.Dock = System.Windows.Forms.DockStyle.Top;
            buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Padding = new System.Windows.Forms.Padding(9, 6, 9, 6);
            buttonPanel.Size = new System.Drawing.Size(450, 45);
            buttonPanel.TabIndex = 3;

            //
            // acceptButton
            //
            acceptButton.Enabled = false;
            acceptButton.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            acceptButton.Margin = new System.Windows.Forms.Padding(6, 3, 0, 3);
            acceptButton.Name = "acceptButton";
            acceptButton.Size = new System.Drawing.Size(93, 27);
            acceptButton.TabIndex = 1;
            acceptButton.Text = "Accept";
            acceptButton.UseVisualStyleBackColor = true;
            acceptButton.Click += AcceptButton_Click;

            //
            // stopButton
            //
            stopButton.Enabled = false;
            stopButton.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            stopButton.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            stopButton.Name = "stopButton";
            stopButton.Size = new System.Drawing.Size(93, 27);
            stopButton.TabIndex = 0;
            stopButton.Text = "Stop";
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += StopButton_Click;

            //
            // NestProgressForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(450, 315);
            Controls.Add(buttonPanel);
            Controls.Add(statusPanel);
            Controls.Add(resultsPanel);
            Controls.Add(phaseStepper);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "NestProgressForm";
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Nesting Progress";
            resultsPanel.ResumeLayout(false);
            resultsPanel.PerformLayout();
            resultsTable.ResumeLayout(false);
            resultsTable.PerformLayout();
            densityPanel.ResumeLayout(false);
            densityPanel.PerformLayout();
            statusPanel.ResumeLayout(false);
            statusPanel.PerformLayout();
            statusTable.ResumeLayout(false);
            statusTable.PerformLayout();
            buttonPanel.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Controls.PhaseStepperControl phaseStepper;
        private System.Windows.Forms.Panel resultsPanel;
        private System.Windows.Forms.Label resultsHeader;
        private System.Windows.Forms.TableLayoutPanel resultsTable;
        private System.Windows.Forms.Label partsLabel;
        private System.Windows.Forms.Label partsValue;
        private System.Windows.Forms.Label densityLabel;
        private System.Windows.Forms.FlowLayoutPanel densityPanel;
        private System.Windows.Forms.Label densityValue;
        private Controls.DensityBar densityBar;
        private System.Windows.Forms.Label nestedAreaLabel;
        private System.Windows.Forms.Label nestedAreaValue;
        private System.Windows.Forms.Panel statusPanel;
        private System.Windows.Forms.Label statusHeader;
        private System.Windows.Forms.TableLayoutPanel statusTable;
        private System.Windows.Forms.Label plateLabel;
        private System.Windows.Forms.Label plateValue;
        private System.Windows.Forms.Label elapsedLabel;
        private System.Windows.Forms.Label elapsedValue;
        private System.Windows.Forms.Label descriptionLabel;
        private System.Windows.Forms.Label descriptionValue;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private System.Windows.Forms.Button acceptButton;
        private System.Windows.Forms.Button stopButton;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest/OpenNest.csproj --no-restore -v q`
Expected: Build errors — `NestProgressForm.cs` still references removed fields (`phaseValue`, `remnantValue`, etc.). This is expected; Task 4 will fix it.

- [ ] **Step 3: Commit**

Do NOT commit yet — the code-behind (Task 4) must be updated first to compile.

---

### Task 4: Rewrite NestProgressForm Code-Behind

**Files:**
- Modify: `OpenNest/Forms/NestProgressForm.cs` (full rewrite)

- [ ] **Step 1: Rewrite the form code-behind**

Replace the entire file with the updated logic: phase stepper integration, color-coded flash & fade with per-label color tracking, Accept/Stop buttons, updated FormatPhase, and DensityBar updates.

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class NestProgressForm : Form
    {
        private static readonly Color DefaultFlashColor = Color.FromArgb(0, 160, 0);
        private static readonly Color DensityLowColor = Color.FromArgb(200, 40, 40);
        private static readonly Color DensityMidColor = Color.FromArgb(200, 160, 0);
        private static readonly Color DensityHighColor = Color.FromArgb(0, 160, 0);

        private const int FadeSteps = 20;
        private const int FadeIntervalMs = 50;

        private readonly CancellationTokenSource cts;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer elapsedTimer;
        private readonly System.Windows.Forms.Timer fadeTimer;
        private readonly Dictionary<Label, (int remaining, Color flashColor)> fadeCounters = new();
        private bool hasReceivedProgress;

        public bool Accepted { get; private set; }

        public NestProgressForm(CancellationTokenSource cts, bool showPlateRow = true)
        {
            this.cts = cts;
            InitializeComponent();

            if (!showPlateRow)
            {
                plateLabel.Visible = false;
                plateValue.Visible = false;
            }

            elapsedTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            elapsedTimer.Tick += (s, e) => UpdateElapsed();
            elapsedTimer.Start();

            fadeTimer = new System.Windows.Forms.Timer { Interval = FadeIntervalMs };
            fadeTimer.Tick += FadeTimer_Tick;
        }

        public void UpdateProgress(NestProgress progress)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (!hasReceivedProgress)
            {
                hasReceivedProgress = true;
                acceptButton.Enabled = true;
                stopButton.Enabled = true;
            }

            phaseStepper.ActivePhase = progress.Phase;

            SetValueWithFlash(plateValue, progress.PlateNumber.ToString());
            SetValueWithFlash(partsValue, progress.BestPartCount.ToString());

            var densityText = progress.BestDensity.ToString("P1");
            var densityFlashColor = GetDensityColor(progress.BestDensity);
            SetValueWithFlash(densityValue, densityText, densityFlashColor);
            densityBar.Value = progress.BestDensity;

            SetValueWithFlash(nestedAreaValue,
                $"{progress.NestedWidth:F1} x {progress.NestedLength:F1} ({progress.NestedArea:F1} sq in)");

            descriptionValue.Text = !string.IsNullOrEmpty(progress.Description)
                ? progress.Description
                : FormatPhase(progress.Phase);
        }

        public void ShowCompleted()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            stopwatch.Stop();
            elapsedTimer.Stop();
            UpdateElapsed();

            phaseStepper.IsComplete = true;
            descriptionValue.Text = "\u2014";

            acceptButton.Visible = false;
            stopButton.Text = "Close";
            stopButton.Enabled = true;
            stopButton.Click -= StopButton_Click;
            stopButton.Click += (s, e) => Close();
        }

        private void UpdateElapsed()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            var elapsed = stopwatch.Elapsed;
            elapsedValue.Text = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"m\:ss");
        }

        private void AcceptButton_Click(object sender, EventArgs e)
        {
            Accepted = true;
            cts.Cancel();
            acceptButton.Enabled = false;
            stopButton.Enabled = false;
            acceptButton.Text = "Accepted";
            stopButton.Text = "Stopping...";
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            cts.Cancel();
            acceptButton.Enabled = false;
            stopButton.Enabled = false;
            stopButton.Text = "Stopping...";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            fadeTimer.Stop();
            fadeTimer.Dispose();
            elapsedTimer.Stop();
            elapsedTimer.Dispose();
            stopwatch.Stop();

            if (!cts.IsCancellationRequested)
                cts.Cancel();

            base.OnFormClosing(e);
        }

        private void SetValueWithFlash(Label label, string text, Color? flashColor = null)
        {
            if (label.Text == text)
                return;

            var color = flashColor ?? DefaultFlashColor;
            label.Text = text;
            label.ForeColor = color;
            fadeCounters[label] = (FadeSteps, color);

            if (!fadeTimer.Enabled)
                fadeTimer.Start();
        }

        private void FadeTimer_Tick(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                fadeTimer.Stop();
                return;
            }

            var defaultColor = SystemColors.ControlText;
            var labels = fadeCounters.Keys.ToList();

            foreach (var label in labels)
            {
                var (remaining, flashColor) = fadeCounters[label];
                remaining--;

                if (remaining <= 0)
                {
                    label.ForeColor = defaultColor;
                    fadeCounters.Remove(label);
                }
                else
                {
                    var ratio = (float)remaining / FadeSteps;
                    var r = (int)(defaultColor.R + (flashColor.R - defaultColor.R) * ratio);
                    var g = (int)(defaultColor.G + (flashColor.G - defaultColor.G) * ratio);
                    var b = (int)(defaultColor.B + (flashColor.B - defaultColor.B) * ratio);
                    label.ForeColor = Color.FromArgb(r, g, b);
                    fadeCounters[label] = (remaining, flashColor);
                }
            }

            if (fadeCounters.Count == 0)
                fadeTimer.Stop();
        }

        private static Color GetDensityColor(double density)
        {
            if (density < 0.5)
                return DensityLowColor;
            if (density < 0.7)
                return DensityMidColor;
            return DensityHighColor;
        }

        private static string FormatPhase(NestPhase phase)
        {
            switch (phase)
            {
                case NestPhase.Linear: return "Trying rotations...";
                case NestPhase.RectBestFit: return "Trying best fit...";
                case NestPhase.Pairs: return "Trying pairs...";
                case NestPhase.Extents: return "Trying extents...";
                case NestPhase.Nfp: return "Trying NFP...";
                default: return phase.ToString();
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest/OpenNest.csproj --no-restore -v q`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add OpenNest/Forms/NestProgressForm.cs OpenNest/Forms/NestProgressForm.Designer.cs
git commit -m "feat(ui): rewrite NestProgressForm with grouped panels, stepper, density bar, and Accept button"
```

---

### Task 5: Update Callers for Accept Support

**Files:**
- Modify: `OpenNest/Forms/MainForm.cs:868`
- Modify: `OpenNest/Controls/PlateView.cs:933`

- [ ] **Step 1: Update `RunAutoNest_Click` in MainForm.cs**

At line 868, change the cancellation check to also honor `Accepted`:

```csharp
// Before:
if (nestParts.Count > 0 && !token.IsCancellationRequested)
// After:
if (nestParts.Count > 0 && (!token.IsCancellationRequested || progressForm.Accepted))
```

- [ ] **Step 2: Update `FillWithProgress` in PlateView.cs**

At line 933, same change:

```csharp
// Before:
if (parts.Count > 0 && !cts.IsCancellationRequested)
// After:
if (parts.Count > 0 && (!cts.IsCancellationRequested || progressForm.Accepted))
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build OpenNest.sln --no-restore -v q`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add OpenNest/Forms/MainForm.cs OpenNest/Controls/PlateView.cs
git commit -m "feat(ui): support Accept button in nesting callers"
```

---

### Task 6: Build Verification and Layout Tuning

**Files:** None new — adjustments to existing files from Tasks 1-5 if needed.

- [ ] **Step 1: Full solution build**

Run: `dotnet build OpenNest.sln -v q`
Expected: 0 errors

- [ ] **Step 2: Run existing tests**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --no-build -v q`
Expected: All tests pass (no regressions — these tests don't touch the UI)

- [ ] **Step 3: Visual review checklist**

Launch the app and open or create a nest with drawings. Run a fill operation and verify:

1. Phase stepper shows circles for all 6 phases — initially all hollow/pending
2. As the engine progresses, active phase circle is larger with glow, visited phases are filled blue
3. Results section has white background with "RESULTS" header, monospaced values
4. Status section has white background with "STATUS" header
5. Density bar shows next to percentage, gradient from orange to green
6. Values flash green (or red/yellow for low density) and fade back to black on change
7. Accept button stops engine and keeps result on plate
8. Stop button stops engine and discards result
9. Both buttons disabled until first progress update
10. After ShowCompleted(), both replaced by "Close" button

- [ ] **Step 4: Final commit if any tuning was needed**

```bash
git add -A
git commit -m "fix(ui): tune progress form layout spacing"
```
