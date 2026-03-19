# Two-Bucket Preview Parts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split PlateView nesting preview into stationary (overall best) and active (current strategy) layers so the preview never regresses and acceptance always uses the engine's result.

**Architecture:** Add `IsOverallBest` flag to `NestProgress` so the engine can distinguish overall-best reports from strategy-local progress. PlateView maintains two `List<LayoutPart>` buckets drawn at different opacities. Acceptance uses the engine's returned parts directly, decoupling preview from acceptance.

**Tech Stack:** C# / .NET 8 / WinForms / System.Drawing

**Spec:** `docs/superpowers/specs/2026-03-18-two-bucket-preview-design.md`

---

### Task 1: Add IsOverallBest to NestProgress and ReportProgress

**Files:**
- Modify: `OpenNest.Engine/NestProgress.cs:37-49`
- Modify: `OpenNest.Engine/NestEngineBase.cs:188-236`

- [ ] **Step 1: Add property to NestProgress**

In `OpenNest.Engine/NestProgress.cs`, add after line 48 (`ActiveWorkArea`):

```csharp
public bool IsOverallBest { get; set; }
```

- [ ] **Step 2: Add parameter to ReportProgress**

In `OpenNest.Engine/NestEngineBase.cs`, change the `ReportProgress` signature (line 188) to:

```csharp
internal static void ReportProgress(
    IProgress<NestProgress> progress,
    NestPhase phase,
    int plateNumber,
    List<Part> best,
    Box workArea,
    string description,
    bool isOverallBest = false)
```

In the same method, add `IsOverallBest = isOverallBest` to the `NestProgress` initializer (after line 235 `ActiveWorkArea = workArea`):

```csharp
IsOverallBest = isOverallBest,
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded, 0 errors. Existing callers use the default `false`.

- [ ] **Step 4: Commit**

```
feat(engine): add IsOverallBest flag to NestProgress
```

---

### Task 2: Flag overall-best reports in DefaultNestEngine

**Files:**
- Modify: `OpenNest.Engine/DefaultNestEngine.cs:55-58` (final report in Fill(NestItem))
- Modify: `OpenNest.Engine/DefaultNestEngine.cs:83-85` (final report in Fill(List<Part>))
- Modify: `OpenNest.Engine/DefaultNestEngine.cs:132-139` (RunPipeline strategy loop)

- [ ] **Step 1: Update RunPipeline — replace conditional report with unconditional overall-best report**

In `RunPipeline` (line 132-139), change from:

```csharp
if (IsBetterFill(result, context.CurrentBest, context.WorkArea))
{
    context.CurrentBest = result;
    context.CurrentBestScore = FillScore.Compute(result, context.WorkArea);
    context.WinnerPhase = strategy.Phase;
    ReportProgress(context.Progress, strategy.Phase, PlateNumber,
        result, context.WorkArea, BuildProgressSummary());
}
```

to:

```csharp
if (IsBetterFill(result, context.CurrentBest, context.WorkArea))
{
    context.CurrentBest = result;
    context.CurrentBestScore = FillScore.Compute(result, context.WorkArea);
    context.WinnerPhase = strategy.Phase;
}

if (context.CurrentBest != null && context.CurrentBest.Count > 0)
{
    ReportProgress(context.Progress, context.WinnerPhase, PlateNumber,
        context.CurrentBest, context.WorkArea, BuildProgressSummary(),
        isOverallBest: true);
}
```

- [ ] **Step 2: Flag final report in Fill(NestItem, Box, ...)**

In `Fill(NestItem item, Box workArea, ...)` (line 58), change:

```csharp
ReportProgress(progress, WinnerPhase, PlateNumber, best, workArea, BuildProgressSummary());
```

to:

```csharp
ReportProgress(progress, WinnerPhase, PlateNumber, best, workArea, BuildProgressSummary(),
    isOverallBest: true);
