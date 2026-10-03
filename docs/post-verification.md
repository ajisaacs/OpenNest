# Verify a nest before posting

The desktop's **Nest > Post > (post processor)** workflow checks every plate after
post settings are accepted and before asking where to save CNC output. The review
shows all three check categories, even when there are no findings:

- **Overlapping parts:** shared material between placed parts, including containment.
  Holes are subtracted; placing a part inside a sufficiently large cutout is not
  itself an overlap. This uses clean drawing geometry, not added lead-in/out paths.
- **Missing lead-ins:** cutting contours without an actual, nonzero lead-in in the
  placed program. Applying a lead-in to only one contour does not clear the other
  contours. Scribe-only work and scrap cutoff lines do not require lead-ins here.
- **Rapid crossings:** direct XY moves crossing closed, already-cut, untabbed
  contours in cutting order. This includes earlier holes within the same part and
  previously cut parts. A future cut is not an obstacle yet. Actual uncut gaps are
  respected; enabling tabs in settings alone is not evidence that a gap exists.

Findings identify the plate and part's position in the cutting sequence. Read the
scrollable report, cancel to fix the nest, then post again to rerun verification.
Uncheckable geometry is reported as incomplete, not passed. Multiple interrupted
cut fragments, or a lead-out after an open contour that might cut through a tab,
also require manual review rather than being assumed retained.

## Explicit override

With any warning, **Post Anyway** is disabled until the user selects:

> I understand the listed risks, including possible head crashes and machine or
> material damage. I choose to bypass these warnings and continue posting this nest.

Consent applies only to that posting attempt. Unchecking disables posting again;
opening another review always starts unchecked. Cancel, Escape, and the window's
close button do not write CNC files. Cancel is the default button. A clear report
still requires **Continue Posting**. An unexpected verification failure blocks
posting rather than allowing an override of an absent report.

The editor is modal during verification and posting dialogs. Posting is refused
while auto-nesting, interactive fill, or a busy plate action can still change the
nest. Cancellation keeps the modal dialog open until its worker has stopped.
No automatic repair, resequencing, lead-in insertion, or tab insertion is performed.

## Console

`--post` prints the same report. Warnings prevent CNC output (including overwriting
an existing file) and return exit code 1. After reviewing them, explicitly pass
`--acknowledge-post-risks` to accept the risks for that invocation. The switch does
not skip verification and is not persisted. When `--post` is requested, verification
must pass or be acknowledged before either nest saving or CNC writing. Nest-save
and CNC destinations must differ; collisions are refused even with acknowledgment.
Without `--post`, ordinary nest saving is unchanged; `--no-save` skips that save.

The raw `IPostProcessor` plugin interface remains a low-level writer. Third-party
callers must apply their own review/consent flow using `PostVerificationAnalyzer`;
the gate is installed at OpenNest's desktop and console posting entry points.

## Selected post and cut order

Cincinnati CI Fiber preserves the placed part/contour order and pierce positions
used by this check. The older Cincinnati CL post sorts parts and moves cutoffs;
Gravograph builds and optimizes separate tool passes. Those two posts, and unknown
plugins, therefore show an additional **incomplete final rapid sequence** warning
requiring acknowledgment even if the nest-level checks have no findings. This
change does not silently alter their existing CNC output order.

## Limits and maintainer contract

These checks are warnings, not a machine safety certification. They inspect the
nest's placed programs and direct XY travel, not a simulation of final NC output.
A post processor or controller may change routing, cutting order, lead-ins,
retract height, or parking moves. The checks do not model tipping, slats, clamps,
actual tab strength, kerf, or safe Z height. Review the generated CNC program and
machine setup before cutting, even when this report has no warnings.

`OpenNest.Core/Diagnostics/PostVerificationAnalyzer` owns the cross-platform logic;
its report exposes findings, display text, and `CanPost(risksAcknowledged)`. Use
`AnalyzeForPost` at posting entry points so unknown post ordering is not silently
certified. `IPostVerificationSupport.PreservesPlacedProgramOrder` is an opt-in
contract, not a machine-safety claim; only declare it after verifying the writer's
part order, contour order, and pierce positions. The WinForms dialog renders the
report and enforces per-attempt consent. New callers
must keep inputs stable until analysis completes, honor cancellation, and never
treat incomplete analysis as a clear result. Keep analysis out of paint handlers.
The overlap diagnostic's documented numeric/tessellation limits also apply.

Future hardening should inspect a shared post-specific emitted motion stream,
including controller retracts and final parking, rather than pretending direct XY
geometry proves physical head clearance. The [fixed-program route foundation](cutting-planner.md)
reuses these motion/completion checks but is not yet a desktop planner or an Apply
API; its route-only `Ready` result does not replace fresh posting review.

## Verification on Windows

The core/console tests run cross-platform. Dialog tests and actual desktop behavior
require Windows; Linux cross-compilation is not runtime verification.

1. Run `dotnet test OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj`.
2. Post a nest containing two overlapping parts and a part without lead-ins.
   Verify the report lists the plates/parts, shows all check categories, and disables
   **Post Anyway** until consent is checked. Uncheck it to verify disabling again.
3. Cancel with the button, Escape, and X on separate attempts; none should open the
   output save dialog. Reopen the review and confirm consent is reset.
4. Use a sequence that cuts a closed untabbed part, moves to its far side, then
   rapids back across it. Verify a crossing warning. Change the sequence so the
   perimeter has not yet been cut, then assign a real tab gap and recheck the
   appropriate control cases. Include a hole crossed later in the same part.
5. Acknowledge the warnings and post through both a single-file and multi-file post.
   Confirm output and normal overwrite prompts. Post a clear, leaded nest with Cincinnati CI Fiber and
   verify the report still appears without requiring a risk override.
6. During a large verification, cancel; the editor must stay disabled until the
   worker exits, with no output. A fill still finishing after its progress dialog
   closes must prevent posting until it has completed.

For failures, record the menu action, nest fixture, selected post, visible report,
and exact exception text.
