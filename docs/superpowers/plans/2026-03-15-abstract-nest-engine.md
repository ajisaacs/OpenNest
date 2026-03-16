# Abstract Nest Engine Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the concrete `NestEngine` into an abstract `NestEngineBase` with pluggable implementations, a registry for engine discovery/selection, and plugin loading from DLLs.

**Architecture:** Extract shared state and utilities into `NestEngineBase` (abstract). Current logic becomes `DefaultNestEngine`. `NestEngineRegistry` provides factory creation, built-in registration, and DLL plugin discovery. All callsites migrate from `new NestEngine(plate)` to `NestEngineRegistry.Create(plate)`.

**Tech Stack:** C# / .NET 8, OpenNest.Engine, OpenNest (WinForms), OpenNest.Mcp, OpenNest.Console

**Spec:** `docs/superpowers/specs/2026-03-15-abstract-nest-engine-design.md`

**Deferred:** `StripNester.cs` → `StripNestEngine.cs` conversion is deferred to the strip nester implementation plan (`docs/superpowers/plans/2026-03-15-strip-nester.md`). That plan should be updated to create `StripNestEngine` as a `NestEngineBase` subclass and register it in `NestEngineRegistry`. The UI engine selector combobox is also deferred — it can be added once there are multiple engines to choose from.

---

## Chunk 1: NestEngineBase and DefaultNestEngine

### Task 1: Create NestEngineBase abstract class

**Files:**
- Create: `OpenNest.Engine/NestEngineBase.cs`

This is the abstract base class. It holds shared properties, abstract `Name`/`Description`, virtual methods that return empty lists by default, convenience overloads that mutate the plate, `FillExact` (non-virtual), and protected utility methods extracted from the current `NestEngine`.

