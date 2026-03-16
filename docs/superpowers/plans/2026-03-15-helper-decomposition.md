# Helper Class Decomposition

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Break the 1,464-line `Helper` catch-all class into focused, single-responsibility static classes.

**Architecture:** Extract six logical groups from `Helper` into dedicated classes. Each extraction creates a new file, moves methods, updates all call sites, and verifies with `dotnet build`. The original `Helper.cs` is deleted once empty. No behavioral changes — pure mechanical refactoring.

**Tech Stack:** .NET 8, C# 12

---

## File Structure

| New File | Namespace | Responsibility | Methods Moved |
|----------|-----------|----------------|---------------|
| `OpenNest.Core/Math/Rounding.cs` | `OpenNest.Math` | Factor-based rounding | `RoundDownToNearest`, `RoundUpToNearest`, `RoundToNearest` |
| `OpenNest.Core/Geometry/GeometryOptimizer.cs` | `OpenNest.Geometry` | Merge collinear lines / coradial arcs | `Optimize(arcs)`, `Optimize(lines)`, `TryJoinLines`, `TryJoinArcs`, `GetCollinearLines`, `GetCoradialArs` |
| `OpenNest.Core/Geometry/ShapeBuilder.cs` | `OpenNest.Geometry` | Chain entities into shapes | `GetShapes`, `GetConnected` |
| `OpenNest.Core/Geometry/Intersect.cs` | `OpenNest.Geometry` | All intersection algorithms | 16 `Intersects` overloads |
| `OpenNest.Core/PartGeometry.cs` | `OpenNest` | Convert Parts to line geometry | `GetPartLines` (×2), `GetOffsetPartLines` (×2), `GetDirectionalLines` |
| `OpenNest.Core/Geometry/SpatialQuery.cs` | `OpenNest.Geometry` | Directional distance, ray casting, box queries | `RayEdgeDistance` (×2), `DirectionalDistance` (×3), `FlattenLines`, `OneWayDistance`, `OppositeDirection`, `IsHorizontalDirection`, `EdgeDistance`, `DirectionToOffset`, `DirectionalGap`, `ClosestDistance*` (×4), `GetLargestBox*` (×2) |

**Files modified (call-site updates):**

| File | Methods Referenced |
|------|--------------------|
| `OpenNest.Core/Plate.cs` | `RoundUpToNearest` → `Rounding.RoundUpToNearest` |
| `OpenNest.IO/DxfImporter.cs` | `Optimize` → `GeometryOptimizer.Optimize` |
| `OpenNest.Core/Geometry/Shape.cs` | `Optimize` → `GeometryOptimizer.Optimize`, `Intersects` → `Intersect.Intersects` |
| `OpenNest.Core/Drawing.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest.Core/Timing.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest.Core/Converters/ConvertGeometry.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest.Core/Geometry/ShapeProfile.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest.Core/Geometry/Arc.cs` | `Intersects` → `Intersect.Intersects` |
| `OpenNest.Core/Geometry/Circle.cs` | `Intersects` → `Intersect.Intersects` |
| `OpenNest.Core/Geometry/Line.cs` | `Intersects` → `Intersect.Intersects` |
| `OpenNest.Core/Geometry/Polygon.cs` | `Intersects` → `Intersect.Intersects` |
| `OpenNest/LayoutPart.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest/Actions/ActionSetSequence.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest/Actions/ActionSelectArea.cs` | `GetLargestBox*` → `SpatialQuery.GetLargestBox*` |
| `OpenNest/Actions/ActionClone.cs` | `GetLargestBox*` → `SpatialQuery.GetLargestBox*` |
| `OpenNest.Gpu/PartBitmap.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest.Gpu/GpuPairEvaluator.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest.Engine/RotationAnalysis.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest.Engine/BestFit/BestFitFinder.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest.Engine/BestFit/PairEvaluator.cs` | `GetShapes` → `ShapeBuilder.GetShapes` |
| `OpenNest.Engine/FillLinear.cs` | `DirectionalDistance`, `OppositeDirection` → `SpatialQuery.*` |
| `OpenNest.Engine/Compactor.cs` | Multiple `Helper.*` → `SpatialQuery.*` + `PartGeometry.*` |
| `OpenNest.Engine/BestFit/RotationSlideStrategy.cs` | Multiple `Helper.*` → `SpatialQuery.*` + `PartGeometry.*` |

