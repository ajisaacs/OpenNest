# Automatic nesting pipeline

`NestPipeline.Run` is the shared whole-job boundary in `OpenNest.Engine`:

1. Snapshot caller requirements and stock into a `NestJob`.
2. Resolve the selected name through `NestingEngineRegistry` (built-ins and plug-ins use the same path).
3. Bundle parts that fit other parts' enclosed cutouts into their frames ([filling cutouts](nesting-engines.md#filling-cutouts)); a refused or less complete bundled trial is replaced by the ordinary solve.
4. Run the engine without mutating caller drawings or plates.
5. Check returned placements with the independent `NestLayoutCheck` used by the benchmark (for bundles, again after expanding them to physical frames and inserts).
6. Consolidate exact ordered nonempty layouts only when the entire result is valid, then bind each layout to the caller's drawing references; the caller decides whether to commit it.

`NestStockBuilder.FromTemplate` snapshots sheet options and spacing settings for multiple-sheet jobs. `SinglePlate` offers exactly one physical sheet, regardless of the legacy plate repeat quantity. `NestPipelineCommit.ApplyToEmptyPlates` applies accepted desktop proposals to empty/new plates with the checked size, quadrant, spacing and total-copy quantity semantics. Quantity is set before parts are attached, so drawing counts multiply exactly once. It never fills occupied plates.

## Repeated layouts and physical sheets

Raw `NestJobResult` remains expanded: one entry per physical sheet, with physical stock usage and original instance indices. `NestPipelineResult.Raw` is not rewritten. Valid exact duplicates in the newly generated proposal become one `ProposedPlate` with positive `Quantity` (total copies, including the first; default one). Its three-field constructor/deconstruction remains supported. `PlateIndex` retains the first raw occurrence, and proposed layouts retain first-occurrence order.

Identity includes stock ID and exact stock settings plus the **ordered** requirement IDs and IEEE bits of X, Y and rotation. Instance indices do not affect grouping. Different same-name drawings, reordered parts, near-equal poses, signed zero differences, or different stock entries stay separate. No rounding, geometric equivalence or merging with existing occupied plates/cutting programs is performed. Empty sheets and all invalid-but-representable proposals stay ungrouped, preserving physical review indices; unknown requirements and nonfinite poses remain unkeepable. Drawing and bound-program freshness checks still apply to every grouped layout and its correspondence to the raw physical sheets.

Layout-entry count is not physical-sheet count: sum layout quantities for sheets, and multiply each layout's part counts, area, cutting time and costs by its quantity. `.nest` already stores `Plate.Quantity`; no file-format change is required. Exact output grouping does not itself remove engine searches.

## Desktop Auto Nest

Every engine selected in **Nest > Auto Nest** uses the pipeline. Part-First and its sorting controls are removed. Use interactive Fill Area/remnant tools for leftover space on occupied sheets; these are not whole-job nesting.

Auto Nest starts on empty/new sheets and does not change existing populated plates. Stock Options offers sheet sizes and salvage settings; minimum salvage size is part of that section, not a Part-First option. When enabled, its grid keeps a blank last row for adding another size (`W x L`) and cost, including after loading saved options. Unused blank rows are not offered as stock. The maximum remains 100 physical sheets per run.

Progress is owned and modal so the input drawings cannot be edited while a worker uses them. Stop or closing progress cancels and discards the whole proposal. The dialog waits for the worker to finish; no engine has an Accept-early button on this path. A plug-in that ignores the cancellation token cannot commit its late result.

