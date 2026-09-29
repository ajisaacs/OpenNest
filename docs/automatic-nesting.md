# Automatic nesting pipeline

`NestPipeline.Run` is the shared whole-job boundary in `OpenNest.Engine`:

1. Snapshot caller requirements and stock into a `NestJob`.
2. Resolve the selected name through `NestingEngineRegistry` (built-ins and plug-ins use the same path).
3. Run the engine without mutating caller drawings or plates.
4. Check returned placements with the independent `NestLayoutCheck` used by the benchmark.
5. Bind a representable result back to the caller's drawing references; the caller decides whether to commit it.

`NestStockBuilder.FromTemplate` snapshots sheet options and spacing settings for multiple-sheet jobs. `SinglePlate` offers exactly one physical sheet, regardless of the legacy plate repeat quantity. `NestPipelineCommit.ApplyToEmptyPlates` applies accepted desktop proposals to empty/new plates with the checked size, quadrant, spacing and quantity-one semantics. It never fills occupied plates.

## Desktop Auto Nest

Every engine selected in **Nest > Auto Nest** uses the pipeline. Part-First and its sorting controls are removed. Use interactive Fill Area/remnant tools for leftover space on occupied sheets; these are not whole-job nesting.

Auto Nest starts on empty/new sheets and does not change existing populated plates. Stock Options offers sheet sizes and salvage settings; minimum salvage size is part of that section, not a Part-First option. The maximum remains 100 physical sheets per run.

Progress is owned and modal so the input drawings cannot be edited while a worker uses them. Stop or closing progress cancels and discards the whole proposal. The dialog waits for the worker to finish; no engine has an Accept-early button on this path. A plug-in that ignores the cancellation token cannot commit its late result.

Every invalid result shows the validation report before any plates are changed:

- **Discard** is the default button and the Escape action. No proposed parts or sheets are applied.
- **Keep anyway** requires an explicit click and keeps the entire representable proposal, then enables Overlap Check.
- Malformed output (unknown requirements or nonfinite poses) is not keepable. It cannot be faithfully represented as the proposed layout; no partial/truncated proposal is offered.

Overlap Check displays material overlaps, not every spacing/stock/rotation failure in the report. A layout passing validation may still be incomplete; completeness and stop reason are separate from geometric validity. This check does not replace pre-post CNC verification.

The selected engine is saved in `%APPDATA%\OpenNest\engine-selection.json`.
Selection is restored after plug-ins load. If the saved engine is unavailable,
Default is selected and the status bar reports the fallback; startup does not
replace the saved missing-engine preference.

## Integration constraints

Callers must hold drawings and target state stable from snapshot through attachment. Do not append whole-job placements to an occupied target or flatten multiple returned sheets onto one plate: that would commit a layout different from the one checked. Do not trust arbitrary engine fulfillment metadata as a substitute for counting returned placements.

Interactive fills are outside this contract and still use `PlateFillService`. The benchmark invokes the same independent validator. Console/MCP/API migration is a separate adoption step; the existence of the pipeline does not imply every front end already calls it.