- [ ] **Step 1: Create NestEngineBase.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest
{
    public abstract class NestEngineBase
    {
        protected NestEngineBase(Plate plate)
        {
            Plate = plate;
        }

        public Plate Plate { get; set; }

        public int PlateNumber { get; set; }

        public NestDirection NestDirection { get; set; }

        public NestPhase WinnerPhase { get; protected set; }

        public List<PhaseResult> PhaseResults { get; } = new();

        public List<AngleResult> AngleResults { get; } = new();

        public abstract string Name { get; }

        public abstract string Description { get; }

        // --- Virtual methods (side-effect-free, return parts) ---

        public virtual List<Part> Fill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            return new List<Part>();
        }

        public virtual List<Part> Fill(List<Part> groupParts, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            return new List<Part>();
        }

        public virtual List<Part> PackArea(Box box, List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            return new List<Part>();
        }

        // --- FillExact (non-virtual, delegates to virtual Fill) ---

        public List<Part> FillExact(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            return Fill(item, workArea, progress, token);
        }

        // --- Convenience overloads (mutate plate, return bool) ---

        public bool Fill(NestItem item)
        {
            return Fill(item, Plate.WorkArea());
        }

        public bool Fill(NestItem item, Box workArea)
        {
            var parts = Fill(item, workArea, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        public bool Fill(List<Part> groupParts)
        {
            return Fill(groupParts, Plate.WorkArea());
        }

        public bool Fill(List<Part> groupParts, Box workArea)
        {
            var parts = Fill(groupParts, workArea, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        public bool Pack(List<NestItem> items)
        {
            var workArea = Plate.WorkArea();
            var parts = PackArea(workArea, items, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        // --- Protected utilities ---

        protected static void ReportProgress(
            IProgress<NestProgress> progress,
            NestPhase phase,
            int plateNumber,
            List<Part> best,
            Box workArea,
            string description)
        {
            if (progress == null || best == null || best.Count == 0)
                return;

            var score = FillScore.Compute(best, workArea);
            var clonedParts = new List<Part>(best.Count);
            var totalPartArea = 0.0;

            foreach (var part in best)
            {
                clonedParts.Add((Part)part.Clone());
                totalPartArea += part.BaseDrawing.Area;
            }

            var bounds = best.GetBoundingBox();

            var msg = $"[Progress] Phase={phase}, Plate={plateNumber}, Parts={score.Count}, " +
                      $"Density={score.Density:P1}, Nested={bounds.Width:F1}x{bounds.Length:F1}, " +
                      $"PartArea={totalPartArea:F0}, Remnant={workArea.Area() - totalPartArea:F0}, " +
                      $"WorkArea={workArea.Width:F1}x{workArea.Length:F1} | {description}";
            Debug.WriteLine(msg);
            try { System.IO.File.AppendAllText(
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "nest-debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); } catch { }

            progress.Report(new NestProgress
            {
                Phase = phase,
                PlateNumber = plateNumber,
                BestPartCount = score.Count,
                BestDensity = score.Density,
                NestedWidth = bounds.Width,
                NestedLength = bounds.Length,
                NestedArea = totalPartArea,
                UsableRemnantArea = workArea.Area() - totalPartArea,
                BestParts = clonedParts,
                Description = description
            });
        }

        protected string BuildProgressSummary()
        {
            if (PhaseResults.Count == 0)
                return null;

            var parts = new List<string>(PhaseResults.Count);

            foreach (var r in PhaseResults)
                parts.Add($"{FormatPhaseName(r.Phase)}: {r.PartCount}");

            return string.Join(" | ", parts);
        }

        protected bool IsBetterFill(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (candidate == null || candidate.Count == 0)
                return false;

            if (current == null || current.Count == 0)
                return true;

            return FillScore.Compute(candidate, workArea) > FillScore.Compute(current, workArea);
        }

        protected bool IsBetterValidFill(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (candidate != null && candidate.Count > 0 && HasOverlaps(candidate, Plate.PartSpacing))
            {
                Debug.WriteLine($"[IsBetterValidFill] REJECTED {candidate.Count} parts due to overlaps (current best: {current?.Count ?? 0})");
                return false;
            }

            return IsBetterFill(candidate, current, workArea);
        }

        protected static bool HasOverlaps(List<Part> parts, double spacing)
        {
            if (parts == null || parts.Count <= 1)
                return false;

            for (var i = 0; i < parts.Count; i++)
            {
                var box1 = parts[i].BoundingBox;

                for (var j = i + 1; j < parts.Count; j++)
                {
                    var box2 = parts[j].BoundingBox;

                    if (box1.Right < box2.Left || box2.Right < box1.Left ||
                        box1.Top < box2.Bottom || box2.Top < box1.Bottom)
                        continue;

                    List<Vector> pts;

                    if (parts[i].Intersects(parts[j], out pts))
                    {
                        var b1 = parts[i].BoundingBox;
                        var b2 = parts[j].BoundingBox;
                        Debug.WriteLine($"[HasOverlaps] Overlap: part[{i}] ({parts[i].BaseDrawing?.Name}) @ ({b1.Left:F2},{b1.Bottom:F2})-({b1.Right:F2},{b1.Top:F2}) rot={parts[i].Rotation:F2}" +
                            $" vs part[{j}] ({parts[j].BaseDrawing?.Name}) @ ({b2.Left:F2},{b2.Bottom:F2})-({b2.Right:F2},{b2.Top:F2}) rot={parts[j].Rotation:F2}" +
                            $" intersections={pts?.Count ?? 0}");
                        return true;
                    }
                }
            }

            return false;
        }

        protected static string FormatPhaseName(NestPhase phase)
        {
            switch (phase)
            {
                case NestPhase.Pairs: return "Pairs";
                case NestPhase.Linear: return "Linear";
                case NestPhase.RectBestFit: return "BestFit";
                case NestPhase.Remainder: return "Remainder";
                default: return phase.ToString();
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngineBase.cs
git commit -m "feat: add NestEngineBase abstract class"
```

---

### Task 2: Convert NestEngine to DefaultNestEngine

**Files:**
- Rename: `OpenNest.Engine/NestEngine.cs` → `OpenNest.Engine/DefaultNestEngine.cs`

Rename the class, make it inherit `NestEngineBase`, add `Name`/`Description`, change the virtual methods to `override`, and remove methods that now live in the base class (convenience overloads, `ReportProgress`, `BuildProgressSummary`, `IsBetterFill`, `IsBetterValidFill`, `HasOverlaps`, `FormatPhaseName`, `FillExact`).

- [ ] **Step 1: Rename the file**

```bash
git mv OpenNest.Engine/NestEngine.cs OpenNest.Engine/DefaultNestEngine.cs
```

- [ ] **Step 2: Update class declaration and add inheritance**

In `DefaultNestEngine.cs`, change the class declaration from:

```csharp
    public class NestEngine
    {
        public NestEngine(Plate plate)
        {
            Plate = plate;
        }

        public Plate Plate { get; set; }

        public NestDirection NestDirection { get; set; }

        public int PlateNumber { get; set; }

        public NestPhase WinnerPhase { get; private set; }

        public List<PhaseResult> PhaseResults { get; } = new();

        public bool ForceFullAngleSweep { get; set; }

        public List<AngleResult> AngleResults { get; } = new();
```

To:

```csharp
    public class DefaultNestEngine : NestEngineBase
    {
        public DefaultNestEngine(Plate plate) : base(plate)
        {
        }

        public override string Name => "Default";

        public override string Description => "Multi-phase nesting (Linear, Pairs, RectBestFit, Remainder)";

        public bool ForceFullAngleSweep { get; set; }
```

This removes properties that now come from the base class (`Plate`, `PlateNumber`, `NestDirection`, `WinnerPhase`, `PhaseResults`, `AngleResults`).

- [ ] **Step 3: Convert the convenience Fill overloads to override the virtual methods**

Remove the non-progress `Fill` convenience overloads (they are now in the base class). The two remaining `Fill` methods that take `IProgress<NestProgress>` and `CancellationToken` become overrides.

Change:
```csharp
        public List<Part> Fill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
```
To:
```csharp
        public override List<Part> Fill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
```

Change:
```csharp
        public List<Part> Fill(List<Part> groupParts, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
```
To:
```csharp
        public override List<Part> Fill(List<Part> groupParts, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
```

Remove these methods entirely (now in base class):
- `bool Fill(NestItem item)` (2-arg convenience)
- `bool Fill(NestItem item, Box workArea)` (convenience that calls the 4-arg)
- `bool Fill(List<Part> groupParts)` (convenience)
- `bool Fill(List<Part> groupParts, Box workArea)` (convenience that calls the 4-arg)
- `FillExact` (now in base class)
- `ReportProgress` (now in base class)
- `BuildProgressSummary` (now in base class)
- `IsBetterFill` (now in base class)
- `IsBetterValidFill` (now in base class)
- `HasOverlaps` (now in base class)
- `FormatPhaseName` (now in base class)

- [ ] **Step 4: Convert Pack/PackArea to override**

Remove `Pack(List<NestItem>)` (now in base class).

Convert `PackArea` to override with the new signature. Replace:

```csharp
        public bool Pack(List<NestItem> items)
        {
            var workArea = Plate.WorkArea();
            return PackArea(workArea, items);
        }

        public bool PackArea(Box box, List<NestItem> items)
        {
            var binItems = BinConverter.ToItems(items, Plate.PartSpacing, Plate.Area());
            var bin = BinConverter.CreateBin(box, Plate.PartSpacing);

            var engine = new PackBottomLeft(bin);
            engine.Pack(binItems);

            var parts = BinConverter.ToParts(bin, items);
            Plate.Parts.AddRange(parts);

            return parts.Count > 0;
        }
```

With:

```csharp
        public override List<Part> PackArea(Box box, List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var binItems = BinConverter.ToItems(items, Plate.PartSpacing, Plate.Area());
            var bin = BinConverter.CreateBin(box, Plate.PartSpacing);

            var engine = new PackBottomLeft(bin);
            engine.Pack(binItems);

            return BinConverter.ToParts(bin, items);
        }
```

Note: the `progress` and `token` parameters are not used yet in the default rectangle packing — the contract is there for engines that need them.

- [ ] **Step 5: Update BruteForceRunner to use DefaultNestEngine**

`BruteForceRunner.cs` is in the same project and still references `NestEngine`. It must be updated before the Engine project can compile. This is the one callsite that stays as a direct `DefaultNestEngine` reference (not via registry) because training data must come from the known algorithm.

In `OpenNest.Engine/ML/BruteForceRunner.cs`, change line 30:

```csharp
            var engine = new NestEngine(plate);
```

To:

```csharp
            var engine = new DefaultNestEngine(plate);
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded (other projects will have errors since their callsites still reference `NestEngine` — fixed in Chunk 3)

- [ ] **Step 7: Commit**

```bash
git add OpenNest.Engine/DefaultNestEngine.cs OpenNest.Engine/ML/BruteForceRunner.cs
git commit -m "refactor: rename NestEngine to DefaultNestEngine, inherit NestEngineBase"
```

---

## Chunk 2: NestEngineRegistry and NestEngineInfo

### Task 3: Create NestEngineInfo

**Files:**
- Create: `OpenNest.Engine/NestEngineInfo.cs`

- [ ] **Step 1: Create NestEngineInfo.cs**

```csharp
using System;

namespace OpenNest
{
    public class NestEngineInfo
    {
        public NestEngineInfo(string name, string description, Func<Plate, NestEngineBase> factory)
        {
            Name = name;
            Description = description;
            Factory = factory;
        }

        public string Name { get; }
        public string Description { get; }
        public Func<Plate, NestEngineBase> Factory { get; }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngineInfo.cs
git commit -m "feat: add NestEngineInfo metadata class"
```

---

### Task 4: Create NestEngineRegistry

**Files:**
- Create: `OpenNest.Engine/NestEngineRegistry.cs`

Static class with built-in registration, plugin loading, active engine selection, and factory creation.

- [ ] **Step 1: Create NestEngineRegistry.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenNest
{
    public static class NestEngineRegistry
    {
        private static readonly List<NestEngineInfo> engines = new();

        static NestEngineRegistry()
        {
            Register("Default",
                "Multi-phase nesting (Linear, Pairs, RectBestFit, Remainder)",
                plate => new DefaultNestEngine(plate));
        }

        public static IReadOnlyList<NestEngineInfo> AvailableEngines => engines;

        public static string ActiveEngineName { get; set; } = "Default";

        public static NestEngineBase Create(Plate plate)
        {
            var info = engines.FirstOrDefault(e =>
                e.Name.Equals(ActiveEngineName, StringComparison.OrdinalIgnoreCase));

            if (info == null)
            {
                Debug.WriteLine($"[NestEngineRegistry] Engine '{ActiveEngineName}' not found, falling back to Default");
                info = engines[0];
            }

            return info.Factory(plate);
        }

        public static void Register(string name, string description, Func<Plate, NestEngineBase> factory)
        {
            if (engines.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.WriteLine($"[NestEngineRegistry] Duplicate engine '{name}' skipped");
                return;
            }

            engines.Add(new NestEngineInfo(name, description, factory));
        }

        public static void LoadPlugins(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var dll in Directory.GetFiles(directory, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);

                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsAbstract || !typeof(NestEngineBase).IsAssignableFrom(type))
                            continue;

                        var ctor = type.GetConstructor(new[] { typeof(Plate) });

                        if (ctor == null)
                        {
                            Debug.WriteLine($"[NestEngineRegistry] Skipping {type.Name}: no Plate constructor");
                            continue;
                        }

                        // Create a temporary instance to read Name and Description.
                        try
                        {
                            var tempPlate = new Plate();
                            var instance = (NestEngineBase)ctor.Invoke(new object[] { tempPlate });
                            Register(instance.Name, instance.Description,
                                plate => (NestEngineBase)ctor.Invoke(new object[] { plate }));
                            Debug.WriteLine($"[NestEngineRegistry] Loaded plugin engine: {instance.Name}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NestEngineRegistry] Failed to instantiate {type.Name}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NestEngineRegistry] Failed to load assembly {Path.GetFileName(dll)}: {ex.Message}");
                }
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngineRegistry.cs
git commit -m "feat: add NestEngineRegistry with built-in registration and plugin loading"
```

---

## Chunk 3: Callsite Migration

### Task 5: Migrate OpenNest.Mcp callsites

**Files:**
- Modify: `OpenNest.Mcp/Tools/NestingTools.cs`

Six `new NestEngine(plate)` calls become `NestEngineRegistry.Create(plate)`. The `PackArea` call on line 276 changes signature since `PackArea` now returns `List<Part>` instead of mutating the plate.

- [ ] **Step 1: Replace all NestEngine instantiations**

In `NestingTools.cs`, replace all six occurrences of `new NestEngine(plate)` with `NestEngineRegistry.Create(plate)`.

Lines to change:
- Line 37: `var engine = new NestEngine(plate);` → `var engine = NestEngineRegistry.Create(plate);`
- Line 73: `var engine = new NestEngine(plate);` → `var engine = NestEngineRegistry.Create(plate);`
- Line 114: `var engine = new NestEngine(plate);` → `var engine = NestEngineRegistry.Create(plate);`
- Line 176: `var engine = new NestEngine(plate);` → `var engine = NestEngineRegistry.Create(plate);`
- Line 255: `var engine = new NestEngine(plate);` → `var engine = NestEngineRegistry.Create(plate);`
- Line 275: `var engine = new NestEngine(plate);` → `var engine = NestEngineRegistry.Create(plate);`

- [ ] **Step 2: Fix PackArea call in AutoNestPlate**

The old code on line 276 was:
```csharp
                engine.PackArea(workArea, packItems);
```

This used the old `bool PackArea(Box, List<NestItem>)` which mutated the plate. The new virtual method returns `List<Part>`. Use the convenience `Pack`-like pattern instead. Replace lines 274-277:

```csharp
                var before = plate.Parts.Count;
                var engine = new NestEngine(plate);
                engine.PackArea(workArea, packItems);
                totalPlaced += plate.Parts.Count - before;
```

With:

```csharp
                var engine = NestEngineRegistry.Create(plate);
                var packParts = engine.PackArea(workArea, packItems, null, CancellationToken.None);
                if (packParts.Count > 0)
                {
                    plate.Parts.AddRange(packParts);
                    totalPlaced += packParts.Count;
                }
```

- [ ] **Step 3: Build OpenNest.Mcp**

Run: `dotnet build OpenNest.Mcp/OpenNest.Mcp.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Mcp/Tools/NestingTools.cs
git commit -m "refactor: migrate NestingTools to NestEngineRegistry"
```

---

### Task 6: Migrate OpenNest.Console callsites

**Files:**
- Modify: `OpenNest.Console/Program.cs`

Three `new NestEngine(plate)` calls. The `PackArea` call also needs the same signature update.

- [ ] **Step 1: Replace NestEngine instantiations**

In `Program.cs`, replace:
- Line 351: `var engine = new NestEngine(plate);` → `var engine = NestEngineRegistry.Create(plate);`
- Line 380: `var engine = new NestEngine(plate);` → `var engine = NestEngineRegistry.Create(plate);`

- [ ] **Step 2: Fix PackArea call**

Replace lines 370-372:

```csharp
                var engine = new NestEngine(plate);
                var before = plate.Parts.Count;
                engine.PackArea(workArea, packItems);
```

With:

```csharp
                var engine = NestEngineRegistry.Create(plate);
                var packParts = engine.PackArea(workArea, packItems, null, CancellationToken.None);
                plate.Parts.AddRange(packParts);
```

And update line 374-375 from:
```csharp
                if (plate.Parts.Count > before)
                    success = true;
```
To:
```csharp
                if (packParts.Count > 0)
                    success = true;
```

- [ ] **Step 3: Build OpenNest.Console**

Run: `dotnet build OpenNest.Console/OpenNest.Console.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Console/Program.cs
git commit -m "refactor: migrate Console Program to NestEngineRegistry"
```

---

### Task 7: Migrate OpenNest WinForms callsites

**Files:**
- Modify: `OpenNest/Actions/ActionFillArea.cs`
- Modify: `OpenNest/Controls/PlateView.cs`
- Modify: `OpenNest/Forms/MainForm.cs`

- [ ] **Step 1: Migrate ActionFillArea.cs**

In `ActionFillArea.cs`, replace both `new NestEngine(plateView.Plate)` calls:
- Line 50: `var engine = new NestEngine(plateView.Plate);` → `var engine = NestEngineRegistry.Create(plateView.Plate);`
- Line 64: `var engine = new NestEngine(plateView.Plate);` → `var engine = NestEngineRegistry.Create(plateView.Plate);`

- [ ] **Step 2: Migrate PlateView.cs**

In `PlateView.cs`, replace:
- Line 836: `var engine = new NestEngine(Plate);` → `var engine = NestEngineRegistry.Create(Plate);`

- [ ] **Step 3: Migrate MainForm.cs**

In `MainForm.cs`, replace all three `new NestEngine(plate)` calls:
- Line 797: `var engine = new NestEngine(plate) { PlateNumber = plateCount };` → `var engine = NestEngineRegistry.Create(plate); engine.PlateNumber = plateCount;`
- Line 829: `var engine = new NestEngine(plate);` → `var engine = NestEngineRegistry.Create(plate);`
- Line 965: `var engine = new NestEngine(plate);` → `var engine = NestEngineRegistry.Create(plate);`

- [ ] **Step 4: Fix MainForm PackArea call**

In `MainForm.cs`, the auto-nest pack phase (around line 829-832) uses the old `PackArea` signature. Replace:

```csharp
                        var engine = new NestEngine(plate);
                        var partsBefore = plate.Parts.Count;
                        engine.PackArea(workArea, packItems);
                        var packed = plate.Parts.Count - partsBefore;
```

With:

```csharp
                        var engine = NestEngineRegistry.Create(plate);
                        var packParts = engine.PackArea(workArea, packItems, null, CancellationToken.None);
                        plate.Parts.AddRange(packParts);
                        var packed = packParts.Count;
```

- [ ] **Step 5: Add plugin loading at startup**

In `MainForm.cs`, find where post-processors are loaded at startup (look for `Posts` directory loading) and add engine plugin loading nearby. Add after the existing plugin loading:

```csharp
            var enginesDir = Path.Combine(Application.StartupPath, "Engines");
            NestEngineRegistry.LoadPlugins(enginesDir);
```

If there is no explicit post-processor loading call visible, add this to the `MainForm` constructor or `Load` event.

- [ ] **Step 6: Build the full solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded with no errors

- [ ] **Step 7: Commit**

```bash
git add OpenNest/Actions/ActionFillArea.cs OpenNest/Controls/PlateView.cs OpenNest/Forms/MainForm.cs
git commit -m "refactor: migrate WinForms callsites to NestEngineRegistry"
```

---

## Chunk 4: Verification and Cleanup

### Task 8: Verify no remaining NestEngine references

**Files:**
- No changes expected — verification only

- [ ] **Step 1: Search for stale references**

Run: `grep -rn "new NestEngine(" --include="*.cs" .`
Expected: Only `BruteForceRunner.cs` should have `new DefaultNestEngine(`. No `new NestEngine(` references should remain.

Also run: `grep -rn "class NestEngine[^B]" --include="*.cs" .`
Expected: No matches (the old `class NestEngine` no longer exists).

- [ ] **Step 2: Build and run smoke test**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded, 0 errors, 0 warnings related to NestEngine

- [ ] **Step 3: Publish MCP server**

Run: `dotnet publish OpenNest.Mcp/OpenNest.Mcp.csproj -c Release -o "$USERPROFILE/.claude/mcp/OpenNest.Mcp"`
Expected: Publish succeeded

- [ ] **Step 4: Commit if any fixes were needed**

If any issues were found and fixed in previous steps, commit them now.

---

### Task 9: Update CLAUDE.md architecture documentation

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update architecture section**

Update the `### OpenNest.Engine` section in `CLAUDE.md` to document the new engine hierarchy:
- `NestEngineBase` is the abstract base class
- `DefaultNestEngine` is the current multi-phase engine (formerly `NestEngine`)
- `NestEngineRegistry` manages available engines and the active selection
- `NestEngineInfo` holds engine metadata
- Plugin engines loaded from `Engines/` directory

Also update any references to `NestEngine` that should now say `DefaultNestEngine` or `NestEngineBase`.

- [ ] **Step 2: Build to verify no docs broke anything**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for abstract nest engine architecture"
```
