# Nesting Progress Window Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a live-preview progress window so users can watch nesting results appear on the plate and stop early if satisfied.

**Architecture:** The engine gets new `List<Part>`-returning overloads that accept `IProgress<NestProgress>` + `CancellationToken`. A modeless `NestProgressForm` shows stats. `PlateView` gains a temporary parts list drawn in a preview color. `MainForm` orchestrates everything via `Task.Run` + `await`.

**Tech Stack:** .NET 8 WinForms, `IProgress<T>`, `CancellationToken`, `Task.Run`

**Spec:** `docs/superpowers/specs/2026-03-13-nesting-progress-window-design.md`

---

## File Structure

| File | Responsibility |
|------|---------------|
| `OpenNest.Engine/NestProgress.cs` | **New.** `NestPhase` enum + `NestProgress` class (progress data model) |
| `OpenNest.Engine/NestEngine.cs` | **Modify.** Add `List<Part>`-returning overloads with progress/cancellation. Existing `bool` overloads delegate to them. |
| `OpenNest/ColorScheme.cs` | **Modify.** Add `PreviewPartColor` + `PreviewPartPen` + `PreviewPartBrush` |
| `OpenNest/Controls/PlateView.cs` | **Modify.** Add `temporaryParts` list, draw them in preview color, expose `SetTemporaryParts`/`ClearTemporaryParts`/`AcceptTemporaryParts` |
| `OpenNest/Forms/NestProgressForm.cs` | **New.** Modeless dialog with TableLayoutPanel showing stats + Stop button |
| `OpenNest/Forms/MainForm.cs` | **Modify.** Rewire `RunAutoNest_Click` and `FillPlate_Click` to async with progress. Add UI lockout. |
| `OpenNest/Actions/ActionFillArea.cs` | **Modify.** Use progress overload for area fill. |

---

## Chunk 1: Engine Progress Infrastructure

### Task 1: NestProgress Data Model

**Files:**
- Create: `OpenNest.Engine/NestProgress.cs`

- [ ] **Step 1: Create NestPhase enum and NestProgress class**

```csharp
// OpenNest.Engine/NestProgress.cs
using System.Collections.Generic;

namespace OpenNest
{
    public enum NestPhase
    {
        Linear,
        RectBestFit,
        Pairs,
        Remainder
    }

    public class NestProgress
    {
        public NestPhase Phase { get; set; }
        public int PlateNumber { get; set; }
        public int BestPartCount { get; set; }
        public double BestDensity { get; set; }
        public double UsableRemnantArea { get; set; }
        public List<Part> BestParts { get; set; }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestProgress.cs
git commit -m "feat(engine): add NestPhase enum and NestProgress data model"
```

---

### Task 2: NestEngine Progress Overloads — FindBestFill

