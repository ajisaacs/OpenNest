# Unified cutting planner: direct-XY proposals

`OpenNest.Engine.CuttingPlanning.CuttingPlanService` plans contiguous whole-part
programs. It can retain fixed programs or jointly choose internal contour order,
entries and whole-part order using explicitly confirmed cutting parameters. It
returns an owned proposal; `Apply` installs Ready plate-scoped proposals atomically
after an exact freshness check. There is no desktop command yet: existing desktop
sequencing, assignment and posting review are unchanged.

## Capture before worker planning

Create a `CuttingPlanRequest`, then call `Capture` while source placements,
programs and settings are stable. Pass that snapshot to `Plan` on a worker;
`Plan(request)` combines these steps synchronously. The modeled starting point
defaults to `Vector.Zero`, not a discovered controller position.

```csharp
var request = new CuttingPlanRequest(plate, startPoint: start,
    confirmedParameters: parameters, expansionBudget: 20000,
    maxEntries: 16, preservePartOrder: false);
var snapshot = CuttingPlanService.Capture(request, cancellationToken);
var result = CuttingPlanService.Plan(snapshot, cancellationToken);
```

- A plate-scoped request plans the plate's current parts and records its exact
  state for `Apply`. A detached part list (`new CuttingPlanRequest(parts, ...)`)
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

## Search and exact output

With regeneration, the bounded deterministic search considers whole-part order,
internal contour order and native entry candidates together. Internal contours
precede their own perimeter; parts remain contiguous. Backtracking can revisit
an earlier entry when a later part cannot be reached safely.

Candidates use native closest points, vertices, midpoints and circle angles in
stable order, capped by `maxEntries`. Circle rounding, clamping, corner resolution
and tab trimming happen during emission. Validation uses the actual emitted
motions, never the nominal entry point alone. Existing lead styles are not
shortened, disabled or substituted as a search fallback.

Every candidate rapid is checked against contours already completed, including
earlier holes in the same part. Future contours are not yet obstacles. Actual
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
  check. Open nominal outlines, ambiguous release states, cutoff dependencies and
  scribe-only source drawings are not silently accepted.
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
part list instance and order, plate quantity/size/quadrant, cutoff definitions,
and for every part its program reference and exact content (an in-place edit
counts), drawing program, pose bits, lead-in/lock flags, settings reference and
bounds. Any difference on any plate returns `Stale` and changes nothing; a result
can therefore be applied at most once.

The whole scope is validated and its bounds staged first; cancellation is checked
immediately before the install. Order changes through `ObservableList.Reorder`
semantics: same references, no `PartAdded`/`PartRemoved`, so drawing quantities,
sentinel plates and plate lists are untouched. Regenerated parts receive a fresh
owned copy of the replayed program and of the settings captured with the request,
keep their pose and lock, and are marked as having lead-ins. Fixed programs are
not replaced. An exception during install restores every plate exactly and returns
`Failed`. After the whole scope is installed, each changed plate raises
`Plate.PartsReordered` once; an observer exception is reported in `RefreshErrors`
on an `Applied` result, not as a rollback.

## Remaining integration boundaries

The service does not establish clean-material non-overlap, inner-part-before-host
release dependencies, cutoff order or physical retention strength. It does not
write CNC or set posting consent. A `Ready` proposal can still be unsuitable for
cutting.

Later slices add containment/cutoff dependencies, then desktop integration
(including `PartsReordered` refresh hooks) and legacy automatic-path retirement. Windows interaction,
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
