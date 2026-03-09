# Remnant Fill Optimization — Investigation & Fix

## Status: Both fixes done

## Problem 1 (FIXED): N0308-008 hinge plate remnant

`NestEngine.Fill(NestItem, Box)` got 7 parts in a 4.7x35.0 remnant strip where manual nesting gets 8 using a staggered brick pattern with alternating rotations.

### Test Case
```
load_nest("C:/Users/AJ/Desktop/N0308-008.zip")
fill_remnants(0, "Converto 3 YRD DUMPERSTER HINGE PLATE #2")  → was 7, now 9
```
Reference: `C:/Users/AJ/Desktop/N0308-008 - Copy.zip` (83 parts total, 8 in remnant).

### Root Cause Found
- FillLinear rotation sweep works correctly — tested at 1° resolution, max is always 7
- The reference uses a **staggered pair pattern** (alternating 90°/270° rotations with horizontal offset)
- `FillWithPairs` generates ~2572 pair candidates but only tried top 50 sorted by minimum bounding area
- The winning pair ranked ~882nd — excluded by the `Take(50)` cutoff
- Top-50-by-area favors compact pairs for full-plate tiling, not narrow pairs suited for remnant strips

### Fix Applied (in `OpenNest.Engine/NestEngine.cs`)
Added `SelectPairCandidates()` method:
1. Always includes standard top 50 pairs by area (no change for full-plate fills)
2. When work area is narrow (`shortSide < plateShortSide * 0.5`), includes **all** pairs whose shortest side fits the strip width
3. Updated `FillWithPairs()` to call `SelectPairCandidates()` instead of `Take(50)`

### Results
- Remnant fill: 7 → **9 parts** (beats reference of 8, with partial pattern fill)
- Full-plate fill: 75 parts (unchanged, no regression)
- Remnant fill time: ~440ms
- Overlap check: PASS

---

## Problem 2 (FIXED): N0308-017 PT02 remnant

`N0308-017.zip` — 54 parts on a 144x72 plate. Two remnant areas:
- Remnant 0: `(119.57, 0.75) 24.13x70.95` — end-of-sheet strip
- Remnant 1: `(0.30, 66.15) 143.40x5.55` — bottom strip

Drawing "4980 A24 PT02" has bbox 10.562x15.406. Engine filled 8 parts (2 cols × 4 rows) in remnant 0. Reference (`N0308-017 - Copy.zip`) has 10 parts using alternating 0°/180° rows.

### Investigation
1. Tested PT02 in remnant isolation → still 8 parts (not a multi-drawing ordering issue)
2. Brute-forced all 7224 pair candidates → max was 8 (no pair yields >8 with full-pattern-only tiling)
3. Tried finer offset resolution (0.05" step) across 0°/90°/180°/270° → still max 8
4. Analyzed reference nest (`N0308-017 - Copy.zip`): **64 PT02 parts on full plate, 10 in remnant area**

### Root Cause Found
The reference uses a 0°/170° staggered pair pattern that tiles in 5 rows × 2 columns:
- Rows at y: 0.75, 14.88, 28.40, 42.53, 56.06 (alternating 0° and 170°)
- Pattern copy distance: ~27.65" (pair tiling distance)
- 2 full pairs = 8 parts, top at ~58.56"
- Remaining height: 71.70 - 58.56 = ~13.14" — enough for 1 more row of 0° parts (height 15.41)
- **But `FillLinear.TilePattern` only placed complete pattern copies**, so the partial 3rd pair (just the 0° row) was never attempted

The pair candidate DID exist in the candidate set and was being tried. The issue was entirely in `FillLinear.TilePattern` — it tiled 2 complete pairs (8 parts) and stopped, even though 2 more individual parts from the next incomplete pair would still fit within the work area.

### Fix Applied (in `OpenNest.Engine/FillLinear.cs`)
Added **partial pattern fill** to `TilePattern()`:
- After tiling complete pattern copies, if the pattern has multiple parts, clone the next would-be copy
- Check each individual part's bounding box against the work area
- Add any that fit — guaranteed no overlaps by the copy distance computation

This is safe because:
- The copy distance ensures no overlaps between adjacent full copies → partial (subset) is also safe
- Parts within the same pattern copy don't overlap by construction
- Individual bounds checking catches parts that exceed the work area

### Results
- PT02 remnant fill: 8 → **10 parts** (matches reference)
- Hinge remnant fill: 8 → **9 parts** (bonus improvement from same fix)
- Full-plate fill: 75 parts (unchanged, no regression)
- All overlap checks: PASS
- PT02 fill time: ~32s (unchanged, dominated by pair candidate evaluation)

---

## Files Modified
- `OpenNest.Engine/NestEngine.cs` — Added `SelectPairCandidates()`, updated `FillWithPairs()`, rotation sweep (pre-existing change)
- `OpenNest.Engine/FillLinear.cs` — Added partial pattern fill to `TilePattern()`

## Temp Files to Clean Up
- `OpenNest.Test/` — temporary test console project (can be deleted or kept for debugging)