---

## Chunk 1: Rounding + GeometryOptimizer + ShapeBuilder

### Task 1: Extract Rounding to OpenNest.Math

**Files:**
- Create: `OpenNest.Core/Math/Rounding.cs`
- Modify: `OpenNest.Core/Plate.cs:415-416`
- Delete from: `OpenNest.Core/Helper.cs` (lines 14–45)

- [ ] **Step 1: Create `Rounding.cs`**

```csharp
using OpenNest.Math;

namespace OpenNest.Math
{
    public static class Rounding
    {
        public static double RoundDownToNearest(double num, double factor)
        {
            return factor.IsEqualTo(0) ? num : System.Math.Floor(num / factor) * factor;
        }

        public static double RoundUpToNearest(double num, double factor)
        {
            return factor.IsEqualTo(0) ? num : System.Math.Ceiling(num / factor) * factor;
        }

        public static double RoundToNearest(double num, double factor)
        {
            return factor.IsEqualTo(0) ? num : System.Math.Round(num / factor) * factor;
        }
    }
}
```

- [ ] **Step 2: Update call site in `Plate.cs`**

Replace `Helper.RoundUpToNearest` with `Rounding.RoundUpToNearest`. Add `using OpenNest.Math;` if not present.

- [ ] **Step 3: Remove three rounding methods from `Helper.cs`**

Delete lines 14–45 (the three methods and their XML doc comments).

- [ ] **Step 4: Build and verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```
refactor: extract Rounding from Helper to OpenNest.Math
```

---

### Task 2: Extract GeometryOptimizer

**Files:**
- Create: `OpenNest.Core/Geometry/GeometryOptimizer.cs`
- Modify: `OpenNest.IO/DxfImporter.cs:59-60`, `OpenNest.Core/Geometry/Shape.cs:162-163`
- Delete from: `OpenNest.Core/Helper.cs` (lines 47–237)

- [ ] **Step 1: Create `GeometryOptimizer.cs`**

Move these 6 methods (preserving exact code):
- `Optimize(IList<Arc>)`
- `Optimize(IList<Line>)`
- `TryJoinLines`
- `TryJoinArcs`
- `GetCollinearLines` (private extension method)
- `GetCoradialArs` (private extension method)

Namespace: `OpenNest.Geometry`. Class: `public static class GeometryOptimizer`.

Required usings: `System`, `System.Collections.Generic`, `System.Threading.Tasks`, `OpenNest.Math`.

- [ ] **Step 2: Update call sites**

- `DxfImporter.cs`: `Helper.Optimize(...)` → `GeometryOptimizer.Optimize(...)`. Add `using OpenNest.Geometry;`.
- `Shape.cs`: `Helper.Optimize(...)` → `GeometryOptimizer.Optimize(...)`. Already in `OpenNest.Geometry` namespace — no using needed.

- [ ] **Step 3: Remove methods from `Helper.cs`**

- [ ] **Step 4: Build and verify**

Run: `dotnet build OpenNest.sln`

- [ ] **Step 5: Commit**

```
refactor: extract GeometryOptimizer from Helper
```

---

### Task 3: Extract ShapeBuilder

**Files:**
- Create: `OpenNest.Core/Geometry/ShapeBuilder.cs`
- Modify: 11 files (see call-site table above for `GetShapes` callers)
- Delete from: `OpenNest.Core/Helper.cs` (lines 239–378)

- [ ] **Step 1: Create `ShapeBuilder.cs`**

