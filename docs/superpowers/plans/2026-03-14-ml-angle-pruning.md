# ML Angle Pruning Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Instrument the nesting engine to collect per-angle results during training runs, store them in the database, and add a `Description` field to the progress window for real-time visibility.

**Architecture:** The engine gains a `ForceFullAngleSweep` flag and an `AngleResults` collection populated during `FindBestFill`. `BruteForceResult` passes these through. The training project stores them in a new `AngleResults` SQLite table. The WinForms progress form gains a description row. ONNX inference (`AnglePredictor`) is a separate future task — it requires trained model data that doesn't exist yet.

**Tech Stack:** C# / .NET 8, EF Core SQLite, WinForms

**Spec:** `docs/superpowers/specs/2026-03-14-ml-angle-pruning-design.md`

---

## Chunk 1: Engine Instrumentation

### Task 1: Add `AngleResult` class and `Description` to `NestProgress`

**Files:**
- Modify: `OpenNest.Engine/NestProgress.cs`

- [ ] **Step 1: Add `AngleResult` class and `Description` property**

Add to `OpenNest.Engine/NestProgress.cs`, after the `PhaseResult` class:

```csharp
public class AngleResult
{
    public double AngleDeg { get; set; }
    public NestDirection Direction { get; set; }
    public int PartCount { get; set; }
}
```

Add to `NestProgress`:

```csharp
public string Description { get; set; }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestProgress.cs
git commit -m "feat(engine): add AngleResult class and Description to NestProgress"
```

---

### Task 2: Add `ForceFullAngleSweep` and `AngleResults` to `NestEngine`

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs`

- [ ] **Step 1: Add properties to `NestEngine`**

Add after the existing `PhaseResults` property (line 29):

```csharp
public bool ForceFullAngleSweep { get; set; }
public List<AngleResult> AngleResults { get; } = new();
```

- [ ] **Step 2: Clear `AngleResults` at the start of `Fill`**

In `Fill(NestItem item, Box workArea, IProgress<NestProgress> progress, CancellationToken token)` (line 52), add `AngleResults.Clear();` right after `PhaseResults.Clear();` (line 55).

- [ ] **Step 3: Force full angle sweep when flag is set**

In `FindBestFill(NestItem item, Box workArea, IProgress<NestProgress> progress, CancellationToken token)` (line 163), after the existing narrow-work-area angle expansion block (lines 182-190), add a second block:

```csharp
if (ForceFullAngleSweep)
{
    var step = Angle.ToRadians(5);
    for (var a = 0.0; a < System.Math.PI; a += step)
    {
        if (!angles.Any(existing => existing.IsEqualTo(a)))
            angles.Add(a);
    }
}
```

Also apply the same pattern to the non-progress overload `FindBestFill(NestItem item, Box workArea)` (line 84) — add the same `ForceFullAngleSweep` block after line 115.

- [ ] **Step 4: Collect per-angle results in the progress overload**

In `FindBestFill` (progress overload, line 163), inside the `Parallel.ForEach` over angles (lines 209-221), replace the parallel body to also collect angle results. Use a `ConcurrentBag<AngleResult>` alongside the existing `linearBag`:

Before the `Parallel.ForEach` (after line 207), add:
```csharp
var angleBag = new System.Collections.Concurrent.ConcurrentBag<AngleResult>();
```

Inside the parallel body, after computing `h` and `v`, add:
```csharp
var angleDeg = Angle.ToDegrees(angle);
if (h != null && h.Count > 0)
    angleBag.Add(new AngleResult { AngleDeg = angleDeg, Direction = NestDirection.Horizontal, PartCount = h.Count });
if (v != null && v.Count > 0)
    angleBag.Add(new AngleResult { AngleDeg = angleDeg, Direction = NestDirection.Vertical, PartCount = v.Count });
