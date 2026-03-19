# Lead Item Rotation for Strip Nesting

## Problem

`StripNestEngine.Nest()` sorts multi-quantity items by priority then area descending, always placing the largest-area drawing first. This fixed ordering can produce suboptimal layouts — a different starting drawing may create a tighter shrink region that leaves more usable remnant space for subsequent items.

## Solution

Try multiple candidate orderings by promoting each of the top N largest drawings to the front of the fill list. Run the full pipeline for each ordering, score the results, and keep the best.

## Candidate Generation

- Take the multi-quantity fill items (already filtered from singles)
- Identify the top `MaxLeadCandidates` (default 3) unique drawings by `Drawing.Area`, deduplicated by `Drawing` reference equality
- If there is only one unique drawing, skip the multi-ordering loop entirely (no-op — only one possible ordering)
- For each candidate drawing, create a reordered copy of the fill list where that drawing's items move to the front, preserving the original relative order for the remaining items
- The default ordering (largest area first) is always the first candidate, so the feature never regresses
- Lead promotion intentionally overrides the existing priority-then-area sort — the purpose is to explore whether a different lead item produces a better overall layout regardless of the default priority ordering

## Scoring

Use `FillScore` semantics for cross-ordering comparison: total placed part count as the primary metric, plate utilization (`sum(part.BaseDrawing.Area) / plate.WorkArea().Area()`) as tiebreaker. This is consistent with how `FillScore` works elsewhere in the codebase (count > density). Keep the first (default) result unless a later candidate is strictly better, so ties preserve the default ordering.

## Execution

- Run each candidate ordering sequentially through the existing pipeline: `IterativeShrinkFiller` → compaction → packing
- No added parallelism — each run already uses `Parallel.Invoke` internally for shrink axes
- `IterativeShrinkFiller.Fill` is a static method that creates fresh internal state (`RemnantFiller`, `placedSoFar` list) on each call, so the same input item list can be passed to multiple runs without interference. Neither `IterativeShrinkFiller` nor `RemnantFiller` mutate `NestItem.Quantity`. Each run also produces independent `Part` instances (created by `DefaultNestEngine.Fill`), so compaction mutations on one run's parts don't affect another.
- Only the winning result gets applied to the quantity deduction at the end of `Nest()`

## Progress Reporting

- Each candidate run reports progress normally (user sees live updates during shrink iterations)
- Between candidates, report a status message like "Lead item 2/3: [drawing name]"
- Only the final winning result is reported with `isOverallBest: true` to avoid the UI flashing between intermediate results

## Early Exit

- If a candidate meets all requested quantities **and** plate utilization exceeds 50%, skip remaining candidates
- Unlimited-quantity items (`Quantity <= 0`) never satisfy the quantity condition, so all candidates are always tried
- Cancellation token is respected — if cancelled mid-run, return the best result across all completed candidates
- The 50% threshold is a constant (`MinEarlyExitUtilization`) that can be tuned if typical nesting utilization proves higher or lower

## Scope

Changes are confined to `StripNestEngine.Nest()`. No modifications to `IterativeShrinkFiller`, `ShrinkFiller`, `DefaultNestEngine`, fill strategies, or the UI.

## Files

- Modify: `OpenNest.Engine/StripNestEngine.cs`
- Add test: `OpenNest.Tests/StripNestEngineTests.cs` (verify multiple orderings are tried, early exit works)
