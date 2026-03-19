# Nest API Design

## Overview

A new `OpenNest.Api` project providing a clean programmatic facade for nesting operations. A single `NestRequest` goes in, a self-contained `NestResponse` comes out. Designed for external callers (MCP server, console app, LaserQuote, future web API) that don't want to manually wire engine + timing + IO.

## Motivation

Today, running a nest programmatically requires manually coordinating:
1. DXF import (IO layer)
2. Plate/NestItem setup (Core)
3. Engine selection and execution (Engine layer)
4. Timing calculation (Core's `Timing` class)
5. File persistence (IO layer)

This design wraps all five steps behind a single stateless call. The response captures the original request, making nests reproducible and re-priceable months later.

## New Project: `OpenNest.Api`

Class library targeting `net8.0-windows`. References Core, Engine, and IO.

```
OpenNest.Api/
  CutParameters.cs
  NestRequest.cs
  NestRequestPart.cs
  NestStrategy.cs
  NestResponse.cs
  NestRunner.cs
```

All types live in the `OpenNest.Api` namespace.

## Types

### CutParameters

Unified timing and quoting parameters. Replaces the existing `OpenNest.CutParameters` in Core.

```csharp
namespace OpenNest.Api;

public class CutParameters
{
    public double Feedrate { get; init; }           // in/min or mm/sec depending on Units
    public double RapidTravelRate { get; init; }    // in/min or mm/sec
    public TimeSpan PierceTime { get; init; }
    public double LeadInLength { get; init; }       // forward-looking: unused until Timing rework
    public string PostProcessor { get; init; }      // forward-looking: unused until Timing rework
    public Units Units { get; init; }

    public static CutParameters Default => new()
    {
        Feedrate = 100,
        RapidTravelRate = 300,
        PierceTime = TimeSpan.FromSeconds(0.5),
        Units = Units.Inches
    };
}
```

`LeadInLength` and `PostProcessor` are included for forward compatibility but will not be wired into `Timing.CalculateTime` until the Timing rework. Implementers should not attempt to use them in the initial implementation.

### NestRequestPart

A part to nest, identified by DXF file path. No `Drawing` reference — keeps the request fully serializable for persistence.

```csharp
namespace OpenNest.Api;

public class NestRequestPart
{
    public string DxfPath { get; init; }
    public int Quantity { get; init; } = 1;
    public bool AllowRotation { get; init; } = true;
    public int Priority { get; init; } = 0;
}
```

### NestStrategy

```csharp
namespace OpenNest.Api;

public enum NestStrategy { Auto }
```

- `Auto` maps to `DefaultNestEngine` (multi-phase fill).
- Additional strategies (`Linear`, `BestFit`, `Pack`) will be added later, driven by ML-based auto-detection of part type during the training work. The intelligence for selecting the best strategy for a given part will live inside `DefaultNestEngine`.

### NestRequest

Immutable input capturing everything needed to run and reproduce a nest.

```csharp
namespace OpenNest.Api;

public class NestRequest
{
    public IReadOnlyList<NestRequestPart> Parts { get; init; } = [];
    public Size SheetSize { get; init; } = new(60, 120);  // OpenNest.Geometry.Size(width, length)
    public string Material { get; init; } = "Steel, A1011 HR";
    public double Thickness { get; init; } = 0.06;
    public double Spacing { get; init; } = 0.1;           // part-to-part spacing; edge spacing defaults to zero
    public NestStrategy Strategy { get; init; } = NestStrategy.Auto;
    public CutParameters Cutting { get; init; } = CutParameters.Default;
}
```

- `Parts` uses `IReadOnlyList<T>` to prevent mutation after construction, preserving reproducibility when the request is stored in the response.
- `Spacing` maps to `Plate.PartSpacing`. `Plate.EdgeSpacing` defaults to zero on all sides.
- `SheetSize` is `OpenNest.Geometry.Size` (not `System.Drawing.Size`).

### NestResponse

Immutable output containing computed metrics, the resulting `Nest`, and the original request for reproducibility.

```csharp
namespace OpenNest.Api;

public class NestResponse
{
    public int SheetCount { get; init; }
    public double Utilization { get; init; }
    public TimeSpan CutTime { get; init; }
    public TimeSpan Elapsed { get; init; }
    public Nest Nest { get; init; }
    public NestRequest Request { get; init; }

    public Task SaveAsync(string path) => ...;
    public static Task<NestResponse> LoadAsync(string path) => ...;
}
```

`SaveAsync`/`LoadAsync` live on the data class for API simplicity — a pragmatic choice over a separate IO helper class.

### NestRunner

Stateless orchestrator. Single public method.

```csharp
namespace OpenNest.Api;

public static class NestRunner
{
    public static async Task<NestResponse> RunAsync(
        NestRequest request,
        IProgress<NestProgress> progress = null,
        CancellationToken token = default)
    {
        // 1. Validate request (non-empty parts list, all DXF paths exist)
        // 2. Import DXFs → Drawings via DxfImporter + ConvertGeometry.ToProgram
        // 3. Create Plate from request.SheetSize / Thickness / Spacing
        // 4. Convert NestRequestParts → NestItems
        // 5. Multi-plate loop:
        //    a. Create engine via NestEngineRegistry
        //    b. Fill plate
        //    c. Deduct placed quantities
        //    d. If remaining quantities > 0, create next plate and repeat
        // 6. Compute TimingInfo → CutTime using request.Cutting (placeholder for Timing rework)
        // 7. Build and return NestResponse (with stopwatch for Elapsed)
    }
}
```

- Static class, no state, no DI. If we need dependency injection later, we add an instance-based overload.
- `Timing.CalculateTime` is called with the new `CutParameters` (placeholder integration — `Timing` will be reworked later).

#### Multi-plate Loop

`NestRunner` handles multi-plate nesting: it fills a plate, deducts placed quantities from the remaining request, creates a new plate, and repeats until all quantities are met or no progress is made (a part doesn't fit on a fresh sheet). This is new logic — `AutoNester` and `NestEngineBase` are single-plate only.

#### Error Handling

- If any DXF file path does not exist or fails to import (empty geometry, conversion failure), `RunAsync` throws `FileNotFoundException` or `InvalidOperationException` with a message identifying the failing file. Fail-fast on first bad DXF — no partial results.
- If cancellation is requested, the method throws `OperationCanceledException` per standard .NET patterns.

## Renames

| Current | New | Reason |
|---------|-----|--------|
| `OpenNest.NestResult` (Engine) | `OpenNest.OptimizationResult` | Frees the "result" name for the public API; this type is engine-internal (sequence/score/iterations) |
| `OpenNest.CutParameters` (Core) | Deleted | Replaced by `OpenNest.Api.CutParameters` |
| `.opnest` file extension | `.nest` | Standardize file extensions |

All references to the renamed types and extensions must be updated across the solution: Engine, Core, IO, MCP, Console, Training, Tests, and WinForms.

The WinForms project gains a reference to `OpenNest.Api` to use the new `CutParameters` type (it already references Core and Engine, so no circular dependency).

## Persistence

### File Extensions

- **`.nest`** — nest files (renamed from `.opnest`)
- **`.nestquote`** — quote files (new)

### `.nestquote` Format

ZIP archive containing:

```
quote.nestquote (ZIP)
├── request.json      ← serialized NestRequest
├── response.json     ← computed metrics (SheetCount, Utilization, CutTime, Elapsed)
└── nest.nest         ← embedded .nest file (existing format, produced by NestWriter)
```

- `NestResponse.SaveAsync(path)` writes this ZIP. The embedded `nest.nest` is written to a `MemoryStream` via `NestWriter`, then added as a ZIP entry alongside the JSON files.
- `NestResponse.LoadAsync(path)` reads it back using `NestReader` for the `.nest` payload and JSON deserialization for the metadata.
- Source DXF files are **not** embedded — they are referenced by path in `request.json`. The actual geometry is captured in the `.nest`. Paths exist so the request can be re-run with different parameters if the DXFs are still available.

## Consumer Integration

### MCP Server

The MCP server can expose a single `nest_and_quote` tool that takes request parameters and calls `NestRunner.RunAsync()` internally, replacing the current multi-tool orchestration for batch nesting workflows.

### Console App

The console app gains a one-liner for batch nesting:

```csharp
var response = await NestRunner.RunAsync(request, progress, token);
await response.SaveAsync(outputPath);
```

### WinForms

The WinForms app continues using the engine directly for its interactive workflow. It gains a reference to `OpenNest.Api` only for the shared `CutParameters` type used by `TimingForm` and `CutParametersForm`.

## Out of Scope

- ML-based auto-strategy detection in `DefaultNestEngine` (future, part of training work)
- `Timing` rework (will happen separately; placeholder integration for now)
- Embedding source DXFs in `.nestquote` files
- Builder pattern for `NestRequest` (C# `init` properties suffice)
- DI/instance-based `NestRunner` (add later if needed)
- Additional `NestStrategy` enum values beyond `Auto` (added with ML work)