```

After the `Parallel.ForEach` completes and `linearSw.Stop()` (line 222), add:
```csharp
AngleResults.AddRange(angleBag);
```

- [ ] **Step 5: Add `ForceFullAngleSweep` to non-progress overload (angle sweep only, no result collection)**

The non-progress `FindBestFill` at line 84 is only called from `TryStripRefill` for sub-strip fills. Do NOT collect angle results there — sub-strip data would contaminate the main plate's results. Only add the `ForceFullAngleSweep` block (same as Step 3) after line 115.

Do NOT add `AngleResults.Clear()` to `Fill(NestItem, Box)` at line 41 — it delegates to the progress overload at line 52 which already clears `AngleResults`.

- [ ] **Step 6: Report per-angle progress descriptions**

In the progress overload's `Parallel.ForEach` body, after computing h and v for an angle, report progress with a description. Since we're inside a parallel loop, use a simple approach — report after computing each angle:

```csharp
var bestDir = (h?.Count ?? 0) >= (v?.Count ?? 0) ? "H" : "V";
var bestCount = System.Math.Max(h?.Count ?? 0, v?.Count ?? 0);
progress?.Report(new NestProgress
{
    Phase = NestPhase.Linear,
    PlateNumber = PlateNumber,
    Description = $"Linear: {angleDeg:F0}\u00b0 {bestDir} - {bestCount} parts"
});
```

- [ ] **Step 7: Report per-candidate progress in Pairs phase**

In `FillWithPairs(NestItem item, Box workArea, CancellationToken token)` (line 409), inside the `Parallel.For` body (line 424), add progress reporting. Since `FillWithPairs` doesn't have access to the `progress` parameter, this requires adding a progress parameter.

Change the signature of the cancellation-token overload at line 409 to:

```csharp
private List<Part> FillWithPairs(NestItem item, Box workArea, CancellationToken token, IProgress<NestProgress> progress = null)
```

Update the call site at line 194 (`FindBestFill` progress overload) to pass `progress`:

```csharp
var pairResult = FillWithPairs(item, workArea, token, progress);
```

Inside the `Parallel.For` body (line 424), after computing `filled`, add:

```csharp
progress?.Report(new NestProgress
{
    Phase = NestPhase.Pairs,
    PlateNumber = PlateNumber,
    Description = $"Pairs: candidate {i + 1}/{candidates.Count} - {filled?.Count ?? 0} parts"
});
```

- [ ] **Step 8: Build to verify**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeded

- [ ] **Step 9: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat(engine): add ForceFullAngleSweep flag and per-angle result collection"
```

---

### Task 3: Add `AngleResults` to `BruteForceResult` and `BruteForceRunner`

**Files:**
- Modify: `OpenNest.Engine/ML/BruteForceRunner.cs`

- [ ] **Step 1: Add `AngleResults` property to `BruteForceResult`**

Add to `BruteForceResult` class (after `ThirdPlaceTimeMs`):

```csharp
public List<AngleResult> AngleResults { get; set; } = new();
```

- [ ] **Step 2: Populate `AngleResults` in `BruteForceRunner.Run`**

In the `return new BruteForceResult` block (line 47), add:

```csharp
AngleResults = engine.AngleResults.ToList(),
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Engine/ML/BruteForceRunner.cs
git commit -m "feat(engine): pass per-angle results through BruteForceResult"
```

---

## Chunk 2: Training Database & Runner

### Task 4: Add `TrainingAngleResult` EF Core entity

**Files:**
- Create: `OpenNest.Training/Data/TrainingAngleResult.cs`
- Modify: `OpenNest.Training/Data/TrainingDbContext.cs`

- [ ] **Step 1: Create the entity class**

Create `OpenNest.Training/Data/TrainingAngleResult.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenNest.Training.Data
{
    [Table("AngleResults")]
    public class TrainingAngleResult
    {
        [Key]
        public long Id { get; set; }

        public long RunId { get; set; }
        public double AngleDeg { get; set; }
        public string Direction { get; set; }
        public int PartCount { get; set; }

        [ForeignKey(nameof(RunId))]
        public TrainingRun Run { get; set; }
    }
}
```

- [ ] **Step 2: Add navigation property to `TrainingRun`**

