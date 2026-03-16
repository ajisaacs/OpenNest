# Abstract Nest Engine Design Spec

**Date:** 2026-03-15
**Goal:** Create a pluggable nest engine architecture so users can create custom nesting algorithms, switch between engines globally, and load third-party engines as plugins.

---

## Motivation

The current `NestEngine` is a concrete class with a sophisticated multi-phase fill strategy (Linear, Pairs, RectBestFit, Remainder). Different part geometries benefit from different algorithms — circles need circle-packing, strip-based layouts work better for mixed-drawing nests, and users may want to experiment with their own approaches. The engine needs to be swappable without changing the UI or other consumers.

## Architecture Overview

```
NestEngineBase (abstract, OpenNest.Engine)
├── DefaultNestEngine (current multi-phase logic)
├── StripNestEngine (strip-based multi-drawing nesting)
├── CircleNestEngine (future, circle-packing)
└── [Plugin engines loaded from DLLs]

NestEngineRegistry (static, OpenNest.Engine)
├── Tracks available engines (built-in + plugins)
├── Manages active engine selection (global)
└── Factory method: Create(Plate) → NestEngineBase
```

## NestEngineBase

Abstract base class in `OpenNest.Engine`. Provides the contract, shared state, and utility methods.

### Properties

| Property | Type | Notes |
|----------|------|-------|
| `Plate` | `Plate` | The plate being nested |
| `PlateNumber` | `int` | For progress reporting |
| `NestDirection` | `NestDirection` | Fill direction preference |
| `WinnerPhase` | `NestPhase` | Which phase produced the best result (private set) |
| `PhaseResults` | `List<PhaseResult>` | Per-phase results for diagnostics |
| `AngleResults` | `List<AngleResult>` | Per-angle results for diagnostics |

### Abstract Members

| Member | Type | Purpose |
|--------|------|---------|
| `Name` | `string` (get) | Display name for UI/registry |
| `Description` | `string` (get) | Human-readable description |

### Virtual Methods (return parts, no side effects)

These are the core methods subclasses override:

```csharp
virtual List<Part> Fill(NestItem item, Box workArea,
    IProgress<NestProgress> progress, CancellationToken token)

virtual List<Part> Fill(List<Part> groupParts, Box workArea,
    IProgress<NestProgress> progress, CancellationToken token)

virtual List<Part> FillExact(NestItem item, Box workArea,
    IProgress<NestProgress> progress, CancellationToken token)

virtual List<Part> PackArea(Box box, List<NestItem> items,
    IProgress<NestProgress> progress, CancellationToken token)
```

Default implementations in the base class delegate to `DefaultNestEngine`'s logic (or return empty lists — see DefaultNestEngine below).

### Convenience Overloads (non-virtual, add parts to plate)

These call the virtual methods and handle plate mutation:

```csharp
bool Fill(NestItem item)
bool Fill(NestItem item, Box workArea)
bool Fill(List<Part> groupParts)
bool Fill(List<Part> groupParts, Box workArea)
bool Pack(List<NestItem> items)
```

Pattern: call the virtual method → if parts returned → add to `Plate.Parts` → return `true`.

### Protected Utilities

Available to all subclasses:

- `ReportProgress(IProgress<NestProgress>, NestPhase, int plateNumber, List<Part>, Box, string)` — clone parts and report
- `BuildProgressSummary()` — format PhaseResults into a status string
- `IsBetterFill(List<Part> candidate, List<Part> current, Box workArea)` — FillScore comparison
- `IsBetterValidFill(List<Part> candidate, List<Part> current, Box workArea)` — with overlap check

## DefaultNestEngine

Rename of the current `NestEngine`. Inherits `NestEngineBase` and overrides all virtual methods with the existing multi-phase logic.

- `Name` → `"Default"`
- `Description` → `"Multi-phase nesting (Linear, Pairs, RectBestFit, Remainder)"`
- All current private methods (`FindBestFill`, `FillWithPairs`, `FillRectangleBestFit`, `FillPattern`, `TryRemainderImprovement`, `BuildCandidateAngles`, etc.) remain as private methods in this class
- No behavioral change — purely structural refactor

## StripNestEngine

The planned `StripNester` (from the strip nester spec) becomes a `NestEngineBase` subclass instead of a standalone class.

- `Name` → `"Strip"`
- `Description` → `"Strip-based nesting for mixed-drawing layouts"`
- Overrides `Fill` for multi-item scenarios with its strip+remnant strategy
- Uses `DefaultNestEngine` internally as a building block for individual strip/remnant fills (composition, not inheritance from Default)

## NestEngineRegistry

Static class in `OpenNest.Engine` managing engine discovery and selection.

### NestEngineInfo

```csharp
class NestEngineInfo
{
    string Name { get; }
    string Description { get; }
    Func<Plate, NestEngineBase> Factory { get; }
}
```

### API

| Member | Purpose |
|--------|---------|
| `List<NestEngineInfo> AvailableEngines` | All registered engines |
| `string ActiveEngineName` | Currently selected engine (defaults to `"Default"`) |
| `NestEngineBase Create(Plate plate)` | Creates instance of active engine |
| `void Register(string name, string description, Func<Plate, NestEngineBase> factory)` | Register a built-in engine |
| `void LoadPlugins(string directory)` | Scan DLLs for NestEngineBase subclasses |

### Built-in Registration

```csharp
Register("Default", "Multi-phase nesting...", plate => new DefaultNestEngine(plate));
Register("Strip", "Strip-based nesting...", plate => new StripNestEngine(plate));
```

### Plugin Discovery

Follows the existing `IPostProcessor` pattern from `Posts/`:
- Scan `Engines/` directory next to the executable for DLLs
- Reflect over types, find concrete subclasses of `NestEngineBase`
- Require a constructor taking `Plate`
- Register each with its `Name` and `Description` properties
- Called at application startup alongside post-processor loading

## Callsite Migration

All `new NestEngine(plate)` calls become `NestEngineRegistry.Create(plate)`:

| Location | Count | Notes |
|----------|-------|-------|
| `MainForm.cs` | 3 | Auto-nest, fill plate, fill area |
| `ActionFillArea.cs` | 2 | |
| `PlateView.cs` | 1 | |
| `NestingTools.cs` (MCP) | 6 | |
| `Program.cs` (Console) | 3 | |
| `BruteForceRunner.cs` | 1 | **Keep as `new DefaultNestEngine(plate)`** — training data must come from the known algorithm |

## UI Integration

- Global engine selector: combobox or menu item bound to `NestEngineRegistry.AvailableEngines`
- Changing selection sets `NestEngineRegistry.ActiveEngineName`
- No per-plate engine state — global setting applies to all subsequent operations
- Plugin directory: `Engines/` next to executable, loaded at startup

## File Summary

| Action | File | Project |
|--------|------|---------|
| Create | `NestEngineBase.cs` | OpenNest.Engine |
| Rename/Modify | `NestEngine.cs` → `DefaultNestEngine.cs` | OpenNest.Engine |
| Create | `NestEngineRegistry.cs` | OpenNest.Engine |
| Create | `NestEngineInfo.cs` | OpenNest.Engine |
| Modify | `StripNester.cs` → `StripNestEngine.cs` | OpenNest.Engine |
| Modify | `MainForm.cs` | OpenNest |
| Modify | `ActionFillArea.cs` | OpenNest |
| Modify | `PlateView.cs` | OpenNest |
| Modify | `NestingTools.cs` | OpenNest.Mcp |
| Modify | `Program.cs` | OpenNest.Console |
