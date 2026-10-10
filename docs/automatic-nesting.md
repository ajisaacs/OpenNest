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

Auto Nest starts on empty/new sheets and does not change existing populated plates. Stock Options offers sheet sizes and salvage settings; minimum salvage size is part of that section, not a Part-First option. When enabled, its grid keeps a blank last row for adding another size (`W x L`) and cost, including after loading saved options. Unused blank rows are not offered as stock. The maximum remains 100 physical sheets per run.

Progress is owned and modal so the input drawings cannot be edited while a worker uses them. Stop or closing progress cancels and discards the whole proposal. The dialog waits for the worker to finish; no engine has an Accept-early button on this path. A plug-in that ignores the cancellation token cannot commit its late result.

Every invalid result shows the validation report before any plates are changed:

- **Discard** is the default button and the Escape action. No proposed parts or sheets are applied.
- **Keep anyway** requires an explicit click and keeps the entire representable proposal, then enables Overlap Check.
- Malformed output (unknown requirements or nonfinite poses) is not keepable. It cannot be faithfully represented as the proposed layout; no partial/truncated proposal is offered.

Overlap Check displays material overlaps, not every spacing/stock/rotation failure in the report. A layout passing validation may still be incomplete; completeness and stop reason are separate from geometric validity. This check does not replace pre-post CNC verification.

The selected engine is saved in `%APPDATA%\OpenNest\engine-selection.json`.
Selection is restored after plug-ins load. A saved name from an earlier release
maps to the engine that replaced it (see [nesting engines](nesting-engines.md)).
If the saved engine is unavailable, Default is selected and the status bar
reports the fallback; startup does not replace the saved missing-engine preference.

## Integration constraints

Callers must hold drawings and target state stable from snapshot through attachment. Do not append whole-job placements to an occupied target or flatten multiple returned sheets onto one plate: that would commit a layout different from the one checked. Do not trust arbitrary engine fulfillment metadata as a substitute for counting returned placements.

Interactive fills are outside this contract and still use `PlateFillService`. The benchmark invokes the same independent validator.

The pre-pipeline multi-plate orchestrator and plate-size optimizer have been removed from `OpenNest.Engine`; this is a source and binary break for external code that called them. Whole-job callers use `NestPipeline.Run` (or implement `INestingEngine` as a plug-in); multiple stock sizes and salvage credit are expressed as job stock and `NestJobOptions`.

## Console and MCP

Console `--autonest` uses the jobs engine named by `--engine` (Default when omitted) against one physical sheet. The selected plate's old parts are replaced only after acceptance; other plates are unchanged. Default demand is still one of each drawing unless `--quantity` is supplied. A partially fulfilled, valid result may be saved; a successful placement is not a claim that all demand was met.

Invalid output is printed and rejected with exit code 2 without saving or posting. `--allow-invalid` explicitly accepts representable layout violations. Malformed output, multiple returned sheets, and zero placements are never saved by this path. Unknown engines exit 1. `--autonest --keep-parts` rejects an occupied target: use the plain interactive fill path for existing obstacles instead.

MCP `autonest_plate` requires an empty target. The stdio server serializes all tool calls sharing its mutable session, so another request cannot change drawings or occupy a target during a solve. `allow_invalid` defaults to false. It reports violations and makes no changes on rejection, including with an override when the output is unrepresentable or contains multiple sheets. `engine` defaults to Default, and its description lists the built-in engines with what each suits. Fill tools (`fill_plate`, `fill_area`, `fill_remnants`, `pack_plate`) remain separate and default to the Fill strategy. Console and MCP load jobs plug-ins from `Engines/` beside their executable.

MCP `pack_plate` and `autonest_plate` report each requested drawing's newly placed count and remaining quantity, indexed by request; partial fulfillment is explicitly warned about. Per-sheet ratios are not enforced or required: an uneven sheet is acceptable if the remaining quantities are placed elsewhere in the nest. These two plate tools operate on a single selected plate and do not automatically finish a multi-sheet job; callers can use `autonest_job` for that. Duplicate request tokens and ambiguous drawing names are refused before placement. A zero-quantity pack request places zero parts for that drawing; negative quantities are rejected.

`autonest_job(plateIndex, drawingNames, quantities, engine?, no_new_plates=false, sheets?)` is the whole-session completion path. Quantities are total targets across existing placed parts (physical plate quantity included) plus newly proposed sheets. Without `sheets`, the selected empty cutoff-free plate supplies stock size, spacing, quadrant, grain and cutting settings; other empty plates with equivalent persisted settings may be reused first, then additional matching plates are created as needed. `no_new_plates=true` limits this legacy solve to existing matching empty plates. The request uses the independently validated `NestPipeline` and commits only when every requested quantity is met; invalid, incomplete, cancelled or unknown-engine proposals leave existing plates and counts unchanged. A no-placement solver result is not proof that geometry cannot fit. `pack_plate` and `autonest_plate` remain single-sheet operations.

For finite mixed-size inventory, supply `sheets` as an array, for example `{"plateIndex":0,"drawingNames":"large,small","quantities":"1,1","engine":"Rectangles","sheets":[{"width":60,"length":120,"quantity":1},{"width":48,"length":96,"quantity":2}]}`. Width and length have the same units and orientation as `create_plate`. Every row's positive integer `quantity` is the total physical sheets available for this solve, including any compatible pre-created empty plates; it is never a `Plate.Quantity` repeat multiplier or additional stock. The selected plate supplies common spacing, edge spacing, quadrant, grain and cutting settings, but is reused only if its size matches a chosen sheet. Other compatible empty sheets can be reused; occupied or cutoff-bearing sheets cannot. Only needed different-sized sheets are created. Duplicate size pairs, nonpositive or nonfinite dimensions, unusable work areas, nonpositive quantities, an empty list and excessive total inventory are refused before solving. `sheets` cannot be combined with `no_new_plates=true`. Omit `sheets` to retain the previous single-size behavior. Successful output reports `stock-0`, `stock-1`, etc. in input order with used/remaining/reused/created physical counts, plus committed session plate index to stock ID and per-drawing totals. Stock is request-local, not a persistent purchasing catalog. Unused empty templates may be omitted by the `.nest` writer on save.