In `OpenNest.Training/Data/TrainingRun.cs`, add at the end of the class (after the `Part` navigation property):

```csharp
public List<TrainingAngleResult> AngleResults { get; set; } = new();
```

Add `using System.Collections.Generic;` to the top if not already present.

- [ ] **Step 3: Register `DbSet` and configure in `TrainingDbContext`**

In `OpenNest.Training/Data/TrainingDbContext.cs`:

Add DbSet:
```csharp
public DbSet<TrainingAngleResult> AngleResults { get; set; }
```

Add configuration in `OnModelCreating`:
```csharp
modelBuilder.Entity<TrainingAngleResult>(e =>
{
    e.HasIndex(a => a.RunId).HasDatabaseName("idx_angleresults_runid");
    e.HasOne(a => a.Run)
        .WithMany(r => r.AngleResults)
        .HasForeignKey(a => a.RunId);
});
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build OpenNest.Training`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Training/Data/TrainingAngleResult.cs OpenNest.Training/Data/TrainingRun.cs OpenNest.Training/Data/TrainingDbContext.cs
git commit -m "feat(training): add TrainingAngleResult entity and DbSet"
```

---

### Task 5: Extend `TrainingDatabase` for angle result storage and migration

**Files:**
- Modify: `OpenNest.Training/TrainingDatabase.cs`

- [ ] **Step 1: Create `AngleResults` table in `MigrateSchema`**

Add to the end of the `MigrateSchema` method, after the existing column migration loop:

```csharp
try
{
    _db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS AngleResults (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RunId INTEGER NOT NULL,
            AngleDeg REAL NOT NULL,
            Direction TEXT NOT NULL,
            PartCount INTEGER NOT NULL,
            FOREIGN KEY (RunId) REFERENCES Runs(Id)
        )");
    _db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS idx_angleresults_runid ON AngleResults (RunId)");
}
catch
{
    // Table already exists or other non-fatal issue.
}
```

- [ ] **Step 2: Extend `AddRun` to accept and batch-insert angle results**

Change the `AddRun` signature to accept angle results:

```csharp
public void AddRun(long partId, double w, double h, double s, BruteForceResult result, string filePath, List<AngleResult> angleResults = null)
```

Add `using OpenNest;` at the top if not already present (for `AngleResult` type).

After `_db.Runs.Add(run);` and before `_db.SaveChanges();`, add:

```csharp
if (angleResults != null && angleResults.Count > 0)
{
    foreach (var ar in angleResults)
    {
        _db.AngleResults.Add(new Data.TrainingAngleResult
        {
            Run = run,
            AngleDeg = ar.AngleDeg,
            Direction = ar.Direction.ToString(),
            PartCount = ar.PartCount
        });
    }
}
```

The single `SaveChanges()` call will batch-insert both the run and all angle results in one transaction.

- [ ] **Step 3: Build to verify**

Run: `dotnet build OpenNest.Training`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Training/TrainingDatabase.cs
git commit -m "feat(training): add AngleResults table migration and batch insert"
```

---

### Task 6: Wire up training runner to collect angle data

**Files:**
- Modify: `OpenNest.Training/Program.cs`

- [ ] **Step 1: Set `ForceFullAngleSweep` on the engine**

In `Program.cs`, inside the `foreach (var size in sheetSuite)` loop, after creating the `BruteForceRunner.Run` call (line 203), we need to change the approach. Currently `BruteForceRunner.Run` creates the engine internally. We need to modify `BruteForceRunner.Run` to accept the `ForceFullAngleSweep` flag.

Actually, looking at the code, `BruteForceRunner.Run` creates a `NestEngine` internally (line 29 of BruteForceRunner.cs). The cleanest approach: add an overload or optional parameter.

In `OpenNest.Engine/ML/BruteForceRunner.cs`, change the `Run` method signature to:

```csharp
public static BruteForceResult Run(Drawing drawing, Plate plate, bool forceFullAngleSweep = false)
```

And set it on the engine after creation:

