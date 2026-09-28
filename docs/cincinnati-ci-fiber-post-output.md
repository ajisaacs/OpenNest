# Cincinnati CI Fiber Post Output Reference

Project-written summary of `OpenNest.Posts.CincinnatiCIFiber`, not a vendor
manual or a machine-operation guide. For the separate CL-series post, see
[Cincinnati output](cincinnati-post-output.md).

Controller reference used during development: Beckhoff *TF5200 | TwinCAT 3 CNC
Programming manual*, version 1.33, May 19, 2026
(`TF5200_programming_manual_en.pdf`). Obtain the applicable documentation from
Beckhoff and Cincinnati. Vendor PDFs and full-text extracts stay outside source
control; redistribution permission has not been established.

## Output contract

The current [writer](../Posts/OpenNest.Posts.CincinnatiCIFiber/CIFiberProgramWriter.cs)
uses the Cincinnati machine-sample convention, not a generic TF5200 laser API:

- Header: nest/configuration/material comments; `V.E.MATERIAL`, `V.E.THICKNESS`,
  `V.E.X_SIZE`, `V.E.Y_SIZE`, and `V.E.UNIT`. Sheet weight is omitted by default.
- Startup: `G90`, `L PROGRAMSTART.NC`, then `P3=V.E.R3`, `$GOTO NP3:`, and `N0:`.
- Parts follow plate order. `V.E.R4` identifies the part within its sheet;
  numbered contour labels and `V.E.R3` continue across sheets for restart lookup.
- Each contour: `/L "L0"`, restart number, rapid to pierce, then interior
  `/L "L2"` with `G41` or exterior `/L "L4"` with `G42`; linear lead-in,
  `/L "L6"`, cutting moves, and `/L "ZHSOFF"`.
- Tail: `/L "L0"`, `L PROGRAMEND.NC`, `M50`, `M30`, and `%`.
- Motion endpoints are sheet-absolute XY. Arc `I`/`J` are offsets from the arc
  start, matching the G162 convention; the post does not explicitly emit G162.
- Sheet cut-offs post after every part on their sheet. Each segment is an open
  line with no lead-in: rapid to its start, `/L "L4"`, `/L "L6"`, the `G1`,
  and `/L "ZHSOFF"`. No `G41`/`G42` is selected, because the line is the beam
  centreline and has no inside or outside. Whether the `L4` macro runs
  correctly without a following lead-in move has not been confirmed on the
  machine.
- Hole subprogram geometry is inlined. Suppressed moves and, by default,
  wholly scribe contours are omitted.
- Files use UTF-8 without a BOM and CRLF lines. Default accuracy is three decimal
  places: coordinates trim trailing zeros; dimensional header values retain them.

## Configuration and boundaries

See [CIFiberPostConfig](../Posts/OpenNest.Posts.CincinnatiCIFiber/CIFiberPostConfig.cs)
for macro names, material mappings, unit codes, precision, and table limits.
The post is named for the machine family; table size belongs in configuration.

- Assign linear lead-ins before posting, including circular holes. The writer
  rejects missing or arc-first lead-ins. Its conservative rule cites the G238
  compensation-selection restriction in TF5200 §13.2.4.1; do not generalize it
  to every controller compensation mode.
- `/L` calls are skippable; `L PROGRAMSTART.NC` and `L PROGRAMEND.NC` are not.
  Macro bodies, process settings, restart handling, and compensation cancellation
  belong to the machine configuration. The post does not emit an explicit G40.
- `InchUnitCode` defaults to `1`; `MetricUnitCode` defaults to `0` but remains
  unconfirmed. Table limits compare directly with nest dimensions, without unit
  conversion. Do not assume changing the unit code establishes metric support.
- Multiple nonempty plates are written sequentially, but dimensional header
  values come only from the first plate and the pallet-change tail occurs once.
  This is not proof of a qualified multi-sheet machine cycle.
- Validation can throw after output has started. Discard any output from a failed
  post; it may be incomplete. Successful generation and tests do not establish
  that a program is safe to run on a particular machine.

## Verification

[Output-contract tests](../OpenNest.Tests/CincinnatiCIFiber/CIFiberPostProcessorTests.cs)
cover a square with a hole, coordinate transforms/formatting, lead-in rejection,
cut-off ordering and output,
table bounds, and suppression/scribe handling. Run:

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter FullyQualifiedName~CincinnatiCIFiber
```

Keep this summary aligned with the implementation and tests. Cite the relevant
manual section for controller rules and distinguish those rules from
Cincinnati-specific macros and behavior observed in a machine sample.