```

- [ ] **Step 3: Flag final report in Fill(List<Part>, Box, ...)**

In `Fill(List<Part> groupParts, Box workArea, ...)` (line 85), change:

```csharp
ReportProgress(progress, NestPhase.Linear, PlateNumber, best, workArea, BuildProgressSummary());
```

to:

```csharp
ReportProgress(progress, NestPhase.Linear, PlateNumber, best, workArea, BuildProgressSummary(),
    isOverallBest: true);
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```
feat(engine): flag overall-best progress reports in DefaultNestEngine
```

---

### Task 3: Add active preview style to ColorScheme

**Files:**
- Modify: `OpenNest/ColorScheme.cs:58-61` (pen/brush declarations)
- Modify: `OpenNest/ColorScheme.cs:160-176` (PreviewPartColor setter)

- [ ] **Step 1: Add pen/brush declarations**

In `ColorScheme.cs`, after line 60 (`PreviewPartBrush`), add:

```csharp
public Pen ActivePreviewPartPen { get; private set; }

public Brush ActivePreviewPartBrush { get; private set; }
```

- [ ] **Step 2: Create resources in PreviewPartColor setter**

In the `PreviewPartColor` setter (lines 160-176), change from:

```csharp
set
{
    previewPartColor = value;

    if (PreviewPartPen != null)
        PreviewPartPen.Dispose();

    if (PreviewPartBrush != null)
        PreviewPartBrush.Dispose();

    PreviewPartPen = new Pen(value, 1);
    PreviewPartBrush = new SolidBrush(Color.FromArgb(60, value));
}
```

to:

```csharp
set
{
    previewPartColor = value;

    if (PreviewPartPen != null)
        PreviewPartPen.Dispose();

    if (PreviewPartBrush != null)
        PreviewPartBrush.Dispose();

    if (ActivePreviewPartPen != null)
        ActivePreviewPartPen.Dispose();

    if (ActivePreviewPartBrush != null)
        ActivePreviewPartBrush.Dispose();

    PreviewPartPen = new Pen(value, 1);
    PreviewPartBrush = new SolidBrush(Color.FromArgb(60, value));
    ActivePreviewPartPen = new Pen(Color.FromArgb(128, value), 1);
    ActivePreviewPartBrush = new SolidBrush(Color.FromArgb(30, value));
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build OpenNest/OpenNest.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```
feat(ui): add active preview brush/pen to ColorScheme
```

---

### Task 4: Two-bucket preview parts in PlateView

**Files:**
- Modify: `OpenNest/Controls/PlateView.cs`

This task replaces the single `temporaryParts` list with `stationaryParts` and `activeParts`, updates the public API, drawing, and all internal references.

- [ ] **Step 1: Replace field and add new list**

Change line 34:

```csharp
private List<LayoutPart> temporaryParts = new List<LayoutPart>();
```

to:

```csharp
private List<LayoutPart> stationaryParts = new List<LayoutPart>();
private List<LayoutPart> activeParts = new List<LayoutPart>();
```

- [ ] **Step 2: Update SetPlate (line 152-153)**

Change:

```csharp
temporaryParts.Clear();
```

to:

```csharp
stationaryParts.Clear();
activeParts.Clear();
```

- [ ] **Step 3: Update Refresh (line 411)**

Change:

```csharp
temporaryParts.ForEach(p => p.Update(this));
```

to:

```csharp
stationaryParts.ForEach(p => p.Update(this));
activeParts.ForEach(p => p.Update(this));
```

- [ ] **Step 4: Update UpdateMatrix (line 1085)**

Change:

```csharp
temporaryParts.ForEach(p => p.Update(this));
```

to:

```csharp
stationaryParts.ForEach(p => p.Update(this));
activeParts.ForEach(p => p.Update(this));
```

- [ ] **Step 5: Replace the temporary parts drawing block in DrawParts (lines 506-522)**

Change:

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

to:

```csharp
// Draw stationary preview parts (overall best — full opacity)
for (var i = 0; i < stationaryParts.Count; i++)
{
    var part = stationaryParts[i];

    if (part.IsDirty)
        part.Update(this);

    var path = part.Path;
    if (!path.GetBounds().IntersectsWith(viewBounds))
        continue;

    g.FillPath(ColorScheme.PreviewPartBrush, path);
    g.DrawPath(ColorScheme.PreviewPartPen, path);
}