```csharp
var engine = new NestEngine(plate);
engine.ForceFullAngleSweep = forceFullAngleSweep;
```

- [ ] **Step 2: Pass `forceFullAngleSweep = true` from the training runner**

In `OpenNest.Training/Program.cs`, change the `BruteForceRunner.Run` call (line 203) to:

```csharp
var result = BruteForceRunner.Run(drawing, runPlate, forceFullAngleSweep: true);
```

- [ ] **Step 3: Pass angle results to `AddRun`**

Change the `db.AddRun` call (line 266) to:

```csharp
db.AddRun(partId, size.Width, size.Length, s, result, savedFilePath, result.AngleResults);
```

- [ ] **Step 4: Add angle result count to console output**

In the console output line (line 223), append angle result count. Change:

```csharp
Console.WriteLine($"  {size.Length}x{size.Width} - {result.PartCount}pcs, {result.Utilization:P1}, {sizeSw.ElapsedMilliseconds}ms [{engineInfo}]");
```

To:

```csharp
Console.WriteLine($"  {size.Length}x{size.Width} - {result.PartCount}pcs, {result.Utilization:P1}, {sizeSw.ElapsedMilliseconds}ms [{engineInfo}] angles={result.AngleResults.Count}");
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build OpenNest.Training`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add OpenNest.Engine/ML/BruteForceRunner.cs OpenNest.Training/Program.cs
git commit -m "feat(training): enable forced full angle sweep and store per-angle results"
```

---

## Chunk 3: Progress Window Enhancement

### Task 7: Add `Description` row to the progress form

**Files:**
- Modify: `OpenNest/Forms/NestProgressForm.Designer.cs`
- Modify: `OpenNest/Forms/NestProgressForm.cs`

- [ ] **Step 1: Add description label and value controls in Designer**

In `NestProgressForm.Designer.cs`, add field declarations alongside the existing ones (after `elapsedValue` on line 231):

```csharp
private System.Windows.Forms.Label descriptionLabel;
private System.Windows.Forms.Label descriptionValue;
```

In `InitializeComponent()`, add control creation (after the `elapsedValue` creation, around line 43):

```csharp
this.descriptionLabel = new System.Windows.Forms.Label();
this.descriptionValue = new System.Windows.Forms.Label();
```

Add the description row to the table. Exact changes:
- Line 71: Change `this.table.RowCount = 6;` to `this.table.RowCount = 7;`
- After line 77 (last `RowStyles.Add`), add: `this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));`
- After line 66 (elapsedValue table.Controls.Add), add the description controls to the table
- Line 197: Change `this.ClientSize = new System.Drawing.Size(264, 207);` to `this.ClientSize = new System.Drawing.Size(264, 230);` (taller to fit the new row)

Add table controls (after the elapsed row controls):
```csharp
this.table.Controls.Add(this.descriptionLabel, 0, 6);
this.table.Controls.Add(this.descriptionValue, 1, 6);
```

Configure the labels (in the label configuration section, after elapsedValue config):
```csharp
// descriptionLabel
this.descriptionLabel.AutoSize = true;
this.descriptionLabel.Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold);
this.descriptionLabel.Margin = new System.Windows.Forms.Padding(4);
this.descriptionLabel.Name = "descriptionLabel";
this.descriptionLabel.Text = "Detail:";
// descriptionValue
this.descriptionValue.AutoSize = true;
this.descriptionValue.Margin = new System.Windows.Forms.Padding(4);
this.descriptionValue.Name = "descriptionValue";
this.descriptionValue.Text = "\u2014";
```

Add field declarations after `elapsedValue` (line 230), before `stopButton` (line 231):

- [ ] **Step 2: Display `Description` in `UpdateProgress`**

In `NestProgressForm.cs`, in the `UpdateProgress` method (line 30), add after the existing updates:

```csharp
if (!string.IsNullOrEmpty(progress.Description))
    descriptionValue.Text = progress.Description;
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add OpenNest/Forms/NestProgressForm.cs OpenNest/Forms/NestProgressForm.Designer.cs
git commit -m "feat(ui): add description row to nest progress form"
```

---

## Chunk 4: ONNX Inference Scaffolding

### Task 8: Add `Microsoft.ML.OnnxRuntime` NuGet package

**Files:**
- Modify: `OpenNest.Engine/OpenNest.Engine.csproj`

- [ ] **Step 1: Add the package reference**

Add to `OpenNest.Engine/OpenNest.Engine.csproj` inside the existing `<ItemGroup>` (or create a new one for packages):

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.17.3" />
</ItemGroup>
```