Auto Nest never draws its parts on the plate view before they are kept: its result is a set of sheets, possibly of several sizes, not parts on the current plate. Ghost (preview) parts on the plate view mean only "what this plate gets if you accept now", and stay with interactive Fill and the remnant tools. Instead the progress window shows **Best plan so far**: every sheet of the engine's best whole-job plan drawn side by side at one scale, identical sheets once with their count, each captioned with size, part count and utilization. An engine reports a plan with `NestJobStage.PlanImproved` and `NestJobProgress.Plan`; `NestPipeline` adds `PlanSheets`, parts on detached drawings built from the job geometry, so nothing touches the caller's drawings or quantities. The plan is unvalidated and may be replaced. Default, Irregular, Rectangles and Circles report plans (see [nesting engines](nesting-engines.md#best-plan-so-far)); FlatBar, the fixed-strategy engines and plug-ins that report none show the strip empty until the review.

After a valid result, or an invalid one kept in the validation report, a review window shows the final sheets in the same strip with totals, the supplied-cost summary and every unplaced drawing, count and reason. **Keep** (Enter) commits the whole proposal; **Discard** (Escape) applies nothing.

Every invalid result shows the validation report before any plates are changed:

- **Discard** is the default button and the Escape action. No proposed parts or sheets are applied.
- **Keep anyway** requires an explicit click and keeps the entire representable proposal, then enables Overlap Check.
- Malformed output (unknown requirements or nonfinite poses) is not keepable. It cannot be faithfully represented as the proposed layout; no partial/truncated proposal is offered.

Overlap Check displays material overlaps, not every spacing/stock/rotation failure in the report. A layout passing validation may still be incomplete; completeness and stop reason are separate from geometric validity.

## Unplaced parts

Unplaced demand is never dropped silently. `NestPipelineResult.Unplaced` lists every requested copy the proposal leaves unplaced, by requirement (caller drawing) and `NestUnplacedReason`, counted from the returned placements rather than engine metadata:

- `NoStockFits`: the part's material bounds exceed every offered sheet's work area at each sampled legal rotation (up to 16; automatic rotation keeps its quarter turns).
- `FrameUnplaced`: the copy was reserved inside a cutout of a frame copy the engine did not place (see [filling cutouts](nesting-engines.md#filling-cutouts)).
- `StockExhausted`, `PlateLimitReached`: from the engine's stop reason.
- `NoPlacementFound`: the engine found no position; this does not prove none exists.
- `ProposalNotKeepable`: the proposal cannot be kept, so all demand is listed.

An incomplete but valid proposal is keepable like any other; keeping it is still an explicit action. Desktop Auto Nest lists each unplaced drawing, count and reason in its review before committing (the drawing list keeps showing remaining quantities); Console `--autonest` writes `Unplaced:` lines to standard error; MCP `autonest_plate` adds `Unplaced:` lines and `autonest_job` names the reasons when it refuses an incomplete proposal; the API returns `NestResponse.Unplaced` by request part ID. This check does not replace pre-post CNC verification.

The selected engine is saved in `%APPDATA%\OpenNest\engine-selection.json`.
Selection is restored after plug-ins load. A saved name from an earlier release
maps to the engine that replaced it (see [nesting engines](nesting-engines.md)).
If the saved engine is unavailable, Default is selected and the status bar
reports the fallback; startup does not replace the saved missing-engine preference.

## Machine edge clamps

A nest can carry the machine's fixed clamp rail: identical rectangular clamps
(width along the edge, depth into the sheet), each on one sheet edge with its
center measured from the plate origin along that edge. The edges are named from
the origin: the origin side and far side run along X, the origin end and far
end along Y. **Nest > Clamps...** edits the current nest's rail as a list, and
**Plate > Edit Clamps** edits it on the plate view (below). **OK** applies it to every plate of the nest (**Cancel** discards
the edits); removing every center removes the clamps. **Load Machine Defaults**
only fills the dialog with the clamps of the nest's machine (**Nest > Machine**).
**Save as Machine Defaults** writes the clamps, and whether new nests receive them,
into that machine's configuration immediately (in Database mode, the
[shared machine](shared-settings.md) once the server holds it); Cancel does not undo
that write. A nest without a machine on file cannot save its clamps. The dialog
refuses to open while nesting or a plate action is running.

Clamps are physical fixtures of a machine, so each machine keeps its own rail on the
**Clamps** tab of **Tools > Machine Configuration**: whether new nests get it, the
clamp width and depth, the minutes to remove one clamp, and the clamp centers, all in
the machine's units. New and BOM import give a new nest the rail of the machine they
assign it (the **Tools > Nest Defaults** machine, or the first by name), converted to
the nest's units. A nest keeps its own copy: editing a machine's clamps, or assigning a
nest another machine, never moves the clamps of an existing nest; use **Load Machine
Defaults** for that. Clamps are off by default: a machine without clamps, or with them
switched off or without a center, gives new nests none. A stored size or removal time
that is not valid falls back to the default and an invalid center is dropped.

Before machines held clamps, one `%APPDATA%\OpenNest\clamps.json` (or the server's
shared clamp settings) served every machine. At startup, while no machine has clamps of
its own, OpenNest copies that rail once into the Nest Defaults machine, or the only
machine, converted to the machine's units. With several machines and no default it asks
which machine has those clamps (once per session; **Not Now** leaves new nests without
clamps until a machine has some). Once any machine has clamps the old file is no longer
read; it is left in place.

Every plate follows the nest's rail at its own size and quadrant: clamps wholly
beyond a shorter sheet are left off it, one still reaching onto the sheet keeps
its whole footprint as the avoidance area (it may hang past the sheet edge), and
resizing or rotating a plate re-resolves its clamps. Auto Nest
offers each stock size with the rail resolved for that size, and every committed
sheet keeps exactly the clamps its layout was validated against. Clamps are
drawn as hatched amber rectangles over the sheet; they cannot be selected,
moved or sequenced and never count as parts. Interactive fill, pack and push
keep parts clear of clamps (fill drops the parts that land on one; see
[nesting engines](nesting-engines.md)), Check Overlaps reports parts on a clamp and manual
cut-offs crossing one, automatic cutoffs never place a cut through a clamp, and
pre-post verification warns (with per-attempt consent) about parts, cut-offs
and direct rapids crossing an active clamp - including the first rapid when a
clamp covers the program start point.

### Editing clamps on the plate view

**Plate > Edit Clamps** (or **Edit Clamps** on a clamp's right-click menu)
switches the plate view to clamp edit mode. Each clamp shows a locating
dimension: a line out across the sheet edge to its distance from the plate
origin. Right-click the sheet to choose **Dimension to Clamp Center** or
**Dimension to Near Edge** (the clamp edge nearer the origin, the default); the
choice is remembered.

- Drag a clamp to move it along its edge, or onto another edge. Positions snap
  to 1/8" (1 mm in metric nests) on the dimension's value; hold Shift to move
  freely.
- Point near an edge where there is no clamp: a dashed clamp previews the
  position, and a click adds it. A nest with no clamps takes the clamp size and
  removal time from the machine defaults.
- Click a dimension to type its value, then Enter (Escape cancels).
- Select a clamp and press Delete, or right-click it, to remove it.
- Escape or **Finish Editing Clamps** returns to Select mode.

Every change is applied at once to the nest's rail, so every plate follows it,
as with **OK** in **Nest > Clamps...**: removed clamps are put back on every
sheet, and changes are refused while nesting or a fill is running. Clamps
beyond the current sheet are not shown.

### Removing a clamp by hand

In Select mode, right-click a clamp on the sheet and choose **Remove clamp-N
from This Sheet**. Only that sheet changes. The clamp is drawn dashed with
"remove", parts may be placed over it, and it adds the rail's removal minutes to
that sheet's cut time, the same as a clamp Auto Nest removed. To put it back,
right-click the dashed clamp and choose **Put clamp-N Back on This Sheet**.
Parts already over the restored clamp are not moved; Check Overlaps and pre-post
verification report them. The menu is not offered while inspecting or
previewing a proposal. The change is refused while nesting or a fill is
running. **OK** in **Nest > Clamps...** re-resolves every plate from the edited
rail and puts every removed clamp back, whether removed by hand or by Auto Nest.

### Last-resort clamp removal

When a part cannot be placed on an empty sheet of any offered size with every
clamp in place - neither the selected engine nor the Irregular or Rectangles
lane finds a clamped placement - Auto Nest takes the fewest clamps off one sheet
that let it fit (not necessarily neighbours: a notch may let a clamp between
them stay; the clamps nearest the origin on ties) and nests it
on extra sheets with those clamps removed; other unplaced parts may share the
freed sheets. It never removes a clamp to save material, never re-plans a sheet
the normal nest already used, draws only on stock that is left, and only
removes clamps resolved from the machine rail. The completion summary lists
"Sheet N: remove clamp K (+M min)", the sheet shows each removed clamp dashed
with "remove", and each removed clamp on the sheet adds the rail's removal
minutes (default 5, set in **Nest > Clamps...**) to that sheet's cut time.

Limits: the search stops after 1000 single-part trial solves and then keeps the
normal result, removing nothing; and a part whose material clears a clamp by
less than the engines' 0.0001" grid may still be reported as needing a removal.

## Integration constraints

Callers must hold drawings and target state stable from snapshot through attachment. Do not append whole-job placements to an occupied target or flatten multiple returned sheets onto one plate: that would commit a layout different from the one checked. Do not trust arbitrary engine fulfillment metadata as a substitute for counting returned placements.

Interactive fills are outside this contract and still use `PlateFillService`. The benchmark invokes the same independent validator.

The pre-pipeline multi-plate orchestrator and plate-size optimizer have been removed from `OpenNest.Engine`; this is a source and binary break for external code that called them. Whole-job callers use `NestPipeline.Run` (or implement `INestingEngine` as a plug-in); multiple stock sizes and salvage credit are expressed as job stock and `NestJobOptions`.

## Console and MCP

Console `--autonest` uses the jobs engine named by `--engine` (Default when omitted) against one physical sheet unless `--max-sheets <n>` allows more. `--stock WxL[:cost],...` offers those sizes instead of the selected plate's size, each unlimited and built from the selected plate's spacing, quadrant and clamp rail; costs are all-or-none and a cost summary is printed for valid results. The selected plate's old parts are replaced by the first layout only after acceptance, further layouts are inserted as new plates right after it (with its grain and cutting parameters), and other plates are unchanged. Default demand is still one of each drawing unless `--quantity` is supplied. A partially fulfilled, valid result may be saved; a successful placement is not a claim that all demand was met. `--check-overlaps` checks every committed plate.

Invalid output is printed and rejected with exit code 2 without saving or posting. `--allow-invalid` explicitly accepts representable layout violations. Malformed output, more physical sheets than `--max-sheets`, and zero placements are never saved by this path. `--max-sheets` and `--stock` without `--autonest`, or with malformed values, exit 1 before solving. Unknown engines exit 1. `--autonest --keep-parts` rejects an occupied target: use the plain interactive fill path for existing obstacles instead.

MCP `autonest_plate` requires an empty target. The stdio server serializes all tool calls sharing its mutable session, so another request cannot change drawings or occupy a target during a solve. `allow_invalid` defaults to false. It reports violations and makes no changes on rejection, including with an override when the output is unrepresentable or contains multiple sheets. `engine` defaults to Default, and its description lists the built-in engines with what each suits. Fill tools (`fill_plate`, `fill_area`, `fill_remnants`, `pack_plate`) remain separate and default to the Fill strategy. Console and MCP load jobs plug-ins from `Engines/` beside their executable.

MCP `pack_plate` and `autonest_plate` report each requested drawing's newly placed count and remaining quantity, indexed by request; partial fulfillment is explicitly warned about. Per-sheet ratios are not enforced or required: an uneven sheet is acceptable if the remaining quantities are placed elsewhere in the nest. These two plate tools operate on a single selected plate and do not automatically finish a multi-sheet job; callers can use `autonest_job` for that. Duplicate request tokens and ambiguous drawing names are refused before placement. A zero-quantity pack request places zero parts for that drawing; negative quantities are rejected.

`autonest_job(plateIndex, drawingNames, quantities, engine?, no_new_plates=false, sheets?)` is the whole-session completion path. Quantities are total targets across existing placed parts (physical plate quantity included) plus newly proposed sheets. Without `sheets`, the selected empty cutoff-free plate supplies stock size, spacing, quadrant, grain and cutting settings; other empty plates with equivalent persisted settings may be reused first, then additional matching plates are created as needed. `no_new_plates=true` caps physical stock by the number of existing matching empty plates, not the number of grouped output layouts or their old repeat quantities. Exact repeats still attach once with their total quantity; redundant empty entries remain unchanged and are normally omitted on save. Empty entries consumed by those physical copies are reserved by their populated layout in the live MCP session and cannot be offered again or filled by another nesting tool while that owner remains populated. Clearing or removing the owner (including replacement by `load_nest`) releases only its surviving placeholders; clearing or deleting a placeholder does not release the owner's other copies or another layout's reservations. Genuinely unused entries remain available. No automatic deletion of user plates occurs. No new layout entries are created in this mode. The request uses the independently validated `NestPipeline` and commits only when every requested quantity is met; invalid, incomplete, cancelled or unknown-engine proposals leave existing plates and counts unchanged. A no-placement solver result is not proof that geometry cannot fit. `pack_plate` and `autonest_plate` remain single-sheet operations.

For finite mixed-size inventory, supply `sheets` as an array, for example `{"plateIndex":0,"drawingNames":"large,small","quantities":"1,1","engine":"Rectangles","sheets":[{"width":60,"length":120,"quantity":1},{"width":48,"length":96,"quantity":2}]}`. Width and length have the same units and orientation as `create_plate`. Every row's positive integer `quantity` is the total physical sheets available for this solve, including any compatible pre-created empty plates; it is never a `Plate.Quantity` repeat multiplier or additional stock. The selected plate supplies common spacing, edge spacing, quadrant, grain and cutting settings, but is reused only if its size matches a chosen sheet. Other compatible empty sheets can be reused; occupied or cutoff-bearing sheets cannot. Only needed different-sized sheets are created. Duplicate size pairs, nonpositive or nonfinite dimensions, unusable work areas, nonpositive quantities, an empty list and excessive total inventory are refused before solving. `sheets` cannot be combined with `no_new_plates=true`. Omit `sheets` to retain the previous single-size behavior. Successful output reports `stock-0`, `stock-1`, etc. in input order with physical used/remaining counts, reused/created layout counts, plus committed session layout index to stock ID and per-drawing totals. Stock is request-local, not a persistent purchasing catalog. Unused empty templates may be omitted by the `.nest` writer on save.

MCP `delete_plate(plateIndex)` removes only a sheet with no parts and no cutoff definitions, from either loaded or newly created session plates. Occupied, cutoff-bearing and invalid indices are refused without changing the session. After deletion every later index shifts down by one; inspect `get_plate_info` before further edits. This is session cleanup, not a data purge or a save-time option. `save_nest` lists all session plates in its response, but the `.nest` writer omits empty cutoff-free plates from the serialized file unless one carries clamp settings or clamp removals the saved defaults cannot recreate; reloading may therefore have fewer plates than the response says. There is no opt-in switch to preserve empty plates in the file.

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

Response archive schema 5 adds `Unplaced`; earlier archives load it empty (not recorded). Schema 4 records grouped-layout semantics. `SheetCount` is the physical sum of `Nest.Plates[*].Quantity`; fulfillment, stock usage, utilization (part area / physical sheet area), cutting time and costs are quantity-weighted. `PlateStockMappings[*].PlateIndex` indexes the actual materialized `Nest.Plates` layout entries, so mappings may be fewer than `SheetCount`. Schema 3 and unversioned archives still load unchanged: no regrouping or metadata reinterpretation occurs. Validation status and violations remain persisted; older archives without validation metadata load with null status, which is not a successful check. Saving a proposal archive does not authorize CNC posting.

## Verification

`dotnet test OpenNest.FrontEnd.Tests/OpenNest.FrontEnd.Tests.csproj` exercises Console, MCP and API rejection/override, cancellation, physical-sheet settings, response IDs and archive round-trips. It targets `net8.0` and runs on Linux, macOS and Windows. It is included in the solution and Windows test workflow. Desktop interaction tests remain in Windows-only `OpenNest.WinForms.Tests`.

## Stock costs

Auto Nest Stock Options accepts a generic **per physical sheet** Cost. Enter positive
finite costs on every active row in one common unit (purchase price, area, or another
consistent scalar). All-zero rows retain legacy area scoring: zero means unspecified,
not free stock. Negative/nonfinite costs and mixed zero/positive rows are rejected
before solving. The blank add row is ignored.

MCP `autonest_job` accepts optional `cost` on each `sheets` row, alongside `width`,
`length` and positive finite `quantity`. API `NestRequestPlate.Cost` uses the same
nullable contract: omit every cost for area scoring, or supply strictly positive
finite values for every available row. Explicit zero is invalid in these requests.
Engine stock with quantity zero is unavailable and does not select the cost mode.
MCP continues to reject duplicate sizes; API/engine stock IDs keep independent inventory.

With prices, gross sheet cost is the supplied value. Salvage is still computed from
the largest qualifying material-edge offcut; net cost is
`Cost * (NetSheetArea / SheetArea)`. Set desktop salvage rate to zero for purchase-price
comparisons. Entering prices preserves the selected salvage settings. MCP and API
currently disable salvage. Without prices, scores retain their existing area units;
whole-job engines break exact cost ties by the compactness offcut (see
[cost basis](nesting-engines.md#cost-basis-and-heuristic-limits)), and fixed strategies retain
their legacy no-salvage area tie-break.

Desktop reports gross cost, estimated salvage credit and net score. MCP reports
`basis`, each stock's used physical count, supplied cost and gross subtotal, plus
gross total, estimated salvage credit and net score. API exposes these under `Costs`
(`Basis`, `Stock`, `GrossTotal`, `SalvageCredit`, `NetScore`) and persists them in
quote archives. For example, two sheets costing 25 each report gross total 50;
with salvage disabled, credit is 0 and net score is 50. `area` identifies fallback
scoring; `supplied-cost` identifies explicit prices. Totals cover the returned proposal,
not previously occupied session sheets, and exclude penalties for unplaced demand.
Invalid API proposals have no cost summary. Overflow fails instead of becoming a score.

An accepted MCP solve stores requested drawing quantities and offered stock prices;
save/reopen preserves them and existing nest metadata. Rejected/cancelled proposals
change neither requirements nor options. Inventory remains request-local; saved
options are offers, not a per-sheet purchase ledger. These are cost-aware heuristics,
not global cost optimizers; validation and fulfillment remain required.

## Stock from MaterialLedger

Auto Nest can offer the sheets and drops that MaterialLedger reports as free for the
nest's material. The ledger is optional: with no URL set, or the ledger unreachable,
Auto Nest, save and post work exactly as before. OpenNest only reads from the ledger.
The stock it loads is a snapshot taken at nesting time and reserves nothing. ML's hold
batch stays the only authority, and another programmer can hold the same drop first.

**Setup (per PC).** Tools > Options > MaterialLedger takes the ledger URL and the API
key for ML's `Api:Keys:OpenNest`. They are stored in `%APPDATA%\OpenNest\material-ledger.json`,
with the key encrypted by DPAPI for the current Windows user. The key never goes in
`defaults.json` or a nest file. **Test** makes the same request with a material no
mapping names, so it reports the connection as working when ML answers `mapped: false`.

**Request.** **Load from ledger** above the Stock Options grid calls
`GET {url}/api/stock/opennest?material=&grade=&thicknessIn=` with the `X-Api-Key`
header and a 5 s timeout. The request sends the nest's `Material.Name`, its `Material.Grade`
and the thickness converted to inches (rounded to 4 places). ML resolves its own
OpenNest material mapping, and OpenNest keeps no copy of it. OpenNest's grade is a shop
condition such as HR or P&O; ML's acceptance rule for the material and grade decides which
catalog grades and finishes may fill it, ranked by preference. Rank 0 is an exact mapping
override and rank 1 the rule's first entry. The best rule rank ML matched at the nest's
thickness is the preferred material, and higher ranks are substitutes, which only ever
upgrade (P&O may fill HR, never the reverse). A rule that lists sheet grades first can
match only its plate entries at plate thickness: there A36 at rank 3 is preferred and
A572 at rank 4 the substitute, as ML labels them. Specs carry `rank`, `materialFamily`,
`grade` and `finish`; items carry `rank` and arrive in rank, location code, item code order.
An older ledger that sends none of these still loads: its stock counts as rank 1, unlabeled.
The button is enabled when
the ledger is configured and the nest has a material and a thickness. The request runs
while the dialog stays usable, and closing the dialog cancels it. The client
(`OpenNest.Data/MaterialLedger/`) returns `NotConfigured`, `Unreachable(reason)`,
`Unauthorized` (401/403), `Unmapped` (`mapped: false`) or `Ok` and never throws for these
failures. Unknown `kind`/`shape` strings still deserialize.

**Rows.** `LedgerStockMapper` turns the items into stock rows. Ledger sizes are inches
with the short side first, and become `Size(short, long)` in nest units:

| Ledger item | Stock row |
|---|---|
| `FullSheetBundle` / `FullSheet`, free quantity N | one row, quantity N |
| `Drop` | drops of identical usable size (3-place inches) and the same spec share one row; quantity is the count |
| `IrregularConservativeRectangle` | its conservative rectangle, kept apart and marked "irregular" |
| `Unknown` or an unrecognized shape | skipped and counted in the summary |
| no part to nest fits in either orientation | skipped and counted in the summary |

Rows run in rank order. Within a rank, and within each row's item codes (one per sheet),
the ledger's own order is kept, so item assignment takes the preferred, first-listed items
first. Each row also keeps its locations for the tooltip, its rank and a label from its
spec's grade and finish (for example "A1011 P&O"). The engine sees only ordinary
`NestPlateStock` rows with ids `ledger:{n}` and finite quantities. It never sees item
codes or ranks, so it does not prefer low-rank stock; a stock priority would be a
separate `NestJob` input. `OpenNest.Engine` has no ledger reference.

**Grid.** A load replaces earlier ledger rows and keeps hand-typed rows. A ledger row's
Source shows its label, or "Ledger" when the ledger sends no grade or finish, with
"(irr.)" for an irregular drop. A substitute (a rank above the preferred one) reads, for example,
"A1011 P&O for HR" in amber, and its tooltip names the substitution and rank ahead of the
usual item codes and locations. Substitutes load ticked like other rows; untick **Use** to
keep them out of the run. Ledger rows' size and cost are read-only. You can lower a ledger
row's quantity (not above the free count) or untick **Use** to leave any row out.
The **Qty** column applies to every row: blank offers the size without limit, as
hand-typed rows always have, and a number is passed as `PlateOption.Quantity` to
`NestPlateStock.Quantity`. A summary line under the grid reports the load, for example
"Ledger: 3 sheet sizes, 7 drops, 2 substitute rows (2 skipped: 2 unknown shape)", or the failure. An
unmapped material clears the ledger rows. A failed request keeps them.

**Codes on other nests.** ML's free counts drop only once someone reserves a nest's sheets
there, so until then another nest's coded sheets still look free. The load therefore also
reads the codes on every other open server nest (Draft, Quote or To Be Cut, not deleted)
from [`/api/nests/stock-item-codes`](nest-storage.md#stock-item-codes-in-use), passing the
nest being edited as `exclude` so its own codes stay offered. `LedgerCodeFilter`
(`OpenNest.Data/MaterialLedger/`) takes each such code out of the mapped rows: every copy
leaves the row's item codes and locations, its quantity drops by the copies removed (a
bundle loses all its sheets), and a row left empty is dropped. The summary counts them, for
example "Ledger: 2 sheet sizes, 5 drops (3 on other nests)". When the codes cannot be read
(File mode, the nest server unreachable or too old) the stock loads whole and the summary
ends "(other nests not checked: reason)"; the load never waits more than 5 s on the server.
Gaps remain: a nest not yet saved on another PC, and stock taken in ML for other work, are
not seen.

**Persistence.** Ledger rows and quantities are request-local and are never written to
`Nest.PlateOptions`. After a kept result, the nest saves the hand-typed rows with their
typed costs, as before.

**Item codes.** After a kept result is committed, `LedgerStockAssignment` gives each
plate on a ledger row that row's next item codes, in nest plate order, one per sheet of
the plate's quantity (a bundle repeats its code). It reads each sheet's stock from
`NestPipelineCommit.PairWithStock`; a sheet Auto Nest solved with clamps removed takes its
base row's codes. Plates committed on hand-typed stock get none, and a discarded,
rejected or cancelled result changes no codes. The plate keeps them as
`Plate.StockItemCodes` (saved in the nest, see the [nest file format](nest-file-format.md)),
and `/api/nests/{id}/sheets` reports them per plate so MaterialLedger can hold those exact
items. The plate list's tooltip shows "Stock: CODE (location)"; the location comes from
this window's ledger load and is never saved. A re-nest builds new plates, so it drops codes.

**Re-check before save and post.** Codes can be taken after Auto Nest: another nest is
coded with the same sheet, or ML holds or consumes it. Before a nest with any
`StockItemCodes` is saved to the server (Save or Save As in Database mode) or posted,
`LedgerCodeRecheck` rereads the ledger's free stock for the nest's material, grade and
thickness, the nest's own ML holds and the other open nests' codes (excluding this nest;
a Save As excludes nothing, since the original keeps its codes). `LedgerCodeCheck` reports
each code another open nest is coded with, and each code the ledger no longer has free for
this nest: an open hold of this nest counts as its own sheet, and a bundle short of the
sheets the nest cuts from it is reported with the count free. The **Ledger Stock Changed**
dialog lists them by plate and offers:

- **Save Anyway** / **Post Anyway**, enabled only once its acknowledgement box is ticked;
- **Remove Codes and Save** / **Post**, which takes every copy of those codes off the plates
  as one undoable edit ("Remove Ledger Codes") and keeps the layout; the uncoded sheets are
  matched by size in ML;
- **Cancel**, which stops this save or post.

Like [pre-post verification](post-verification.md), the dialog is fresh on every attempt and
its consent is never remembered. A PC without a ledger, a nest without a material or
thickness, and a ledger that cannot be read (unreachable, key rejected, material unmapped)
skip the check; the status bar says why it was skipped, and the save or post goes on. When
only the other nests' codes cannot be read, the ledger is still checked and the status bar
says other nests were not.

**Cost.** Ledger rows load without cost. A ledger `CostPerUnit` is per lot unit, and a
drop's is its source sheet's. While any ledger row is ticked, the run scores by area and
hand-typed costs are ignored. The summary line says so, and the saved options keep
those costs. Pro-rating drop cost by area is a possible later change. Area scoring does
not make drops preferred over full sheets; a stock priority would be a `NestJob` input
with its own benchmark.

MCP, Console and `OpenNest.Api` do not load ledger stock yet. The client, mapper and code
filter live in `OpenNest.Data`, so these front ends can use them.

The status bar shows a server nest's ledger holds and reminds you to close them out once
the nest is cut; see [MaterialLedger holds](nest-storage.md#materialledger-holds-desktop).
