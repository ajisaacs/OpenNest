# Cincinnati Post Output Reference

Reference for the G-code structure emitted by `OpenNest.Posts.Cincinnati`.
Every code listed here maps to a section in the Cincinnati Laser Programming
Manual (EM-423 R-02/11). Section numbers in parentheses (e.g. `§1.52`)
refer to that edition. Obtain the applicable manual from Cincinnati; vendor
manuals are not bundled because redistribution permission has not been established.

This is a project-written implementation reference, not a replacement for the
machine's programming, operating, or safety documentation. Keep vendor PDFs and
full-text extracts outside source control; do not re-add them without permission.
For the separate CI Fiber post, see [CI Fiber output](cincinnati-ci-fiber-post-output.md).

If you add a new emission in the post, either cite the manual section it maps
to, or flag it here as a known custom extension. "Custom code" in this project
means something that is not documented in the manual but that the Cincinnati
control is known to accept — none exist today and we should not introduce any
without confirming the control behavior.

## Overall file structure

A generated file contains, in order:

1. **Main program** (`CincinnatiPreambleWriter.WriteMainProgram`)
   Preamble, unit/mode setup, initial library, variable-declaration call, one
   `M98 P<sheetSubNum>` call per plate quantity, and `M30` to end.

2. **Variable declaration sub-program** (`CincinnatiPreambleWriter.WriteVariableDeclaration`)
   Machine variables (`#number = value`) used across the nest, terminated
   with `M99`.

3. **Sheet sub-programs** (`CincinnatiSheetWriter.Write`), one per unique plate
   layout. A sheet sub-program contains the cutting sequence for a whole
   plate, either with features inlined or with `M98` calls into part
   sub-programs. Parts, cut-offs included, post in plate order, which is the
   cut sequence set by manual, cut-first or automatic sequencing; the post
   never reorders them.

4. **Part sub-programs** (`CincinnatiPartSubprogramWriter.Write`), one per
   unique posted part toolpath, only emitted when `Config.UsePartSubprograms`
   is enabled. Parts share a sub-program only when they have the same drawing
   name and their normalized programs (lead-ins, hole calls and suppression
   included) post identically, so parts of one drawing and rotation with
   different lead-ins get their own sub-programs.

5. **Hole sub-programs** (`CincinnatiPartSubprogramWriter.Write` reused with a
   `"HOLE"` label), one per unique posted hole toolpath. Calls are matched to
   sub-programs by their program reference, never by call id: lead-in planning
   numbers holes 1, 2, ... within each drawing, so ids collide across drawings.
   Hole programs are stored incremental; the post writes absolute-mode copies
   (everything posts under `G90`) and leaves the nest's programs unchanged.

Sub-program bodies start with a `:<subNum>` label and end with `M99`.

The post reads part programs through absolute-mode copies and never changes the
nest: an incremental part program stays incremental after posting.

### Sheet header

Each sheet sub-program re-anchors the work frame with `G92 X#5021 Y#5022`
(§1.92; `#5021`/`#5022` are the machine X/Y system variables), loads the cut
library with `G89 P<library>` (§2.89) when one resolved, and raises the head
with `M47`. When `Config.UseLineNumbers` is on it then posts
`GOTO<FeatureLineNumberStart>( Goto Feature )` (§5.5), which targets the
sheet's first feature line; the operator edits it to restart at another
feature. Without line numbers there is no target, so no `GOTO` is posted.

### Suppressed moves

Moves with `Motion.Suppressed` set are not cut, as in the CI Fiber post and the
cut simulation. When cutting resumes after a suppressed run, the post ends the
feature and starts a new one with a rapid to the run's end point, so the gap is
crossed with the beam off. Rapids left with nothing to cut, and hole calls whose
moves are all suppressed, are dropped.

### Cut library selection

`MaterialLibraryResolver` picks the first `MaterialLibraries` entry matching the
nest material, thickness within 0.001 and assist gas (`Nest.AssistGas`, else
`Config.DefaultAssistGas`). Before the configuration dialog opens,
`PrepareForNest` pre-selects that same entry as `SelectedLibrary`, or clears the
selection when nothing matches; it never substitutes another gas or thickness.
A `SelectedLibrary` the operator then picks overrides auto-resolution.

## Feature blocks

A "feature" is a single contour: lead-in → cut moves → lead-out. Each feature
block in a sheet or sub-program output follows this order
(`CincinnatiFeatureWriter.Write`):

1. `G0 X_ Y_` — rapid to the pierce point (§1.00).
2. Optional part-name comment, only on the first feature of each part.
3. `G89 P<library>` — load process parameters (§2.89). `P` is a library file
   name; the `(...)` trailing comment carries speed-class info. With no
   resolved library the post writes `(WARNING: No library found)` instead.