Move these 2 methods:
- `GetShapes(IEnumerable<Entity>)` — public
- `GetConnected(Vector, IEnumerable<Entity>)` — internal

Namespace: `OpenNest.Geometry`. Class: `public static class ShapeBuilder`.

Required usings: `System.Collections.Generic`, `System.Diagnostics`, `OpenNest.Math`.

- [ ] **Step 2: Update all call sites**

Replace `Helper.GetShapes` → `ShapeBuilder.GetShapes` in every file. Add `using OpenNest.Geometry;` where the file isn't already in that namespace.

Files to update:
- `OpenNest.Core/Drawing.cs`
- `OpenNest.Core/Timing.cs`
- `OpenNest.Core/Converters/ConvertGeometry.cs`
- `OpenNest.Core/Geometry/ShapeProfile.cs` (already in namespace)
- `OpenNest/LayoutPart.cs`
- `OpenNest/Actions/ActionSetSequence.cs`
- `OpenNest.Gpu/PartBitmap.cs`
- `OpenNest.Gpu/GpuPairEvaluator.cs`
- `OpenNest.Engine/RotationAnalysis.cs`
- `OpenNest.Engine/BestFit/BestFitFinder.cs`
- `OpenNest.Engine/BestFit/PairEvaluator.cs`

- [ ] **Step 3: Remove methods from `Helper.cs`**

- [ ] **Step 4: Build and verify**

Run: `dotnet build OpenNest.sln`

- [ ] **Step 5: Commit**

```
refactor: extract ShapeBuilder from Helper
```

---

## Chunk 2: Intersect + PartGeometry

### Task 4: Extract Intersect

**Files:**
- Create: `OpenNest.Core/Geometry/Intersect.cs`
- Modify: `Arc.cs`, `Circle.cs`, `Line.cs`, `Shape.cs`, `Polygon.cs` (all in `OpenNest.Core/Geometry/`)
- Delete from: `OpenNest.Core/Helper.cs` (lines 380–742)

- [ ] **Step 1: Create `Intersect.cs`**

Move all 16 `Intersects` overloads. Namespace: `OpenNest.Geometry`. Class: `public static class Intersect`.

All methods keep their existing access modifiers (`internal` for most, none are `public`).

Required usings: `System.Collections.Generic`, `System.Linq`, `OpenNest.Math`.

- [ ] **Step 2: Update call sites in geometry types**

All callers are in the same namespace (`OpenNest.Geometry`) so no using changes needed. Replace `Helper.Intersects` → `Intersect.Intersects` in:
- `Arc.cs` (10 calls)
- `Circle.cs` (10 calls)
- `Line.cs` (8 calls)
- `Shape.cs` (12 calls, including the internal offset usage at line 537)
- `Polygon.cs` (10 calls)

- [ ] **Step 3: Remove methods from `Helper.cs`**

- [ ] **Step 4: Build and verify**

Run: `dotnet build OpenNest.sln`

- [ ] **Step 5: Commit**

```
refactor: extract Intersect from Helper
```

---

### Task 5: Extract PartGeometry

**Files:**
- Create: `OpenNest.Core/PartGeometry.cs`
- Modify: `OpenNest.Engine/Compactor.cs`, `OpenNest.Engine/BestFit/RotationSlideStrategy.cs`
- Delete from: `OpenNest.Core/Helper.cs` (lines 744–858)

- [ ] **Step 1: Create `PartGeometry.cs`**

Move these 5 methods:
- `GetPartLines(Part, double)` — public
- `GetPartLines(Part, PushDirection, double)` — public
- `GetOffsetPartLines(Part, double, double)` — public
- `GetOffsetPartLines(Part, double, PushDirection, double)` — public
- `GetDirectionalLines(Polygon, PushDirection)` — private

Namespace: `OpenNest`. Class: `public static class PartGeometry`.

Required usings: `System.Collections.Generic`, `System.Linq`, `OpenNest.Converters`, `OpenNest.Geometry`.

- [ ] **Step 2: Update call sites**