- [ ] **Step 2: Restore and build**

Run: `dotnet restore OpenNest.Engine && dotnet build OpenNest.Engine`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/OpenNest.Engine.csproj
git commit -m "chore(engine): add Microsoft.ML.OnnxRuntime package"
```

---

### Task 9: Create `AnglePredictor` with ONNX inference

**Files:**
- Create: `OpenNest.Engine/ML/AnglePredictor.cs`

- [ ] **Step 1: Create the predictor class**

Create `OpenNest.Engine/ML/AnglePredictor.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenNest.Math;

namespace OpenNest.Engine.ML
{
    public static class AnglePredictor
    {
        private static InferenceSession _session;
        private static bool _loadAttempted;
        private static readonly object _lock = new();

        public static List<double> PredictAngles(
            PartFeatures features, double sheetWidth, double sheetHeight,
            double threshold = 0.3)
        {
            var session = GetSession();
            if (session == null)
                return null;

            try
            {
                var input = new float[11];
                input[0] = (float)features.Area;
                input[1] = (float)features.Convexity;
                input[2] = (float)features.AspectRatio;
                input[3] = (float)features.BoundingBoxFill;
                input[4] = (float)features.Circularity;
                input[5] = (float)features.PerimeterToAreaRatio;
                input[6] = features.VertexCount;
                input[7] = (float)sheetWidth;
                input[8] = (float)sheetHeight;
                input[9] = (float)(sheetWidth / (sheetHeight > 0 ? sheetHeight : 1.0));
                input[10] = (float)(features.Area / (sheetWidth * sheetHeight));

                var tensor = new DenseTensor<float>(input, new[] { 1, 11 });
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("features", tensor)
                };

                using var results = session.Run(inputs);
                var probabilities = results.First().AsEnumerable<float>().ToArray();

                var angles = new List<(double angleDeg, float prob)>();
                for (var i = 0; i < 36 && i < probabilities.Length; i++)
                {
                    if (probabilities[i] >= threshold)
                        angles.Add((i * 5.0, probabilities[i]));
                }

                // Minimum 3 angles — take top by probability if fewer pass threshold.
                if (angles.Count < 3)
                {
                    angles = probabilities
                        .Select((p, i) => (angleDeg: i * 5.0, prob: p))
                        .OrderByDescending(x => x.prob)
                        .Take(3)
                        .ToList();
                }

                // Always include 0 and 90 as safety fallback.
                var result = angles.Select(a => Angle.ToRadians(a.angleDeg)).ToList();

                if (!result.Any(a => a.IsEqualTo(0)))
                    result.Add(0);
                if (!result.Any(a => a.IsEqualTo(Angle.HalfPI)))
                    result.Add(Angle.HalfPI);

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnglePredictor] Inference failed: {ex.Message}");
                return null;
            }
        }

        private static InferenceSession GetSession()
        {
            if (_loadAttempted)
                return _session;

            lock (_lock)
            {
                if (_loadAttempted)
                    return _session;

                _loadAttempted = true;

                try
                {
                    var dir = Path.GetDirectoryName(typeof(AnglePredictor).Assembly.Location);
                    var modelPath = Path.Combine(dir, "Models", "angle_predictor.onnx");

                    if (!File.Exists(modelPath))
                    {
                        Debug.WriteLine($"[AnglePredictor] Model not found: {modelPath}");
                        return null;
                    }

                    _session = new InferenceSession(modelPath);
                    Debug.WriteLine("[AnglePredictor] Model loaded successfully");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AnglePredictor] Failed to load model: {ex.Message}");
                }

                return _session;
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeded (model file doesn't exist yet — that's expected, `GetSession` returns null gracefully)

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/ML/AnglePredictor.cs
git commit -m "feat(engine): add AnglePredictor ONNX inference class"
```

---

### Task 10: Wire `AnglePredictor` into `NestEngine.FindBestFill`

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs`

- [ ] **Step 1: Add ML angle prediction to the progress overload**

In `FindBestFill` (progress overload), after the narrow-work-area angle expansion block and after the `ForceFullAngleSweep` block, add ML prediction logic. This replaces the full sweep when the model is available:

```csharp
// When the work area triggers a full sweep (and we're not forcing it for training),
// try ML angle prediction to reduce the sweep.
if (!ForceFullAngleSweep && angles.Count > 2)
{
    var features = FeatureExtractor.Extract(item.Drawing);
    if (features != null)
    {
        var predicted = AnglePredictor.PredictAngles(
            features, workArea.Width, workArea.Length);

        if (predicted != null)
        {
            // Use predicted angles, but always keep bestRotation and bestRotation + 90.
            var mlAngles = new List<double>(predicted);

            if (!mlAngles.Any(a => a.IsEqualTo(bestRotation)))
                mlAngles.Add(bestRotation);
            if (!mlAngles.Any(a => a.IsEqualTo(bestRotation + Angle.HalfPI)))
                mlAngles.Add(bestRotation + Angle.HalfPI);

            Debug.WriteLine($"[FindBestFill] ML: {angles.Count} angles -> {mlAngles.Count} predicted");
            angles = mlAngles;
        }
    }
}
```

Add `using OpenNest.Engine.ML;` at the top of the file if not already present.

- [ ] **Step 2: Apply the same pattern to the non-progress overload**

Add the identical ML prediction block to `FindBestFill(NestItem item, Box workArea)` after its `ForceFullAngleSweep` block.

- [ ] **Step 3: Build the full solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat(engine): integrate AnglePredictor into FindBestFill angle selection"
```

---

## Chunk 5: Training Notebook Scaffolding

### Task 11: Create Python training notebook and requirements

**Files:**
- Create: `OpenNest.Training/notebooks/requirements.txt`
- Create: `OpenNest.Training/notebooks/train_angle_model.ipynb`

- [ ] **Step 1: Create requirements.txt**

Create `OpenNest.Training/notebooks/requirements.txt`:

```
pandas>=2.0
scikit-learn>=1.3
xgboost>=2.0
onnxmltools>=1.12
skl2onnx>=1.16
matplotlib>=3.7
jupyter>=1.0
```

- [ ] **Step 2: Create training notebook skeleton**

Create `OpenNest.Training/notebooks/train_angle_model.ipynb` as a Jupyter notebook with the following cells:

Cell 1 (markdown):
```
# Angle Prediction Model Training
Trains an XGBoost multi-label classifier to predict which rotation angles are competitive for a given part geometry and sheet size.

**Input:** SQLite database from OpenNest.Training data collection runs
**Output:** `angle_predictor.onnx` model file for `OpenNest.Engine/Models/`
```

Cell 2 (code): imports and setup
```python
import sqlite3
import pandas as pd
import numpy as np
from pathlib import Path

DB_PATH = "../OpenNestTraining.db"  # Adjust to your database location
OUTPUT_PATH = "../../OpenNest.Engine/Models/angle_predictor.onnx"
COMPETITIVE_THRESHOLD = 0.95  # Angle is "competitive" if >= 95% of best
```

Cell 3 (code): data extraction
```python
# Extract training data from SQLite
conn = sqlite3.connect(DB_PATH)

query = """
SELECT
    p.Area, p.Convexity, p.AspectRatio, p.BBFill, p.Circularity,
    p.PerimeterToAreaRatio, p.VertexCount,
    r.SheetWidth, r.SheetHeight, r.Id as RunId,
    a.AngleDeg, a.Direction, a.PartCount
FROM AngleResults a
JOIN Runs r ON a.RunId = r.Id
JOIN Parts p ON r.PartId = p.Id
WHERE a.PartCount > 0
"""

df = pd.read_sql_query(query, conn)
conn.close()

print(f"Loaded {len(df)} angle result rows")
print(f"Unique runs: {df['RunId'].nunique()}")
print(f"Angle range: {df['AngleDeg'].min()}-{df['AngleDeg'].max()}")
```

Cell 4 (code): label generation
```python
# For each run, find best PartCount (max of H and V per angle),
# then label angles within 95% of best as positive.

# Best count per angle per run (max of H and V)
angle_best = df.groupby(['RunId', 'AngleDeg'])['PartCount'].max().reset_index()
angle_best.columns = ['RunId', 'AngleDeg', 'BestCount']

# Best count per run (overall best angle)
run_best = angle_best.groupby('RunId')['BestCount'].max().reset_index()
run_best.columns = ['RunId', 'RunBest']

# Merge and compute labels
labels = angle_best.merge(run_best, on='RunId')
labels['IsCompetitive'] = (labels['BestCount'] >= labels['RunBest'] * COMPETITIVE_THRESHOLD).astype(int)

# Pivot to 36-column binary label matrix
label_matrix = labels.pivot_table(
    index='RunId', columns='AngleDeg', values='IsCompetitive', fill_value=0
)

# Ensure all 36 angle columns exist (0, 5, 10, ..., 175)
all_angles = [i * 5 for i in range(36)]
for a in all_angles:
    if a not in label_matrix.columns:
        label_matrix[a] = 0
label_matrix = label_matrix[all_angles]

print(f"Label matrix: {label_matrix.shape}")
print(f"Average competitive angles per run: {label_matrix.sum(axis=1).mean():.1f}")
```

Cell 5 (code): feature engineering
```python
# Build feature matrix — one row per run
features_query = """
SELECT DISTINCT
    r.Id as RunId, p.FileName,
    p.Area, p.Convexity, p.AspectRatio, p.BBFill, p.Circularity,
    p.PerimeterToAreaRatio, p.VertexCount,
    r.SheetWidth, r.SheetHeight
FROM Runs r
JOIN Parts p ON r.PartId = p.Id
WHERE r.Id IN ({})
""".format(','.join(str(x) for x in label_matrix.index))

conn = sqlite3.connect(DB_PATH)
features_df = pd.read_sql_query(features_query, conn)
conn.close()

features_df = features_df.set_index('RunId')

# Derived features
features_df['SheetAspectRatio'] = features_df['SheetWidth'] / features_df['SheetHeight']
features_df['PartToSheetAreaRatio'] = features_df['Area'] / (features_df['SheetWidth'] * features_df['SheetHeight'])

# Filter outliers (title blocks, etc.)
mask = (features_df['BBFill'] >= 0.01) & (features_df['Area'] > 0.1)
print(f"Filtering: {(~mask).sum()} outlier runs removed")
features_df = features_df[mask]
label_matrix = label_matrix.loc[features_df.index]

feature_cols = ['Area', 'Convexity', 'AspectRatio', 'BBFill', 'Circularity',
                'PerimeterToAreaRatio', 'VertexCount',
                'SheetWidth', 'SheetHeight', 'SheetAspectRatio', 'PartToSheetAreaRatio']

X = features_df[feature_cols].values
y = label_matrix.values

print(f"Features: {X.shape}, Labels: {y.shape}")
```

Cell 6 (code): train/test split and training
```python
from sklearn.model_selection import GroupShuffleSplit
from sklearn.multioutput import MultiOutputClassifier
import xgboost as xgb

# Split by part (all sheet sizes for a part stay in the same split)
groups = features_df['FileName']
splitter = GroupShuffleSplit(n_splits=1, test_size=0.2, random_state=42)
train_idx, test_idx = next(splitter.split(X, y, groups))

X_train, X_test = X[train_idx], X[test_idx]
y_train, y_test = y[train_idx], y[test_idx]

print(f"Train: {len(train_idx)}, Test: {len(test_idx)}")

# Train XGBoost multi-label classifier
base_clf = xgb.XGBClassifier(
    n_estimators=200,
    max_depth=6,
    learning_rate=0.1,
    use_label_encoder=False,
    eval_metric='logloss',
    random_state=42
)

clf = MultiOutputClassifier(base_clf, n_jobs=-1)
clf.fit(X_train, y_train)
print("Training complete")
```

Cell 7 (code): evaluation
```python
from sklearn.metrics import recall_score, precision_score
import matplotlib.pyplot as plt

y_pred = clf.predict(X_test)
y_prob = np.array([est.predict_proba(X_test)[:, 1] for est in clf.estimators_]).T

# Per-angle metrics
recalls = []
precisions = []
for i in range(36):
    if y_test[:, i].sum() > 0:
        recalls.append(recall_score(y_test[:, i], y_pred[:, i], zero_division=0))
        precisions.append(precision_score(y_test[:, i], y_pred[:, i], zero_division=0))

print(f"Mean recall:    {np.mean(recalls):.3f}")
print(f"Mean precision: {np.mean(precisions):.3f}")

# Average angles predicted per run
avg_predicted = y_pred.sum(axis=1).mean()
print(f"Avg angles predicted per run: {avg_predicted:.1f}")

# Plot
fig, axes = plt.subplots(1, 2, figsize=(12, 4))
axes[0].bar(range(len(recalls)), recalls)
axes[0].set_title('Recall per Angle Bin')
axes[0].set_xlabel('Angle (5-deg bins)')
axes[0].axhline(y=0.95, color='r', linestyle='--', label='Target 95%')
axes[0].legend()

axes[1].bar(range(len(precisions)), precisions)
axes[1].set_title('Precision per Angle Bin')
axes[1].set_xlabel('Angle (5-deg bins)')
axes[1].axhline(y=0.60, color='r', linestyle='--', label='Target 60%')
axes[1].legend()

plt.tight_layout()
plt.show()
```

Cell 8 (code): export to ONNX
```python
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType
from pathlib import Path

initial_type = [('features', FloatTensorType([None, 11]))]
onnx_model = convert_sklearn(clf, initial_types=initial_type)

output_path = Path(OUTPUT_PATH)
output_path.parent.mkdir(parents=True, exist_ok=True)

with open(output_path, 'wb') as f:
    f.write(onnx_model.SerializeToString())

print(f"Model saved to {output_path} ({output_path.stat().st_size / 1024:.0f} KB)")
```

- [ ] **Step 3: Create the `Models` directory placeholder**

Run: `mkdir -p OpenNest.Engine/Models`

Create `OpenNest.Engine/Models/.gitkeep` (empty file to track the directory).

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Training/notebooks/ OpenNest.Engine/Models/.gitkeep
git commit -m "feat(training): add training notebook skeleton and requirements"
```

---

## Summary

| Chunk | Tasks | Purpose |
|-------|-------|---------|
| 1 | 1-3 | Engine instrumentation: `AngleResult`, `ForceFullAngleSweep`, per-angle collection |
| 2 | 4-6 | Training DB: `AngleResults` table, migration, runner wiring |
| 3 | 7 | Progress window: `Description` display |
| 4 | 8-10 | ONNX inference: `AnglePredictor` class, NuGet package, `FindBestFill` integration |
| 5 | 11 | Python notebook: training pipeline skeleton |

**Dependency order:** Chunks 1-2 must be sequential (2 depends on 1). Chunks 3, 4, 5 are independent of each other and can be done in parallel after Chunk 1.

**After this plan:** Run training data collection with `--force-sweep` (the existing training runner + new angle collection). Once data exists, run the notebook to train and export the ONNX model. The engine will automatically use it once `angle_predictor.onnx` is placed in the `Models/` directory.
