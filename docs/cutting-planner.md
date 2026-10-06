# Unified cutting planner: direct-XY proposals

`OpenNest.Engine.CuttingPlanning.CuttingPlanService` plans contiguous whole-part
programs. It can retain fixed programs or jointly choose internal contour order,
entries and whole-part order using explicitly confirmed cutting parameters. It
returns an owned proposal; `Apply` installs Ready plate-scoped proposals atomically
after an exact freshness check. The desktop opens it from `Plate > Plan Cutting...` and
`Nest > Plan Cutting (All Plates)...` (see [Desktop workflow](#desktop-workflow)); the older
automatic sequencing and lead-in assignment commands remain until they are retired.

## Capture before worker planning

Create a `CuttingPlanRequest`, then call `Capture` while source placements,
programs and settings are stable. Pass that snapshot to `Plan` on a worker;
`Plan(request)` combines these steps synchronously. The modeled starting point
defaults to `Vector.Zero`, not a discovered controller position.

```csharp
var request = CuttingPlanRequest.ForPlate(plate, startPoint: start,
    confirmedParameters: parameters, expansionBudget: 20000,
    maxEntries: 16, preservePartOrder: false);
var snapshot = CuttingPlanService.Capture(request, cancellationToken);
var result = CuttingPlanService.Plan(snapshot, cancellationToken);
```

- A plate-scoped request (`CuttingPlanRequest.ForPlate`) plans the plate's current
  parts and records its exact state for `Apply`. A detached part list (`new CuttingPlanRequest(parts, ...)`)
  plans the same way but can never be applied. An empty plate is a Ready no-op.
- Omitting `confirmedParameters` preserves the original fixed-program contract:
  locked and unlocked programs stay fixed; only whole-part order may change.
- Supplying confirmed parameters enables regeneration for unlocked placements.
  `eligibleParts` can restrict it to an explicit reference-based subset; an empty
  subset retains all programs but still checks their leads against owned material.
  Locked placements never regenerate. Foreign or duplicate eligible identities
  are invalid; eligibility without confirmed parameters is invalid.
- `preservePartOrder` fixes whole-part order, not eligible internal contour choices.
- Parameters are caller-confirmed inputs. The service does not recover missing
  operator settings or silently change lead styles to find a solution.

Capture owns clean geometry, placed programs and required settings. Source `Part`
references are identity handles only: workers never read their mutable state.
Clean geometry accounts for the base program's existing rotation before applying
placement rotation; placement translation is applied once. Subprogram copying
must not rotate shared programs through their property setters. No live drawings
are attached to preview plates, so capture/search do not change quantity accounting.
Planning works from this historical snapshot; Apply compares it with live state.
Original
clean and executable graphs are type/mode-checked before cloning can erase unknown
semantics. Exact placed/proposed copies preserve authored motion feed/exact-stop
flags, symbolic bindings and shared subprogram identity; unsupported graphs are
refused. The geometry-only clean transform uses per-parent copies so legacy rotation
does not visit a globally shared descendant twice; it never changes the fixed payload.

## Cutting dependencies

Capture builds whole-part prerequisites from owned values, and both the search and
the final replay enforce them:

- A cutoff precedes every part its nominal line crosses within its span, using the
  same rule as automatic sequencing: nominal position and limits against the part's
  placed bounds, matched by drawing reference, never by name or trimmed segments. A
  cutoff whose definition is missing precedes every part. Cutoffs need a
  plate-scoped request; they are always fixed programs, need no lead-in, are not
  material for lead validation and, being open cuts, never become rapid obstacles.
  Rapids into and out of them are still checked.
- A part whose perimeter lies strictly inside a cutout of another part precedes
  that host. Material bounds (never rapids or scribe marks) only select candidate
  pairs; containment is proven on native clean material. Touching or crossing boundaries, or material that cannot be
  captured, refuse as `UnsupportedGeometry` naming both parts. A part in a concave
  pocket outside the host's material has no dependency.
- A preserved manual order that violates a prerequisite, or a cycle, is a
  `ConstraintConflict`. Replay rechecks the captured prerequisites and refuses a
  violating order rather than trusting the search.

## Search and exact output

With regeneration, the bounded deterministic search plans internal contour order
and native entry candidates part by part along a whole-part order. Internal
contours precede their own perimeter; parts remain contiguous. Backtracking can
revisit an earlier entry when a later part cannot be reached safely.

A preserved order is followed as given. Otherwise the order is an open
travelling-salesman path over part centres from the start point: nearest neighbour,
then 2-opt reversals and Or-opt moves of one to three parts, never placing a part
before a cutoff or nested-part prerequisite. If a part on that order cannot be
reached without crossing parts already cut, the search learns "cut this part
before those", backs up to just before the earliest of them and re-plans the rest
from the tool position there; parts cut before that point are kept. An attempt
stops backtracking after a stall of 8 x entries x contours expansions without
getting further, so it learns instead of retrying every entry combination of the
parts before it. When nothing new can be learned the result is a refusal.

Candidates use native closest points, vertices, midpoints and circle angles in
stable order, capped by `maxEntries`. Circle rounding, clamping, corner resolution
and tab trimming happen during emission. Validation uses the actual emitted
motions, never the nominal entry point alone. Existing lead styles are not
shortened, disabled or substituted as a search fallback.

Every candidate rapid is checked against contours already completed, including
earlier holes in the same part. Future contours are not yet obstacles. Rapid and
lead checks skip contours and material whose extents (an arc's whole supporting
circle) are more than 1e-6 x (1 + coordinate size) clear of the motion; anything
closer, touching included, gets the full native check. Actual
lead-in and lead-out line/arc paths must stay in target scrap and avoid other
placed material; holes in other parts remain scrap. Tangent/coincident contacts
outside the genuine target contour joint and numerically uncertain queries refuse.
Material capture supports a simple closed perimeter minus disjoint, non-nested
holes; unsupported topology is not a bounding-box approximation.

Candidates rank by actual modeled rapid distance with stable source/contour/entry
ordinals. Hash values and drawing names are not tie breakers. The expansion budget
counts rejected candidates and frontier ranking as well as accepted moves, before
emission; it is not a wall-clock timeout. Callers can cancel. Exhaustion may occur
before already-generated siblings are traversed; it returns a refusal, not an
unranked fallback or a proof of geometric impossibility.

Selected programs are replayed from the beginning with a fresh checker and fresh
lead validation, without regenerating them or trusting cached search verdicts.
Before replay, expected-emission geometry is independently built from the owned
choices/settings, not from the candidate payload. Replay checks actual selected
code against it and independently accounts for directed native boundary coverage:
no partial, duplicated, retraced or reversed cuts, except the exact selected tab.
Equivalent subdivisions and merged collinear moves remain valid. Captured source
identity and pose binding use exact scalar bits, not geometric tolerance.
Arrival positions use actual departures, including lead-outs and subprograms.
`ProposedOrder` retains source identities/poses; `CopyProgram()` returns an
independent deep copy of each exact captured/generated program. `ContourChoices`
are nominal choice metadata, not a substitute for reading actual execution.
Neither obtaining a proposal nor copying its programs installs them on live parts.

## Results and refusal

`Ready` and `IndependentlyReplayed` describe the modeled proposal only. In the
no-parameter fixed route, replay checks rapid crossings, missing leads and
incomplete retention; it does not add regeneration-mode material/lead checks.
In regeneration mode, replay also checks actual lead paths and contour accounting
against owned clean material. Neither mode certifies final NC, production cutting
readiness or physical machine safety.

Findings and source ordinals use the original zero-based source list, not proposed
sequence positions. A non-ready result contains no proposed order or unsafe fallback.

- `ConstraintConflict`: fixed programs or explored fixed routing violate the
  modeled constraints. Locked internal crossings cannot be repaired by regeneration.
- `UnsupportedGeometry`: unsupported motion/material semantics or an incomplete
  check. Open nominal outlines, ambiguous release states or containment, cutoffs
  in a detached part list and scribe-only source drawings are not silently accepted.
- `InvalidInput`: malformed/missing/duplicate placements or settings, invalid
  geometry, empty input, invalid eligibility or nonpositive bounds.
- `NoSolutionWithinBudget`: the bounded/capped search found no complete proposal;
  it does not prove no possible geometric route exists.
- `Cancelled`: capture, search or replay cancelled without live mutation.

Malformed original executed graphs are refused, not salvaged. Valid but incomplete
old programs can regenerate from clean geometry. Regenerated programs retain genuine
configured tab gaps; stale tab settings do not establish retention. In confirmed-
parameters mode, locked/ineligible programs must cover the complete directed clean
boundary: an open fixed program has no certified selected tab metadata and is refused,
not repaired, even if it may have been intentionally tabbed. The no-parameter route
retains its narrower compatibility contract. A lead-out that may bridge a tab, or
a malformed emitted arc, is refused, not automatically repaired. Tabbed lead-outs
leave from the trimmed cut end, but a lead-out after an open contour still needs
manual review of its retention gap, so confirmed-parameters planning refuses it.

## Apply

```csharp
var commit = CuttingPlanService.Apply(results, cancellationToken); // one result per plate
```

Call it on the thread that owns the plates, with Ready, independently replayed
results from plate-scoped requests; anything else is `InvalidInput`. Apply never
replans. Each plate is compared exactly with the state captured with its request:
part list instance and order, plate quantity/size/quadrant and settings, cutoff
definitions, and for every part its program reference and exact content (an
in-place edit counts), drawing program and cutoff classification, pose bits,
lead-in/lock flags, settings (reference and exact content) and bounds. Any
difference on any plate returns `Stale` and changes nothing, so a proposal that
changes a plate can be applied once; an unchanged (no-op) proposal stays current.
A malformed live program is also `Stale`, not an exception. A part repeated on
two plates of one scope is `InvalidInput`. Caller-confirmed planning settings are
input, not plate state: editing a separate confirmed-settings object after
capture does not stale the result (confirmed settings that are also a part's or
the plate's live settings are live state, and editing them does). Settings are
compared member by member, and only the exact built-in settings types are
supported (the same set regeneration copies): a plate-scoped request whose part
or plate settings, or any lead-in, lead-out, tab, sequencing or assignment
object inside them, is another type (a subclass included) returns
`UnsupportedGeometry` without running that type's code. A settings object
replaced by such a type after capture makes `Apply` return `Stale`. Detached
part-list requests do not capture settings and are unaffected.

The whole scope is validated and its bounds staged first; cancellation is checked
immediately before the install. Order changes through `ObservableList.Reorder`
semantics: same references, no `PartAdded`/`PartRemoved`, so drawing quantities,
sentinel plates and plate lists are untouched. Regenerated parts receive a fresh
owned copy of the replayed program and of the settings captured with the request,
keep their pose and lock, and are marked as having lead-ins. Fixed programs are
not replaced. An exception during install restores every plate exactly and returns
`Failed`. The installer itself is internal: it trusts these owned payloads and
checks only root program references, so `CuttingPlanService.Apply` is the only
public path. After the whole scope is installed, each changed plate raises
`Plate.PartsReordered` once; an observer exception is reported in `RefreshErrors`
on an `Applied` result, not as a rollback.

## Desktop workflow

`Plate > Plan Cutting...` plans the active plate and `Nest > Plan Cutting (All Plates)...`
plans every plate that has parts. Both open one dialog built on
`OpenNest.Engine.CuttingPlanning.CuttingPlanBatch`:

- The dialog starts from the plate's cutting settings (or the last-used settings) and plans
  at once. `Cutting Settings...` edits them and `Keep the current part order` fixes the
  whole-part order; either change replans. The settings are confirmed parameters: every
  unlocked part's lead-ins are regenerated, and locked parts keep programs that must
  already pass the checks.
- Every plate is captured on the UI thread and checked and planned on a worker. Clean part
  material is checked for overlaps with the pre-post overlap analyzer; overlapping parts or
  an incomplete check (see [pre-post verification](post-verification.md)) block that plate
  whatever its route. A free-order search that ends
  `NoSolutionWithinBudget` is retried once with the current part order, and the summary
  says the order was kept. Both are allowed 400 expansions per part (at least the
  default 20000), because both still plan contour order and entries for every part.
- The summary lists every plate: ready plates with part counts and rapid travel, others
  with their status and findings. Finding part numbers are the plate's current order, as the
  editor numbers them. The preview shows the active plate detached from the nest (quantity
  zero, so drawing quantities do not change) in the proposed order with the proposed
  programs. It is shown only for a ready plate that still matches what was planned: a
  refused plate may hold program graphs that are unsafe to copy, and a changed one would
  draw replayed programs at poses that were never checked.
- Apply is enabled only when every plate is ready, and it is all or nothing through
  `CuttingPlanService.Apply`. After it applies, each plate keeps its own copy of the
  confirmed settings, which also become the saved defaults. `Stale` keeps the dialog open
  and asks for a replan; nothing changes.
- Closing or cancelling while planning cancels the worker and keeps the dialog open until
  it stops. Planning and Apply refuse to start while a nesting or plate operation runs.
  The dialog plans with its own copy of the settings, and posts progress and results to
  the thread it was created on rather than to whichever context is current.
- `PlateView` follows `Plate.PartsReordered`: it redraws parts in the plate's order (the
  numbers it draws are the cutting order), rebuilds their graphics and marks the overlap
  check out of date.

## Remaining integration boundaries

The service does not establish clean-material non-overlap, scrap release by open
cutoff cuts or sheet edges, or physical retention strength. It does not write CNC
or set posting consent. A `Ready` proposal can still be unsuitable for cutting.

Later slices route the lead-in side panel's automatic assignment through the planner
and retire the legacy automatic paths. Windows interaction,
supplied-job coverage and actual posted order remain separate acceptance gates.
Fresh [pre-post verification](post-verification.md) is still required; it is not a
physical safety qualification.

## Portable regression gate

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter 'FullyQualifiedName~CuttingPlanning|FullyQualifiedName~PostVerificationAnalyzerTests'
```

Synthetic fixtures exercise whole-part routing and internal-hole crossing repair,
locked/ineligible refusal, actual native lead paths, shared subprograms, ownership,
entry/whole-part backtracking, deterministic budgets, cancellation and fresh replay
rejection. Retained emission characterizations cover styles, winding, corner rules,
circle rounding/clamping and tabs; unsupported cases remain explicit refusals.