// Draw active preview parts (current strategy — reduced opacity)
for (var i = 0; i < activeParts.Count; i++)
{
    var part = activeParts[i];

    if (part.IsDirty)
        part.Update(this);

    var path = part.Path;
    if (!path.GetBounds().IntersectsWith(viewBounds))
        continue;

    g.FillPath(ColorScheme.ActivePreviewPartBrush, path);
    g.DrawPath(ColorScheme.ActivePreviewPartPen, path);
}
```

- [ ] **Step 6: Replace public API methods (lines 882-910)**

Replace `SetTemporaryParts`, `ClearTemporaryParts`, and `AcceptTemporaryParts` with:

```csharp
public void SetStationaryParts(List<Part> parts)
{
    stationaryParts.Clear();

    if (parts != null)
    {
        foreach (var part in parts)
            stationaryParts.Add(LayoutPart.Create(part, this));
    }

    Invalidate();
}

public void SetActiveParts(List<Part> parts)
{
    activeParts.Clear();

    if (parts != null)
    {
        foreach (var part in parts)
            activeParts.Add(LayoutPart.Create(part, this));
    }

    Invalidate();
}

public void ClearPreviewParts()
{
    stationaryParts.Clear();
    activeParts.Clear();
    Invalidate();
}

public void AcceptPreviewParts(List<Part> parts)
{
    if (parts != null)
    {
        foreach (var part in parts)
            Plate.Parts.Add(part);
    }

    stationaryParts.Clear();
    activeParts.Clear();
}
```

- [ ] **Step 7: Update FillWithProgress (lines 912-957)**

Change the progress callback (lines 918-923):

```csharp
var progress = new Progress<NestProgress>(p =>
{
    progressForm.UpdateProgress(p);
    SetTemporaryParts(p.BestParts);
    ActiveWorkArea = p.ActiveWorkArea;
});
```

to:

```csharp
var progress = new Progress<NestProgress>(p =>
{
    progressForm.UpdateProgress(p);

    if (p.IsOverallBest)
        SetStationaryParts(p.BestParts);
    else
        SetActiveParts(p.BestParts);

    ActiveWorkArea = p.ActiveWorkArea;
});
```

Change the acceptance block (lines 933-943):

```csharp
if (parts.Count > 0 && (!cts.IsCancellationRequested || progressForm.Accepted))
{
    SetTemporaryParts(parts);
    AcceptTemporaryParts();
    sw.Stop();
    Status = $"Fill: {parts.Count} parts in {sw.ElapsedMilliseconds} ms";
}
else
{
    ClearTemporaryParts();
}
```

to:

```csharp
if (parts.Count > 0 && (!cts.IsCancellationRequested || progressForm.Accepted))
{
    AcceptPreviewParts(parts);
    sw.Stop();
    Status = $"Fill: {parts.Count} parts in {sw.ElapsedMilliseconds} ms";
}
else
{
    ClearPreviewParts();
}
```

Change the catch block (line 949):

```csharp
ClearTemporaryParts();
```

to:

```csharp
ClearPreviewParts();
```

- [ ] **Step 8: Build to verify**

Run: `dotnet build OpenNest/OpenNest.csproj`
Expected: Build errors in `MainForm.cs` (still references old API). That is expected — Task 5 fixes it.

- [ ] **Step 9: Commit**

```
feat(ui): two-bucket preview parts in PlateView
```

---

### Task 5: Update MainForm progress callbacks and acceptance

**Files:**
- Modify: `OpenNest/Forms/MainForm.cs`

Three progress callback sites and their acceptance points need updating.

- [ ] **Step 1: Update auto-nest callback (RunAutoNest_Click, line 827)**

Change:

```csharp
var progress = new Progress<NestProgress>(p =>
{
    progressForm.UpdateProgress(p);
    activeForm.PlateView.SetTemporaryParts(p.BestParts);
    activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
});
```

to:

```csharp
var progress = new Progress<NestProgress>(p =>
{
    progressForm.UpdateProgress(p);

    if (p.IsOverallBest)
        activeForm.PlateView.SetStationaryParts(p.BestParts);
    else
        activeForm.PlateView.SetActiveParts(p.BestParts);

    activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
});
```

Change `ClearTemporaryParts()` on line 866 to `ClearPreviewParts()`.

Change `ClearTemporaryParts()` in the catch block (line 884) to `ClearPreviewParts()`.

- [ ] **Step 2: Update fill-plate callback (FillPlate_Click, line 962)**

Replace the progress setup (lines 962-976):

```csharp
var progressForm = new NestProgressForm(nestingCts, showPlateRow: false);
var highWaterMark = 0;

