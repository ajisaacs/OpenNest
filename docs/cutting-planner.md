# Fixed-program cutting-route foundation

`OpenNest.Engine.CuttingPlanning.CuttingPlanService` currently plans a direct-XY
route through fixed, contiguous whole-part programs. This is the first foundation
of the unified cutting planner, not a replacement desktop command or an Apply API.
Existing desktop sequencing, lead assignment and per-attempt posting review are
unchanged.

## Calling the service

Create a `CuttingPlanRequest` from source placements and an explicit modeled start
point, then call `Capture` while their order, programs and poses are stable. Pass
the resulting `CuttingPlanSnapshot` to `Plan` on a worker. `Plan(request)` combines
both steps for synchronous callers. The default start is `Vector.Zero`; this is
not a discovered controller position. The diagnostic overload
`PostVerificationAnalyzer.Analyze(nest, startPoint)` uses the same modeled start.

Capture reads absolute/incremental instructions and shared subprogram calls into
owned immutable motion values. Placed rotations are already baked into programs;
placement translation is applied once. It does not clone recursive graphs, rebind
subcalls through rotating setters, or attach live drawings to preview plates.
Source `Part` references are identity handles only; worker planning never reads
their mutable state. A captured snapshot is deliberately historical, not a
freshness check against later edits.

Both locked and unlocked programs remain fixed. Search changes only the proposed
whole-part order and never edits source order, programs, settings, locks, poses or
quantity accounting. Branches are ranked by modeled rapid distance and source
ordinal, with bounded deterministic backtracking. The expansion budget defaults
to 20,000 attempted placements. Actual departure motions, including lead-outs and
subprograms, determine the next approach.

## Results and refusal

`Ready` means only that every captured placement occurs once and the full
fixed-program route was replayed with a fresh completed-contour checker without
rapid, missing-lead or incomplete-motion findings. `IndependentlyReplayed` records
that replay; it does not certify final NC or machine safety. Result ordinals and
finding identities refer to the original zero-based source list, not the proposed
sequence positions.

The checker uses the same native line/arc contact, contour completion and actual
gap semantics as pre-post diagnostics. Future contours are not yet obstacles;
completed holes in the same part are obstacles immediately. Stale tab settings
are not evidence of retention.

- `ConstraintConflict`: a fixed internal program or all explored whole-part orders
  violate the modeled route constraints. Reordering cannot repair a fixed rapid
  crossing its own completed hole.
- `UnsupportedGeometry`: unsupported motion semantics or incomplete retention
  checks. Cutoff dependencies and scribe-only drawings are outside this slice.
- `InvalidInput`: malformed/missing/duplicate placements, empty input, invalid
  geometry or a nonpositive budget.
- `NoSolutionWithinBudget`: search reached its bound, not proof that no route exists.
- `Cancelled`: capture or worker cancellation, with no live mutation.

Every non-ready result has no proposed order and no unsafe fallback.

## Limits and next hardening

This service does not check clean-material overlap, enclosing-hole/insert
release dependencies, contour coverage against clean geometry, lead paths through
other material, cutoff order, or physical retention strength. It does not generate
entries/leads, change internal contour order, install programs atomically, check
staleness at Apply, write CNC, or set posting consent. A `Ready` fixed-program route
can still be unsuitable for cutting. Do not apply it as a complete cutting plan.

Next slices must add explicit contour emission and lead validation, containment
and cutoff dependencies, exact freshness/atomic application, then desktop caller
migration and legacy retirement. Native Windows interaction, supplied-job routing
coverage and actual posted order remain separate acceptance gates. Fresh
[pre-post verification](post-verification.md) is still required at posting
boundaries, and it is not a physical safety qualification.

## Portable regression gate

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter 'FullyQualifiedName~CuttingPlanning|FullyQualifiedName~PostVerificationAnalyzerTests'
```

The synthetic three-part fixture starts with A,B,C crossing completed A; B,A,C
replays without findings. Controls cover a locked internal-hole crossing,
backtracking, native shared-hole motions, bounded refusal, cancellation, immutable
ownership, source-reference identity and complete-proposal replay rejection.
