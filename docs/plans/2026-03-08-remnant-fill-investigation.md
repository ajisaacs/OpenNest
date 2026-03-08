# Remnant Fill Optimization Investigation

## Problem

When filling remnant strips on a partially-nested plate, `NestEngine.Fill(NestItem, Box)` produces fewer parts than the UI's Ctrl+F fill. On a test case (N0308-008.zip, 36x36 plate, "Converto 3 YRD DUMPERSTER HINGE PLATE #2" 5.89x3.39):

- **MCP fill_remnants**: 7 parts in the right-side strip
- **UI Ctrl+F (ActionClone.Fill)**: 9 parts in the same strip

## Test Setup

Load `C:/Users/AJ/Desktop/N0308-008.zip` — 75 parts on plate 0, 5 columns of 15. The remnant strip is at `(31.0, 0.8) 4.7x35.0`.

Use the OpenNest MCP tools to reproduce:
```
load_nest("C:/Users/AJ/Desktop/N0308-008.zip")
get_plate_info(0)  → should show 75 parts, remnant at (31.0, 0.8) 4.7x35.0
fill_remnants(0, "Converto 3 YRD DUMPERSTER HINGE PLATE #2")  → gets 7 parts
```

## Root Cause Analysis

The MCP's `fill_remnants` calls `NestEngine.Fill(NestItem, Box)` which:

1. Tries `FillLinear` with best rotation + 90° (and angle sweep if strip is narrow)
2. Tries `FillRectangleBestFit` (mixes horizontal/vertical in bin packing)
3. Tries `FillWithPairs` (paired part combinations via `BestFitCache`)
4. Picks the best result

The UI's `ActionClone.Fill()` (`OpenNest/Actions/ActionClone.cs:171-201`) does something different:

1. Gets `Helper.GetLargestBoxVertically(pt, bounds, boxes)` and `GetLargestBoxHorizontally(pt, bounds, boxes)` from the cursor position
2. Picks the largest area
3. Calls `NestEngine.Fill(groupParts, bestArea)` — note: passes `List<Part>` not `NestItem`

The `Fill(List<Part>, Box)` overload uses `RotationAnalysis.FindHullEdgeAngles(groupParts)` which may produce different/better rotation candidates than `Fill(NestItem, Box)` which uses `RotationAnalysis.FindBestRotation(item)`.

## Key Differences to Investigate

### 1. Rotation candidate generation
- `Fill(NestItem, Box)` uses `FindBestRotation` → best angle + 90° + optional sweep
- `Fill(List<Part>, Box)` uses `FindHullEdgeAngles` → edge angles from convex hull
- The hull edge angles may include rotation angles that pack better in the narrow strip

### 2. Region selection
- UI uses cursor position to find the largest obstacle-free rectangle via `GetLargestBoxVertically`/`GetLargestBoxHorizontally`
- MCP uses `Plate.GetRemnants()` which returns edge strips from global part boundaries
- The UI region may differ in exact bounds from the remnant strip

### 3. Part grouping
- UI's `ActionClone` can pass multi-part groups to `Fill(List<Part>, Box)`, enabling pattern-based tiling
- MCP passes single `NestItem` to `Fill(NestItem, Box)`

## Files to Read

- `OpenNest.Engine/NestEngine.cs` — both `Fill` overloads, `FillWithPairs`, `FillPattern`
- `OpenNest/Actions/ActionClone.cs:171-201` — the UI fill path
- `OpenNest.Engine/BestFit/RotationAnalysis.cs` — `FindBestRotation` vs `FindHullEdgeAngles`
- `OpenNest.Engine/FillLinear.cs` — the linear tiling engine
- `OpenNest.Core/Helper.cs:1098+` — `GetLargestBoxVertically`/`GetLargestBoxHorizontally`

## Possible Fixes

1. **Use hull edge angles in the NestItem overload** — merge rotation candidates from both `FindBestRotation` and `FindHullEdgeAngles`
2. **Improve GetRemnants** — instead of global edge strips, scan per-column to find the actual free space shape
3. **Add a smarter fill_remnants** — have the MCP tool use `GetLargestBox*` helpers to find free rectangles from multiple scan points, similar to how the UI does it