- `Compactor.cs`: `Helper.GetOffsetPartLines` / `Helper.GetPartLines` → `PartGeometry.*`
- `RotationSlideStrategy.cs`: `Helper.GetOffsetPartLines` → `PartGeometry.GetOffsetPartLines`

- [ ] **Step 3: Remove methods from `Helper.cs`**

- [ ] **Step 4: Build and verify**

Run: `dotnet build OpenNest.sln`

- [ ] **Step 5: Commit**

```
refactor: extract PartGeometry from Helper
```

---

## Chunk 3: SpatialQuery + Cleanup

### Task 6: Extract SpatialQuery

**Files:**
- Create: `OpenNest.Core/Geometry/SpatialQuery.cs`
- Modify: `Compactor.cs`, `FillLinear.cs`, `RotationSlideStrategy.cs`, `ActionClone.cs`, `ActionSelectArea.cs`
- Delete from: `OpenNest.Core/Helper.cs` (lines 860–1462, all remaining methods)

- [ ] **Step 1: Create `SpatialQuery.cs`**

Move all remaining methods (14 total):
- `RayEdgeDistance(Vector, Line, PushDirection)` — private
- `RayEdgeDistance(double, double, double, double, double, double, PushDirection)` — private, `[AggressiveInlining]`
- `DirectionalDistance(List<Line>, List<Line>, PushDirection)` — public
- `DirectionalDistance(List<Line>, double, double, List<Line>, PushDirection)` — public
- `DirectionalDistance((Vector,Vector)[], Vector, (Vector,Vector)[], Vector, PushDirection)` — public
- `FlattenLines(List<Line>)` — public
- `OneWayDistance(Vector, (Vector,Vector)[], Vector, PushDirection)` — public
- `OppositeDirection(PushDirection)` — public
- `IsHorizontalDirection(PushDirection)` — public
- `EdgeDistance(Box, Box, PushDirection)` — public
- `DirectionToOffset(PushDirection, double)` — public
- `DirectionalGap(Box, Box, PushDirection)` — public
- `ClosestDistanceLeft/Right/Up/Down` — public (4 methods)
- `GetLargestBoxVertically/Horizontally` — public (2 methods)

Namespace: `OpenNest.Geometry`. Class: `public static class SpatialQuery`.

Required usings: `System`, `System.Collections.Generic`, `System.Linq`, `OpenNest.Math`.

- [ ] **Step 2: Update call sites**

Replace `Helper.*` → `SpatialQuery.*` and add `using OpenNest.Geometry;` where needed:
- `OpenNest.Engine/Compactor.cs` — `OppositeDirection`, `IsHorizontalDirection`, `EdgeDistance`, `DirectionalGap`, `DirectionalDistance`, `DirectionToOffset`
- `OpenNest.Engine/FillLinear.cs` — `DirectionalDistance`, `OppositeDirection`
- `OpenNest.Engine/BestFit/RotationSlideStrategy.cs` — `FlattenLines`, `OppositeDirection`, `OneWayDistance`
- `OpenNest/Actions/ActionClone.cs` — `GetLargestBoxVertically`, `GetLargestBoxHorizontally`
- `OpenNest/Actions/ActionSelectArea.cs` — `GetLargestBoxHorizontally`, `GetLargestBoxVertically`

- [ ] **Step 3: Remove methods from `Helper.cs`**

At this point `Helper.cs` should be empty (just the class wrapper and usings).

- [ ] **Step 4: Build and verify**

Run: `dotnet build OpenNest.sln`

- [ ] **Step 5: Commit**

```
refactor: extract SpatialQuery from Helper
```

---

### Task 7: Delete Helper.cs

**Files:**
- Delete: `OpenNest.Core/Helper.cs`

- [ ] **Step 1: Delete the empty `Helper.cs` file**

- [ ] **Step 2: Build and verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded with zero errors

- [ ] **Step 3: Commit**

```
refactor: remove empty Helper class
```