var progress = new Progress<NestProgress>(p =>
{
    progressForm.UpdateProgress(p);

    if (p.BestParts != null && p.BestPartCount >= highWaterMark)
    {
        highWaterMark = p.BestPartCount;
        activeForm.PlateView.SetTemporaryParts(p.BestParts);
    }

    activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
});
```

with:

```csharp
var progressForm = new NestProgressForm(nestingCts, showPlateRow: false);

var progress = new Progress<NestProgress>(p =>
{
    progressForm.UpdateProgress(p);

    if (p.IsOverallBest)
        activeForm.PlateView.SetStationaryParts(p.BestParts);
    else
        activeForm.PlateView.SetActiveParts(p.BestParts);

    activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
});
```

Change acceptance (line 990-993):

```csharp
if (parts.Count > 0)
    activeForm.PlateView.AcceptTemporaryParts();
else
    activeForm.PlateView.ClearTemporaryParts();
```

to:

```csharp
if (parts.Count > 0)
    activeForm.PlateView.AcceptPreviewParts(parts);
else
    activeForm.PlateView.ClearPreviewParts();
```

Change `ClearTemporaryParts()` in the catch block to `ClearPreviewParts()`.

- [ ] **Step 3: Update fill-area callback (FillArea_Click, line 1031)**

Replace the progress setup (lines 1031-1045):

```csharp
var progressForm = new NestProgressForm(nestingCts, showPlateRow: false);
var highWaterMark = 0;

var progress = new Progress<NestProgress>(p =>
{
    progressForm.UpdateProgress(p);

    if (p.BestParts != null && p.BestPartCount >= highWaterMark)
    {
        highWaterMark = p.BestPartCount;
        activeForm.PlateView.SetTemporaryParts(p.BestParts);
    }

    activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
});
```

with:

```csharp
var progressForm = new NestProgressForm(nestingCts, showPlateRow: false);

var progress = new Progress<NestProgress>(p =>
{
    progressForm.UpdateProgress(p);

    if (p.IsOverallBest)
        activeForm.PlateView.SetStationaryParts(p.BestParts);
    else
        activeForm.PlateView.SetActiveParts(p.BestParts);

    activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
});
```

Change the `onComplete` callback (lines 1047-1052):

```csharp
Action<List<Part>> onComplete = parts =>
{
    if (parts != null && parts.Count > 0)
        activeForm.PlateView.AcceptTemporaryParts();
    else
        activeForm.PlateView.ClearTemporaryParts();
```

to:

```csharp
Action<List<Part>> onComplete = parts =>
{
    if (parts != null && parts.Count > 0)
        activeForm.PlateView.AcceptPreviewParts(parts);
    else
        activeForm.PlateView.ClearPreviewParts();
```

- [ ] **Step 4: Build full solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded, 0 errors (only pre-existing nullable warnings in OpenNest.Gpu).

- [ ] **Step 5: Run tests**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```
feat(ui): route progress to stationary/active buckets in MainForm
```