This task adds the core progress/cancellation support to `FindBestFill`, the private method that tries Linear → RectBestFit → Pairs → Remainder strategies. The new method signature adds `IProgress<NestProgress>` and `CancellationToken` parameters.

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs`

**Context:** `FindBestFill` is currently at line 55. It uses `Parallel.ForEach` for the linear phase (line 90) and `FillWithPairs` uses `Parallel.For` (line 224). Both need the cancellation token threaded into `ParallelOptions`.

- [ ] **Step 1: Add a private helper to report progress**

Add this helper method at the end of `NestEngine` (before the closing brace at line 536). It clones the best parts list and reports via `IProgress<T>`:

```csharp
private static void ReportProgress(
    IProgress<NestProgress> progress,
    NestPhase phase,
    int plateNumber,
    List<Part> best,
    Box workArea)
{
    if (progress == null || best == null || best.Count == 0)
        return;

    var score = FillScore.Compute(best, workArea);
    var clonedParts = new List<Part>(best.Count);

    foreach (var part in best)
        clonedParts.Add((Part)part.Clone());

    progress.Report(new NestProgress
    {
        Phase = phase,
        PlateNumber = plateNumber,
        BestPartCount = score.Count,
        BestDensity = score.Density,
        UsableRemnantArea = score.UsableRemnantArea,
        BestParts = clonedParts
    });
}
```

- [ ] **Step 2: Add a `PlateNumber` property to NestEngine**

Add a public property so callers can set the current plate number for progress reporting. Add after the `NestDirection` property (line 20):

```csharp
public int PlateNumber { get; set; }
```

- [ ] **Step 3: Create `FindBestFill` overload with progress and cancellation**

Add a new private method below the existing `FindBestFill` (after line 135). This is a copy of `FindBestFill` with progress reporting and cancellation token support injected at each phase boundary. Key changes:

- `Parallel.ForEach` gets `new ParallelOptions { CancellationToken = token }`
- After each phase (linear, rect, pairs), call `ReportProgress(...)` if a new best is found
- After each phase, call `token.ThrowIfCancellationRequested()`
- The whole method is wrapped in try/catch for `OperationCanceledException` to return current best

```csharp
private List<Part> FindBestFill(NestItem item, Box workArea,
    IProgress<NestProgress> progress, CancellationToken token)
{
    List<Part> best = null;

    try
    {
        var bestRotation = RotationAnalysis.FindBestRotation(item);
        var engine = new FillLinear(workArea, Plate.PartSpacing);
        var angles = new List<double> { bestRotation, bestRotation + Angle.HalfPI };

        var testPart = new Part(item.Drawing);
        if (!bestRotation.IsEqualTo(0))
            testPart.Rotate(bestRotation);
        testPart.UpdateBounds();

        var partLongestSide = System.Math.Max(testPart.BoundingBox.Width, testPart.BoundingBox.Length);
        var workAreaShortSide = System.Math.Min(workArea.Width, workArea.Length);

        if (workAreaShortSide < partLongestSide)
        {
            var step = Angle.ToRadians(5);
            for (var a = 0.0; a < System.Math.PI; a += step)
            {
                if (!angles.Any(existing => existing.IsEqualTo(a)))
                    angles.Add(a);
            }
        }

        // Linear phase
        var linearBag = new System.Collections.Concurrent.ConcurrentBag<(FillScore score, List<Part> parts)>();

        System.Threading.Tasks.Parallel.ForEach(angles,
            new System.Threading.Tasks.ParallelOptions { CancellationToken = token },
            angle =>
            {
                var localEngine = new FillLinear(workArea, Plate.PartSpacing);
                var h = localEngine.Fill(item.Drawing, angle, NestDirection.Horizontal);
                var v = localEngine.Fill(item.Drawing, angle, NestDirection.Vertical);

                if (h != null && h.Count > 0)
                    linearBag.Add((FillScore.Compute(h, workArea), h));
                if (v != null && v.Count > 0)
                    linearBag.Add((FillScore.Compute(v, workArea), v));
            });

        var bestScore = default(FillScore);

        foreach (var (score, parts) in linearBag)
        {
            if (best == null || score > bestScore)
            {
                best = parts;
                bestScore = score;
            }
        }

        var bestLinearScore = best != null ? FillScore.Compute(best, workArea) : default;
        Debug.WriteLine($"[FindBestFill] Linear: {bestLinearScore.Count} parts, density={bestLinearScore.Density:P1} | WorkArea: {workArea.Width:F1}x{workArea.Length:F1} | Angles: {angles.Count}");

        ReportProgress(progress, NestPhase.Linear, PlateNumber, best, workArea);
        token.ThrowIfCancellationRequested();

        // RectBestFit phase
        var rectResult = FillRectangleBestFit(item, workArea);
        Debug.WriteLine($"[FindBestFill] RectBestFit: {rectResult?.Count ?? 0} parts");

        if (IsBetterFill(rectResult, best, workArea))
        {
            best = rectResult;
            ReportProgress(progress, NestPhase.RectBestFit, PlateNumber, best, workArea);
        }

        token.ThrowIfCancellationRequested();

        // Pairs phase
        var pairResult = FillWithPairs(item, workArea, token);
        Debug.WriteLine($"[FindBestFill] Pair: {pairResult.Count} parts | Winner: {(IsBetterFill(pairResult, best, workArea) ? "Pair" : "Linear")}");

        if (IsBetterFill(pairResult, best, workArea))
        {
            best = pairResult;
            ReportProgress(progress, NestPhase.Pairs, PlateNumber, best, workArea);
        }
    }
    catch (OperationCanceledException)
    {
        Debug.WriteLine("[FindBestFill] Cancelled, returning current best");
    }

    return best ?? new List<Part>();
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeded (the new overload exists but isn't called yet)

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat(engine): add FindBestFill overload with progress and cancellation"
```

---

### Task 3: NestEngine Progress Overloads — FillWithPairs

Thread the `CancellationToken` into `FillWithPairs` so its `Parallel.For` loop can be cancelled mid-phase.

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs`

**Context:** `FillWithPairs` is at line 213. It uses `Parallel.For` at line 224.

- [ ] **Step 1: Add FillWithPairs overload accepting CancellationToken**

Add a new private method below the existing `FillWithPairs` (after line 250). This is a copy with `ParallelOptions` added to the `Parallel.For` call:

```csharp
private List<Part> FillWithPairs(NestItem item, Box workArea, CancellationToken token)
{
    var bestFits = BestFitCache.GetOrCompute(
        item.Drawing, Plate.Size.Width, Plate.Size.Length,
        Plate.PartSpacing);

    var candidates = SelectPairCandidates(bestFits, workArea);
    Debug.WriteLine($"[FillWithPairs] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {candidates.Count}");

    var resultBag = new System.Collections.Concurrent.ConcurrentBag<(FillScore score, List<Part> parts)>();

    try
    {
        System.Threading.Tasks.Parallel.For(0, candidates.Count,
            new System.Threading.Tasks.ParallelOptions { CancellationToken = token },
            i =>
            {
                var result = candidates[i];
                var pairParts = result.BuildParts(item.Drawing);
                var angles = RotationAnalysis.FindHullEdgeAngles(pairParts);
                var engine = new FillLinear(workArea, Plate.PartSpacing);
                var filled = FillPattern(engine, pairParts, angles, workArea);

                if (filled != null && filled.Count > 0)
                    resultBag.Add((FillScore.Compute(filled, workArea), filled));
            });
    }
    catch (OperationCanceledException)
    {
        Debug.WriteLine("[FillWithPairs] Cancelled mid-phase, using results so far");
    }

    List<Part> best = null;
    var bestScore = default(FillScore);

    foreach (var (score, parts) in resultBag)
    {
        if (best == null || score > bestScore)
        {
            best = parts;
            bestScore = score;
        }
    }

    Debug.WriteLine($"[FillWithPairs] Best pair result: {bestScore.Count} parts, remnant={bestScore.UsableRemnantArea:F1}, density={bestScore.Density:P1}");
    return best ?? new List<Part>();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat(engine): add FillWithPairs overload with CancellationToken"
```

---

### Task 4: NestEngine Public Fill Overloads

Add the public `List<Part>`-returning `Fill` overloads that callers (MainForm) will use. These call `FindBestFill` with progress/cancellation and return the result without adding to `Plate.Parts`. Wire the existing `bool Fill(...)` overloads to delegate to them.

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs`

**Context:** The existing `bool Fill(NestItem item, Box workArea)` is at line 32. The existing `bool Fill(List<Part> groupParts, Box workArea)` is at line 137.

- [ ] **Step 1: Add public Fill(NestItem, Box, IProgress, CancellationToken) overload**

Add after the existing `Fill(NestItem, Box)` method (after line 53). This calls `FindBestFill` with progress/cancellation, applies `TryRemainderImprovement`, and returns the parts without adding to the plate:

```csharp
public List<Part> Fill(NestItem item, Box workArea,
    IProgress<NestProgress> progress, CancellationToken token)
{
    var best = FindBestFill(item, workArea, progress, token);

    if (token.IsCancellationRequested)
        return best ?? new List<Part>();

    // Try improving by filling the remainder strip separately.
    var improved = TryRemainderImprovement(item, workArea, best);

    if (IsBetterFill(improved, best, workArea))
    {
        Debug.WriteLine($"[Fill] Remainder improvement: {improved.Count} parts (was {best?.Count ?? 0})");
        best = improved;
        ReportProgress(progress, NestPhase.Remainder, PlateNumber, best, workArea);
    }

    if (best == null || best.Count == 0)
        return new List<Part>();

    if (item.Quantity > 0 && best.Count > item.Quantity)
        best = best.Take(item.Quantity).ToList();

    return best;
}
```

- [ ] **Step 2: Rewire existing bool Fill(NestItem, Box) to delegate**

Replace the body of `Fill(NestItem item, Box workArea)` at line 32 to delegate to the new overload:

```csharp
public bool Fill(NestItem item, Box workArea)
{
    var parts = Fill(item, workArea, null, CancellationToken.None);

    if (parts == null || parts.Count == 0)
        return false;

    Plate.Parts.AddRange(parts);
    return true;
}
```

Add `using System.Threading;` to the top of the file if not already present.

- [ ] **Step 3: Add public Fill(List\<Part\>, Box, IProgress, CancellationToken) overload**

Add after the existing `Fill(List<Part> groupParts, Box workArea)` method (after line 180). Same pattern — compute and return without touching `Plate.Parts`:

```csharp
public List<Part> Fill(List<Part> groupParts, Box workArea,
    IProgress<NestProgress> progress, CancellationToken token)
{
    if (groupParts == null || groupParts.Count == 0)
        return new List<Part>();

    var engine = new FillLinear(workArea, Plate.PartSpacing);
    var angles = RotationAnalysis.FindHullEdgeAngles(groupParts);
    var best = FillPattern(engine, groupParts, angles, workArea);

    Debug.WriteLine($"[Fill(groupParts,Box)] Linear: {best?.Count ?? 0} parts | WorkArea: {workArea.Width:F1}x{workArea.Length:F1}");

    ReportProgress(progress, NestPhase.Linear, PlateNumber, best, workArea);

    if (groupParts.Count == 1)
    {
        try
        {
            token.ThrowIfCancellationRequested();

            var nestItem = new NestItem { Drawing = groupParts[0].BaseDrawing };
            var rectResult = FillRectangleBestFit(nestItem, workArea);

            Debug.WriteLine($"[Fill(groupParts,Box)] RectBestFit: {rectResult?.Count ?? 0} parts");

            if (IsBetterFill(rectResult, best, workArea))
            {
                best = rectResult;
                ReportProgress(progress, NestPhase.RectBestFit, PlateNumber, best, workArea);
            }

            token.ThrowIfCancellationRequested();

            var pairResult = FillWithPairs(nestItem, workArea, token);

            Debug.WriteLine($"[Fill(groupParts,Box)] Pair: {pairResult.Count} parts | Winner: {(IsBetterFill(pairResult, best, workArea) ? "Pair" : "Linear")}");

            if (IsBetterFill(pairResult, best, workArea))
            {
                best = pairResult;
                ReportProgress(progress, NestPhase.Pairs, PlateNumber, best, workArea);
            }

            // Try improving by filling the remainder strip separately.
            var improved = TryRemainderImprovement(nestItem, workArea, best);

            if (IsBetterFill(improved, best, workArea))
            {
                Debug.WriteLine($"[Fill(groupParts,Box)] Remainder improvement: {improved.Count} parts (was {best?.Count ?? 0})");
                best = improved;
                ReportProgress(progress, NestPhase.Remainder, PlateNumber, best, workArea);
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Fill(groupParts,Box)] Cancelled, returning current best");
        }
    }

    return best ?? new List<Part>();
}
```

- [ ] **Step 4: Rewire existing bool Fill(List\<Part\>, Box) to delegate**

Replace the body of `Fill(List<Part> groupParts, Box workArea)` at line 137:

```csharp
public bool Fill(List<Part> groupParts, Box workArea)
{
    var parts = Fill(groupParts, workArea, null, CancellationToken.None);

    if (parts == null || parts.Count == 0)
        return false;

    Plate.Parts.AddRange(parts);
    return true;
}
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded (full solution — ensures MCP and UI still compile)

- [ ] **Step 6: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat(engine): add public Fill overloads returning List<Part> with progress/cancellation"
```

---

## Chunk 2: PlateView Temporary Parts

### Task 5: Add PreviewPart Color to ColorScheme

**Files:**
- Modify: `OpenNest/ColorScheme.cs`

**Context:** Colors follow a pattern: private backing field, public property with getter/setter that lazily creates Pen/Brush. See `LayoutOutlineColor` at line 46 for the pattern. The `Default` static instance is at line 15.

- [ ] **Step 1: Add PreviewPart color fields and properties**

Add a private field after the existing color fields (after line 13):

```csharp
private Color previewPartColor;
```

Add a private `Pen` and `Brush` field alongside the existing ones. Find where `layoutOutlinePen` etc. are declared and add:

```csharp
private Pen previewPartPen;
private Brush previewPartBrush;
```

Add the `PreviewPartColor` property after the last color property (after `EdgeSpacingColor`, around line 136). Follow the same pattern as `LayoutOutlineColor`:

```csharp
public Color PreviewPartColor
{
    get { return previewPartColor; }
    set
    {
        previewPartColor = value;

        if (previewPartPen != null)
            previewPartPen.Dispose();

        if (previewPartBrush != null)
            previewPartBrush.Dispose();

        previewPartPen = new Pen(value, 1);
        previewPartBrush = new SolidBrush(Color.FromArgb(60, value));
    }
}

public Pen PreviewPartPen => previewPartPen;
public Brush PreviewPartBrush => previewPartBrush;
```

Note: The brush uses `Color.FromArgb(60, value)` for semi-transparency so parts look like a preview overlay, not solid placed parts.

- [ ] **Step 2: Set default preview color**

In the `Default` static property initializer (around line 15), add:

```csharp
PreviewPartColor = Color.FromArgb(255, 140, 0),  // orange
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build OpenNest`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add OpenNest/ColorScheme.cs
git commit -m "feat(ui): add PreviewPart color to ColorScheme"
```

---

### Task 6: PlateView Temporary Parts List and Drawing

**Files:**
- Modify: `OpenNest/Controls/PlateView.cs`

**Context:**
- `parts` field (private `List<LayoutPart>`) at line ~27
- `DrawParts` method at line 453
- `LayoutPart.Create(Part, PlateView)` is the factory for wrapping Part → LayoutPart
- `Refresh()` at line 376 calls `parts.ForEach(p => p.Update(this))` then `Invalidate()`
- `SetPlate` at line 116 clears `parts` when switching plates

- [ ] **Step 1: Add temporaryParts field**

Add after the `parts` field declaration (around line 27):

```csharp
private List<LayoutPart> temporaryParts = new List<LayoutPart>();
```

- [ ] **Step 2: Draw temporary parts in DrawParts**

In `DrawParts` (line 453), after the main part drawing loop (after line 470 where `part.Draw(g, ...)` is called for regular parts), add temporary part drawing before the `DrawOffset`/`DrawBounds`/`DrawRapid` section:

```csharp
// Draw temporary (preview) parts
for (var i = 0; i < temporaryParts.Count; i++)
{
    var temp = temporaryParts[i];

    if (temp.IsDirty)
        temp.Update(this);

    var path = temp.Path;
    var pathBounds = path.GetBounds();

    if (!pathBounds.IntersectsWith(viewBounds))
        continue;

    g.FillPath(ColorScheme.PreviewPartBrush, path);
    g.DrawPath(ColorScheme.PreviewPartPen, path);
}
```

- [ ] **Step 3: Add SetTemporaryParts method**

Add as a public method on `PlateView`:

```csharp
public void SetTemporaryParts(List<Part> parts)
{
    temporaryParts.Clear();

    if (parts != null)
    {
        foreach (var part in parts)
            temporaryParts.Add(LayoutPart.Create(part, this));
    }

    Invalidate();
}
```

- [ ] **Step 4: Add ClearTemporaryParts method**

```csharp
public void ClearTemporaryParts()
{
    temporaryParts.Clear();
    Invalidate();
}
```

- [ ] **Step 5: Add AcceptTemporaryParts method**

This moves temporary parts into the real plate. It returns the count of parts accepted so the caller can use it for quantity tracking.

```csharp
public int AcceptTemporaryParts()
{
    var count = temporaryParts.Count;

    foreach (var layoutPart in temporaryParts)
        Plate.Parts.Add(layoutPart.BasePart);

    temporaryParts.Clear();
    return count;
}
```

Note: `Plate.Parts.Add()` fires `PartAdded` on `ObservableList`, which triggers `plate_PartAdded` on PlateView, which adds a new `LayoutPart` to the regular `parts` list. So the part transitions from temp → real automatically.

- [ ] **Step 6: Clear temporary parts on plate switch**

In `SetPlate` (line 116), add `temporaryParts.Clear()` in the cleanup section (after `parts.Clear()` at line 123):

```csharp
temporaryParts.Clear();
```

- [ ] **Step 7: Update Refresh to include temporary parts**

In `Refresh()` (line 376), add temporary part updates:

```csharp
public override void Refresh()
{
    parts.ForEach(p => p.Update(this));
    temporaryParts.ForEach(p => p.Update(this));
    Invalidate();
}
```

- [ ] **Step 8: Build to verify**

Run: `dotnet build OpenNest`
Expected: Build succeeded

- [ ] **Step 9: Commit**

```bash
git add OpenNest/Controls/PlateView.cs
git commit -m "feat(ui): add temporary parts list to PlateView with preview drawing"
```

---

## Chunk 3: NestProgressForm

### Task 7: Create NestProgressForm

**Files:**
- Create: `OpenNest/Forms/NestProgressForm.cs`

**Context:** This is a WinForms form. Since there's no designer in the CLI workflow, build it in code. The form is small and simple — a `TableLayoutPanel` with labels and a Stop button.

- [ ] **Step 1: Create NestProgressForm.cs**

```csharp
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public class NestProgressForm : Form
    {
        private readonly CancellationTokenSource cts;

        private Label phaseValue;
        private Label plateValue;
        private Label partsValue;
        private Label densityValue;
        private Label remnantValue;
        private Label plateLabel;
        private Button stopButton;
        private TableLayoutPanel table;

        public NestProgressForm(CancellationTokenSource cts, bool showPlateRow = true)
        {
            this.cts = cts;
            InitializeLayout(showPlateRow);
        }

        private void InitializeLayout(bool showPlateRow)
        {
            Text = "Nesting Progress";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(280, showPlateRow ? 210 : 190);

            table = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8)
            };

            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            phaseValue = AddRow(table, "Phase:");
            plateValue = AddRow(table, "Plate:");
            partsValue = AddRow(table, "Parts:");
            densityValue = AddRow(table, "Density:");
            remnantValue = AddRow(table, "Remnant:");

            if (!showPlateRow)
            {
                plateLabel = FindLabel(table, "Plate:");
                if (plateLabel != null)
                    SetRowVisible(plateLabel, plateValue, false);
            }

            stopButton = new Button
            {
                Text = "Stop",
                Width = 80,
                Anchor = AnchorStyles.None,
                Margin = new Padding(0, 8, 0, 8)
            };

            stopButton.Click += StopButton_Click;

            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8, 0, 8, 0)
            };

            buttonPanel.Controls.Add(stopButton);

            Controls.Add(buttonPanel);
            Controls.Add(table);

            // Reverse order since Dock.Top stacks bottom-up
            Controls.SetChildIndex(table, 0);
            Controls.SetChildIndex(buttonPanel, 1);
        }

        private Label AddRow(TableLayoutPanel table, string labelText)
        {
            var row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = new Label
            {
                Text = labelText,
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(4)
            };

            var value = new Label
            {
                Text = "—",
                AutoSize = true,
                Margin = new Padding(4)
            };

            table.Controls.Add(label, 0, row);
            table.Controls.Add(value, 1, row);

            return value;
        }

        private Label FindLabel(TableLayoutPanel table, string text)
        {
            foreach (Control c in table.Controls)
            {
                if (c is Label l && l.Text == text)
                    return l;
            }

            return null;
        }

        private void SetRowVisible(Label label, Label value, bool visible)
        {
            label.Visible = visible;
            value.Visible = visible;
        }

        public void UpdateProgress(NestProgress progress)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            phaseValue.Text = FormatPhase(progress.Phase);
            plateValue.Text = progress.PlateNumber.ToString();
            partsValue.Text = progress.BestPartCount.ToString();
            densityValue.Text = progress.BestDensity.ToString("P1");
            remnantValue.Text = $"{progress.UsableRemnantArea:F1} sq in";
        }

        public void ShowCompleted()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            phaseValue.Text = "Done";
            stopButton.Text = "Close";
            stopButton.Enabled = true;
            stopButton.Click -= StopButton_Click;
            stopButton.Click += (s, e) => Close();
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            cts.Cancel();
            stopButton.Text = "Stopping...";
            stopButton.Enabled = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!cts.IsCancellationRequested)
                cts.Cancel();

            base.OnFormClosing(e);
        }

        private static string FormatPhase(NestPhase phase)
        {
            switch (phase)
            {
                case NestPhase.Linear: return "Trying rotations...";
                case NestPhase.RectBestFit: return "Trying best fit...";
                case NestPhase.Pairs: return "Trying pairs...";
                case NestPhase.Remainder: return "Filling remainder...";
                default: return phase.ToString();
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest/Forms/NestProgressForm.cs
git commit -m "feat(ui): add NestProgressForm modeless dialog"
```

---

## Chunk 4: MainForm Integration

### Task 8: Async RunAutoNest_Click

**Files:**
- Modify: `OpenNest/Forms/MainForm.cs`

**Context:** `RunAutoNest_Click` is at line 692. It currently runs the nesting loop synchronously. The existing `EnableCheck()` pattern at line 107 is used for menu enable/disable.

- [ ] **Step 1: Add a nesting-in-progress flag and lockout helper**

Add fields near the top of `MainForm`:

```csharp
private bool nestingInProgress;
private CancellationTokenSource nestingCts;
```

Add a helper method that extends the existing `EnableCheck` pattern. Place it after `NavigationEnableCheck()`:

```csharp
private void SetNestingLockout(bool locked)
{
    nestingInProgress = locked;

    // Disable nesting-related menus while running
    mnuNest.Enabled = !locked;
    mnuPlate.Enabled = !locked;

    // Lock plate navigation
    mnuNestPreviousPlate.Enabled = !locked && activeForm != null && !activeForm.IsFirstPlate();
    btnPreviousPlate.Enabled = mnuNestPreviousPlate.Enabled;
    mnuNestNextPlate.Enabled = !locked && activeForm != null && !activeForm.IsLastPlate();
    btnNextPlate.Enabled = mnuNestNextPlate.Enabled;
    mnuNestFirstPlate.Enabled = !locked && activeForm != null && activeForm.PlateCount > 0 && !activeForm.IsFirstPlate();
    btnFirstPlate.Enabled = mnuNestFirstPlate.Enabled;
    mnuNestLastPlate.Enabled = !locked && activeForm != null && activeForm.PlateCount > 0 && !activeForm.IsLastPlate();
    btnLastPlate.Enabled = mnuNestLastPlate.Enabled;
}
```

- [ ] **Step 2: Replace RunAutoNest_Click with async version**

Replace the existing `RunAutoNest_Click` method (lines 692-738) with:

```csharp
private async void RunAutoNest_Click(object sender, EventArgs e)
{
    var form = new AutoNestForm(activeForm.Nest);
    form.AllowPlateCreation = true;

    if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        return;

    var items = form.GetNestItems();

    if (!items.Any(it => it.Quantity > 0))
        return;

    nestingCts = new CancellationTokenSource();
    var token = nestingCts.Token;

    var progressForm = new NestProgressForm(nestingCts, showPlateRow: true);
    var plateNumber = 1;

    var progress = new Progress<NestProgress>(p =>
    {
        progressForm.UpdateProgress(p);
        activeForm.PlateView.SetTemporaryParts(p.BestParts);
    });

    progressForm.Show(this);
    SetNestingLockout(true);

    try
    {
        while (items.Any(it => it.Quantity > 0))
        {
            if (token.IsCancellationRequested)
                break;

            var plate = activeForm.PlateView.Plate.Parts.Count > 0
                ? activeForm.Nest.CreatePlate()
                : activeForm.PlateView.Plate;

            // If a new plate was created, switch to it
            if (plate != activeForm.PlateView.Plate)
                activeForm.LoadLastPlate();

            var engine = new NestEngine(plate) { PlateNumber = plateNumber };
            var filled = false;

            foreach (var item in items)
            {
                if (item.Quantity <= 0)
                    continue;

                if (token.IsCancellationRequested)
                    break;

                // Run the engine on a background thread
                var parts = await Task.Run(() =>
                    engine.Fill(item, plate.WorkArea(), progress, token));

                if (parts.Count == 0)
                    continue;

                filled = true;

                // Count parts per drawing before accepting (for quantity tracking)
                foreach (var group in parts.GroupBy(p => p.BaseDrawing))
                {
                    var placed = group.Count();

                    foreach (var ni in items)
                    {
                        if (ni.Drawing == group.Key)
                            ni.Quantity -= placed;
                    }
                }

                // Accept the preview parts into the real plate
                activeForm.PlateView.AcceptTemporaryParts();
            }

            if (!filled)
                break;

            plateNumber++;
        }

        activeForm.Nest.UpdateDrawingQuantities();
        progressForm.ShowCompleted();
    }
    catch (Exception ex)
    {
        activeForm.PlateView.ClearTemporaryParts();
        MessageBox.Show($"Nesting error: {ex.Message}", "Error",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
        progressForm.Close();
        SetNestingLockout(false);
        nestingCts.Dispose();
        nestingCts = null;
    }
}
```

Add `using System.Threading.Tasks;` and `using System.Threading;` to the top of MainForm.cs if not already present.

- [ ] **Step 3: Build to verify**

Run: `dotnet build OpenNest`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add OpenNest/Forms/MainForm.cs
git commit -m "feat(ui): make RunAutoNest_Click async with progress and cancellation"
```

---

### Task 9: Async FillPlate_Click

**Files:**
- Modify: `OpenNest/Forms/MainForm.cs`

**Context:** `FillPlate_Click` is at line 785. Currently creates an engine and calls `Fill` synchronously. This should also show progress since a single-plate fill can be slow.

- [ ] **Step 1: Replace FillPlate_Click with async version**

Replace the existing `FillPlate_Click` method (lines 785-808) with:

```csharp
private async void FillPlate_Click(object sender, EventArgs e)
{
    if (activeForm == null)
        return;

    if (activeForm.Nest.Drawings.Count == 0)
        return;

    var form = new FillPlateForm(activeForm.Nest.Drawings);
    form.ShowDialog();

    var drawing = form.SelectedDrawing;

    if (drawing == null)
        return;

    nestingCts = new CancellationTokenSource();
    var token = nestingCts.Token;

    var progressForm = new NestProgressForm(nestingCts, showPlateRow: false);

    var progress = new Progress<NestProgress>(p =>
    {
        progressForm.UpdateProgress(p);
        activeForm.PlateView.SetTemporaryParts(p.BestParts);
    });

    progressForm.Show(this);
    SetNestingLockout(true);

    try
    {
        var plate = activeForm.PlateView.Plate;
        var engine = new NestEngine(plate);

        var parts = await Task.Run(() =>
            engine.Fill(new NestItem { Drawing = drawing },
                plate.WorkArea(), progress, token));

        if (parts.Count > 0)
            activeForm.PlateView.AcceptTemporaryParts();
        else
            activeForm.PlateView.ClearTemporaryParts();

        progressForm.ShowCompleted();
    }
    catch (Exception ex)
    {
        activeForm.PlateView.ClearTemporaryParts();
        MessageBox.Show($"Nesting error: {ex.Message}", "Error",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
        progressForm.Close();
        SetNestingLockout(false);
        nestingCts.Dispose();
        nestingCts = null;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest/Forms/MainForm.cs
git commit -m "feat(ui): make FillPlate_Click async with progress and cancellation"
```

---

### Task 10: ActionFillArea with Progress

**Files:**
- Modify: `OpenNest/Actions/ActionFillArea.cs`
- Modify: `OpenNest/Forms/MainForm.cs`

**Context:** `ActionFillArea` at `OpenNest/Actions/ActionFillArea.cs` currently creates an engine and calls `Fill` synchronously in its `FillArea()` method (line 28). Per the spec, the progress form is owned by MainForm, not ActionFillArea. ActionFillArea needs access to the progress/token infrastructure.

The cleanest approach: add an event on ActionFillArea that MainForm listens to, letting MainForm handle the async orchestration. But that's overengineered for one call site. Instead, ActionFillArea can accept progress/token via its constructor and use the same async pattern.

- [ ] **Step 1: Modify ActionFillArea to accept progress and cancellation**

Replace the entire `ActionFillArea.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.Actions
{
    [DisplayName("Fill Area")]
    public class ActionFillArea : ActionSelectArea
    {
        private Drawing drawing;
        private IProgress<NestProgress> progress;
        private CancellationTokenSource cts;
        private Action<List<Part>> onFillComplete;

        public ActionFillArea(PlateView plateView, Drawing drawing)
            : this(plateView, drawing, null, null, null)
        {
        }

        public ActionFillArea(PlateView plateView, Drawing drawing,
            IProgress<NestProgress> progress, CancellationTokenSource cts,
            Action<List<Part>> onFillComplete)
            : base(plateView)
        {
            plateView.PreviewKeyDown += plateView_PreviewKeyDown;
            this.drawing = drawing;
            this.progress = progress;
            this.cts = cts;
            this.onFillComplete = onFillComplete;
        }

        private void plateView_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                FillArea();
            else if (e.KeyCode == Keys.Escape && cts != null)
                cts.Cancel();
        }

        private async void FillArea()
        {
            if (progress != null && cts != null)
            {
                try
                {
                    var engine = new NestEngine(plateView.Plate);
                    var parts = await Task.Run(() =>
                        engine.Fill(new NestItem { Drawing = drawing },
                            SelectedArea, progress, cts.Token));

                    onFillComplete?.Invoke(parts);
                }
                catch (Exception)
                {
                    onFillComplete?.Invoke(new List<Part>());
                }
            }
            else
            {
                var engine = new NestEngine(plateView.Plate);
                engine.Fill(new NestItem { Drawing = drawing }, SelectedArea);
                plateView.Invalidate();
            }

            Update();
        }

        public override void DisconnectEvents()
        {
            plateView.PreviewKeyDown -= plateView_PreviewKeyDown;
            base.DisconnectEvents();
        }
    }
}
```

- [ ] **Step 2: Update FillArea_Click in MainForm to provide progress infrastructure**

Replace `FillArea_Click` (line 810) in MainForm:

```csharp
private void FillArea_Click(object sender, EventArgs e)
{
    if (activeForm == null)
        return;

    if (activeForm.Nest.Drawings.Count == 0)
        return;

    var form = new FillPlateForm(activeForm.Nest.Drawings);
    form.ShowDialog();

    var drawing = form.SelectedDrawing;

    if (drawing == null)
        return;

    nestingCts = new CancellationTokenSource();

    var progressForm = new NestProgressForm(nestingCts, showPlateRow: false);

    var progress = new Progress<NestProgress>(p =>
    {
        progressForm.UpdateProgress(p);
        activeForm.PlateView.SetTemporaryParts(p.BestParts);
    });

    Action<List<Part>> onComplete = parts =>
    {
        if (parts != null && parts.Count > 0)
            activeForm.PlateView.AcceptTemporaryParts();
        else
            activeForm.PlateView.ClearTemporaryParts();

        progressForm.Close();
        SetNestingLockout(false);
        nestingCts.Dispose();
        nestingCts = null;
    };

    progressForm.Show(this);
    SetNestingLockout(true);

    activeForm.PlateView.SetAction(typeof(ActionFillArea),
        drawing, progress, nestingCts, onComplete);
}
```

- [ ] **Step 3: Verify PlateView.SetAction passes extra args to constructor**

Check that `PlateView.SetAction(Type type, params object[] args)` passes args to the action constructor via reflection. Read the method to confirm it uses `Activator.CreateInstance` or similar with the extra params. If it prepends `PlateView` as the first arg, the constructor signature `(PlateView, Drawing, IProgress, CancellationTokenSource, Action)` should work with `args = [drawing, progress, nestingCts, onComplete]`.

- [ ] **Step 4: Build to verify**

Run: `dotnet build OpenNest`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add OpenNest/Actions/ActionFillArea.cs OpenNest/Forms/MainForm.cs
git commit -m "feat(ui): add progress support to ActionFillArea and FillArea_Click"
```

---

## Chunk 5: Edge Cases and Cleanup

### Task 11: Handle EditNestForm Close During Nesting

**Files:**
- Modify: `OpenNest/Forms/MainForm.cs`

**Context:** Per the spec, if the user closes the MDI child while nesting runs, cancel and close the progress form. `MainForm` already tracks `activeForm` and has an `OnMdiChildActivate` handler.

- [ ] **Step 1: Cancel nesting on MDI child close**

Find `OnMdiChildActivate` in MainForm (around line 290). Add a check when the active form changes or is set to null:

In the section where `activeForm` is set, add:

```csharp
// If nesting is in progress and the active form changed, cancel nesting
if (nestingInProgress && nestingCts != null)
{
    nestingCts.Cancel();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest/Forms/MainForm.cs
git commit -m "fix(ui): cancel nesting when MDI child form is closed"
```

---

### Task 12: Final Build and Manual Test

- [ ] **Step 1: Full solution build**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded, 0 warnings (or only pre-existing warnings)

- [ ] **Step 2: Manual smoke test checklist**

Launch the app and verify:

1. Open a nest with drawings
2. **Auto Nest** → shows progress form, parts appear on plate in preview color, stats update, Stop works, accepting gives real parts
3. **Fill Plate** → same as above for single plate
4. **Fill Area** → select area, press Enter, progress shows, Stop works
5. **Close MDI child during nesting** → nesting cancels cleanly
6. **Let nesting complete naturally** → progress form shows "Done", parts committed
7. **MCP tools still work** → the `bool Fill(...)` overloads are unchanged

- [ ] **Step 3: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix: address issues found during manual testing"
```