4. `G84` (cut) or `G85` (etch / no-pierce) — pierce and start cut, or start
   cut without pierce (§2.84 / §2.85).
5. `M130 (ANTI DIVE OFF)` — disable anti-dive, only if configured (§3.130).
6. Contour moves:
   - `G41` (left) or `G42` (right) kerf compensation on the first cut move
     (§1.41 / §1.42), suppressed for etch features.
   - `G1 X_ Y_ [F<feedvar>]` — linear cut move (§1.01). Feedrate references a
     machine variable such as `#148` and is emitted only when it changes.
   - `G2 X_ Y_ I_ J_ [F<feedvar>]` (CW) or `G3` (CCW) — arc (§1.02 / §1.03).
     `I`/`J` are incremental offsets from the current position to the center.
7. `G40` — cancel kerf compensation (§1.40), only if it was applied.
8. `M35` (or `M135` if SpeedGas is enabled) — beam off (§3.35 / §3.135).
9. `M131 (ANTI DIVE ON)` — re-enable anti-dive (§3.131).
10. Head raise before the rapid to the next feature, unless this is the last
    feature on the sheet (§3.47). See [Head raises](#head-raises-m47).

## Head raises (M47)

Every raise is written at the end of the feature it follows, before the rapid
that leaves it, and there is exactly one raise per rapid. The block is chosen
in this order (`CincinnatiHeadRaise`):

1. `M47 P<SafetyHeadraiseDistance> (Safety Headraise)` when the next feature
   belongs to a part with a different drawing name and the distance is set.
2. Otherwise the M47 mode for the feature just cut: `ExteriorM47` after a
   part's exterior, `InteriorM47` after anything else.
   - `Always`: `M47`.
   - `BlockDelete`: `/M47`; the operator can skip it with block delete (§5.6).
   - `Auto`: `M47` when the rapid travel to the next pierce exceeds
     `M47OverrideDistanceThreshold` (program units), `/M47` when it does not.
     With no threshold, `Auto` always posts `M47`.
   - `None`: no raise.

The exterior is the cut feature whose cut moves (lead-in and lead-out moves
excluded) span the extents of all of the part's cut moves; etch features and
hole calls are never exterior, and a part whose outline is cut in several
features has no exterior. The raise after a part sub-program call follows
`ExteriorM47`. These rules are this project's interpretation of the settings,
not vendor-documented behavior.

Hole calls (see below) write `M47`, or the safety raise, after `G52 X0 Y0`
regardless of the M47 mode.

## Sub-program call patterns

There are two distinct call-site patterns, depending on whether the call
targets a whole-part sub-program or a hole sub-program.

### Part sub-program call (`WriteSubprogramCall`)

Used when `Config.UsePartSubprograms` is enabled. The tool physically rapids
to the corner of the part's toolpath, G92 sets that position as the local
origin, and the sub-program executes in its own local coordinate frame. After
it returns, the tool is wherever the sub-program's last feature ended, so the
head is raised, the tool rapids back to local `0,0` (the corner) and G92 gives
that point its sheet coordinates again.

```
G0 X<left> Y<bottom>          ; rapid to the toolpath corner (§1.00)
( PART: <name> )
G92 X0 Y0                     ; set local origin at current position (§1.92)
M98 P<partSubNum> (<name>)    ; call the part sub-program (§3.98)
M47                           ; head raise for the rapids that follow (§3.47)
G0 X0 Y0                      ; back to the corner (§1.00)
G92 X<left> Y<bottom>         ; restore the sheet coordinate system (§1.92)
```

The last part on a sheet stops after `M98`: nothing moves before the next
sheet re-anchors with `G92 X#5021 Y#5022`. The sub-program's last feature
posts no raise, so the raise after the call is the only one before the rapids,
chosen by the [head raise](#head-raises-m47) rules.

This pattern uses G92 rather than G52 because hole calls inside a part
sub-program use G52 themselves, and a nested G52 would replace the part shift
instead of adding to it.

### Hole sub-program call (`CincinnatiHoleCallWriter.Write`)

Used for the `SubProgramCall` codes that a `ContourCuttingStrategy` emits for
each circular hole. Unlike parts, we do **not** want a physical rapid to the
hole center before calling — the sub-program's first rapid is the lead-in to
the pierce point, and the machine should travel directly from the previous
feature's end to that pierce.

```
G52 X<hole.x> Y<hole.y>       ; shift local origin to hole center (§1.52)
( PART: <name> )              ; only when the hole is the part's first feature
M98 P<holeSubNum>             ; call the shared hole sub-program (§3.98)
G52 X0 Y0                     ; restore the original coordinate system (§1.52)
M47                           ; head raise unless this is the last feature (§3.47)
```

`<hole>` is the hole center in the calling program's frame: the part location
plus the call offset in a sheet sub-program, or the call offset alone inside a
part sub-program (whose frame is already the part's). A part that opens with a
hole call takes its safety head raise on that call's `M47`, as an ordinary
first feature would.

G52 specifies the new origin in the current work coordinate system and — per
§1.52 — "does not move the cutting nozzle". The hole sub-program is written
in hole-local coordinates (origin at the hole center, produced by
`ContourCuttingStrategy`), so its first `G0 X_ Y_` resolves to `hole + local`
in absolute terms. That is the first physical motion, and it takes the tool
straight from wherever it was to the lead-in pierce point. G52 X0 Y0 cancels
the shift after `M99` returns control.

## G-code reference

These are every G/M code the post emits, grouped by category. Anything here is
documented in the programming manual. Anything not here should be audited the
next time the post is edited.

### Motion modes and contouring

| Code | Description | Manual |
| --- | --- | --- |
| `G0 X_ Y_` | Rapid traverse | §1.00 |
| `G1 X_ Y_ F_` | Linear feedrate move | §1.01 |
| `G2 X_ Y_ I_ J_ F_` | Clockwise arc | §1.02 |
| `G3 X_ Y_ I_ J_ F_` | Counter-clockwise arc | §1.03 |

### Units and coordinate mode

| Code | Description | Manual |
| --- | --- | --- |
| `G20` | Inch mode | §1.20 |
| `G21` | Metric mode | §1.21 |
| `G90` | Absolute mode | §1.90 |

### Kerf compensation

| Code | Description | Manual |
| --- | --- | --- |
| `G40` | Cancel kerf compensation | §1.40 |
| `G41` | Kerf compensation, left side | §1.41 |
| `G42` | Kerf compensation, right side | §1.42 |

### Work coordinate systems

| Code | Description | Manual |
| --- | --- | --- |
| `G52 X_ Y_` | Temporary local work coordinate offset. Does not move the tool. `G52 X0 Y0` cancels. | §1.52 |
| `G92 X_ Y_` | Sets the current tool position to `(X, Y)` in the work coordinate system, implicitly redefining the WCS origin. | §1.92 |

### Exact stop

| Code | Description | Manual |
| --- | --- | --- |
| `G61` | Exact stop mode | §1.61 |

### Cutting operations (custom Cincinnati G-codes)

| Code | Description | Manual |
| --- | --- | --- |
| `G84` | Pierce and start cut | §2.84 |
| `G85` | Start cut without pierce (used for etch) | §2.85 |
| `G89 P<file>` | Load process parameters from a library file | §2.89 |
| `G121` | Enable non-stop cutting (Smart Rapids) | §2.121 |

### Program flow

| Code | Description | Manual |
| --- | --- | --- |
| `M30` | End of main program with rewind | §3.30 |
| `M98 P_` | Sub-program call. **Takes only `P` and `L` — not `X`/`Y`.** | §3.98 |
| `M99` | Return from sub-program | §3.99 |

### Machine state

| Code | Description | Manual |
| --- | --- | --- |
| `M35` | Beam off | §3.35 |
| `M42` | Retract Z-axis | §3.42 |
| `M47 [P<dist>]` | Raise Z-axis, optionally by a distance | §3.47 |
| `M50` | Switch pallets | §3.50 |
| `M130` | Anti-dive off | §3.130 |
| `M131` | Anti-dive on | §3.131 |
| `M135` | Discharge current off (keeps assist gas on) | §3.135 |

### Comments, labels, and block delete

| Syntax | Description | Manual |
| --- | --- | --- |
| `(text)` | Inline comment | §5.4 |
| `:<number>` | Sub-program label | §3.98 |
| `/<block>` | Block delete — operator can toggle the line off | §5.6 |
| `N<number>` | Line number, used by M99 P / GOTO targets | §5.5 |

## System variables referenced

| Variable | Description | Manual |
| --- | --- | --- |
| `#148` | Default cut feedrate variable (used in `F#148`) | §2.89 |
| `#5021` | Current machine X position | §6 (table of system variables) |
| `#5022` | Current machine Y position | §6 (table of system variables) |

Project-defined variables start at `Config.SheetWidthVariable` /
`Config.SheetLengthVariable` and at `Config.UserVariableStart`. Those ranges
are documented in `CincinnatiPostConfig.cs`.
