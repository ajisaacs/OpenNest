# Remnant Fill Optimization — Try More Rotations

## Problem

`NestEngine.Fill(NestItem, Box)` gets 7 parts in a narrow remnant strip where manual nesting gets 9 — all at the same rotation. The engine simply isn't trying the rotation angle that fits best.

## Test Case

Load `C:/Users/AJ/Desktop/N0308-008.zip` — 75 parts on a 36x36 plate. Remnant strip is at `(31.0, 0.8) 4.7x35.0`.

```
load_nest("C:/Users/AJ/Desktop/N0308-008.zip")
fill_remnants(0, "Converto 3 YRD DUMPERSTER HINGE PLATE #2")  → gets 7, should get 9
```

## Root Cause

In `NestEngine.Fill(NestItem, Box)` (`OpenNest.Engine/NestEngine.cs:32-105`), the rotation candidates are:

1. `bestRotation` from `RotationAnalysis.FindBestRotation(item)`
2. `bestRotation + 90°`
3. If the strip is narrow relative to the part, a sweep every 5° from 0° to 175°

The narrow-strip sweep triggers when `workAreaShortSide < partLongestSide`. For this strip (4.7" wide, part is 5.89x3.39), the short side is 4.7 and the part's longest side is 5.89, so the sweep **should** trigger. But the winning rotation may still not be found because:

- The sweep only runs on the `bestRotation`-normalized part dimensions
- `FillLinear` may not be optimal for all angles — check if `FillRectangleBestFit` and `FillWithPairs` are also tried at each sweep angle

## Fix

Ensure all three fill strategies (FillLinear, FillRectangleBestFit, FillWithPairs) are tried across the full set of rotation candidates, not just FillLinear.

## Files

- `OpenNest.Engine/NestEngine.cs:32-105` — `Fill(NestItem, Box)` rotation logic
- `OpenNest.Engine/BestFit/RotationAnalysis.cs` — `FindBestRotation`
- `OpenNest.Engine/FillLinear.cs` — linear tiling