MCP `delete_plate(plateIndex)` removes only a sheet with no parts and no cutoff definitions, from either loaded or newly created session plates. Occupied, cutoff-bearing and invalid indices are refused without changing the session. After deletion every later index shifts down by one; inspect `get_plate_info` before further edits. This is session cleanup, not a data purge or a save-time option. `save_nest` lists all session plates in its response, but the existing `.nest` writer omits empty cutoff-free plates from the serialized file; reloading may therefore have fewer plates than the response says. There is no opt-in switch to preserve empty plates in the file.

### MCP engine development harness

`test_engine` builds and runs `OpenNest.Console` in a configured, trusted checkout.
`nestFile` is required; there is no machine-specific sample default. Drawing,
plate and output arguments and the stdout / `=== Errors ===` / nonzero-exit
response format are unchanged. Relative nest/output paths resolve from the MCP
server's working directory, before launching the checkout's console.

The existing .NET host configuration accepts:

- `EngineHarness:SourceRoot`: required absolute checkout path containing
  `OpenNest.Console/OpenNest.Console.csproj`.
- `EngineHarness:DotnetPath`: optional existing absolute executable path;
  defaults to the dotnet host in the active .NET installation, never a PATH search.
- `EngineHarness:TimeoutSeconds`: positive integer, default **120**, maximum
  2147483. The single deadline covers build/run, process exit and both concurrent
  output drains. MCP request cancellation uses the same bounded path.

For environment variables, use `EngineHarness__SourceRoot`,
`EngineHarness__DotnetPath` and `EngineHarness__TimeoutSeconds`. These are
server/operator settings, not caller-supplied executable or checkout overrides.

Timeout/cancellation returns a clear error, kills the owned live process tree,
waits up to five additional seconds for direct-process cleanup, and closes local
pipe readers. Cleanup failures are reported. A descendant already detached when
its parent exits cannot reliably be found by `Process.Kill(entireProcessTree)`;
it may remain alive, but inherited pipe writers cannot hold the tool open.
The harness does not scan for or kill unrelated processes. Windows process-tree
semantics still need Windows runtime acceptance; the private process regressions
also execute on Linux without customer nest files.

## .NET API and saved responses

Set `NestRequest.Engine` to a registered engine name; null retains `PlacementStrategy` / legacy `Strategy` behavior. Library hosts own plug-in discovery via `NestingEngineRegistry.LoadPlugins` before calling the API. Explicit request requirement IDs are preserved in response fulfillment even when multiple requirements use the same source DXF.

The API returns a detached proposal. `ValidationStatus` is `Valid`, `Invalid`, or `Unrepresentable`, and `Violations` lists the problems. Representable invalid proposals retain their parts for caller review; unrepresentable proposals contain no sheets. Consumers must inspect validation before applying, quoting or posting the proposal. `Status` describes fulfillment, not acceptance: counts, stock usage and completeness are derived from returned placements rather than trusting plug-in summary metadata.

Response archive schema 3 persists validation status and violations. Older archives with no validation metadata load with null status; null must not be interpreted as a successful check. Saving a proposal archive records it and does not authorize CNC posting.

## Verification

`dotnet test OpenNest.FrontEnd.Tests/OpenNest.FrontEnd.Tests.csproj` exercises Console, MCP and API rejection/override, cancellation, physical-sheet settings, response IDs and archive round-trips. Its current target matches MCP's `net8.0-windows` marker but does not use WindowsDesktop and executes on Linux as well as Windows. It is included in the solution and Windows test workflow. Desktop interaction tests remain in Windows-only `OpenNest.WinForms.Tests`.

## Stock costs

API `NestRequestPlate.Cost` is an optional per-physical-sheet cost in caller-consistent
units. Omit every cost for area scoring, or supply strictly positive finite costs
for every available row. Explicit zero and mixed priced/unpriced available stock
are rejected before import. Quantity-zero rows do not select the cost mode.

Validated proposals expose `Costs` (`Basis`, `Stock`, `GrossTotal`, `SalvageCredit`,
`NetScore`) and persist them in quote archives. `area` identifies fallback scoring;
`supplied-cost` identifies explicit prices. API salvage is disabled, so two sheets
costing 25 each report gross and net totals of 50 with zero salvage credit. Totals
exclude penalties for unplaced demand. Invalid proposals have no cost summary;
overflow fails instead of becoming a score.

Returned nests preserve offered stock prices. These are cost-aware heuristics,
not global cost optimizers; validation and fulfillment remain required.


MCP `autonest_job` accepts optional `cost` on each `sheets` row, alongside `width`,
`length` and positive finite `quantity`. Supply positive finite costs on every row
or omit all costs. MCP continues to reject duplicate sizes and disables salvage.
Its result reports the cost basis, used physical counts, supplied costs, gross
subtotals, gross total, estimated salvage credit and net score for the proposal.

An accepted MCP solve stores requested drawing quantities and offered stock prices;
save/reopen preserves them and existing nest metadata, plate defaults and salvage
settings. Rejected/cancelled proposals change neither requirements nor options.
Inventory remains request-local; saved options are offers, not a purchase ledger.
