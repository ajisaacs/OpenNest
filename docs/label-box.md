# Part number label box

A drawing can carry a label box: the rectangle where its part number and bend groups are
etched, found on flat material clear of everything else. The CAD Converter stores and shows the
placement; Plan Cutting etches the text when **Etch part label** is not Off (see
[Etching the label](#etching-the-label)).

In the CAD Converter, **Label Box...** (bottom bar) finds the best spot for the shown part, for
the largest text that fits: half inch text if the part has room for it, so the shop can read it
from farther off, else three eighths, else a quarter inch (12, 9 or 6 mm). The box height is the
text height, so a box can also be resized by hand to change it.
The box is drawn as a dashed rectangle. Drag its body to move it, a corner to resize it
(the opposite corner stays put) and the round handle above its top side to turn it about its
center, in 5° steps or freely with Shift held. Once a box exists the button offers **Find Best
Location** again, **Rotate 90°**, **Make Horizontal** and **Remove**. However it is turned, the
angle is kept within a quarter turn either way of horizontal, so the text never reads upside
down (a half turn covers the same rectangle).

The box previews the label inside it as Plan Cutting would etch it, laid out by the same code
(`PartLabels.Layout`): the drawing name with its bend groups for the part unrotated on a sheet
whose grain runs along X (`PartLabels.PreviewText`), the longest the label gets there, so a box
that fits it also fits the part number alone. The text always reads along the box's width and
never turns by itself: a box made tall and narrow shrinks the text to fit its width, and when that
leaves it under half the box height, the box turns red, the shrunk text is drawn faintly and
**Too small to etch** shows under it, because Plan Cutting would etch nothing there. To run the
text up the part, turn the box first (**Rotate 90°**) and then resize it.

When even the quarter inch label fits nowhere, the button still places a quarter inch box for
hand placement and says so: centered on the roomiest flat spot a smaller label (three quarters,
half, then a quarter of its size) fits clear of everything, at that spot's angle, or in the middle of the
part's bounds when even the smallest does not fit. The box may then overlap edges or bend
lines until it is moved or resized.

## Contract

The box is an annotation, like a [dimension](dimensions.md): it never affects the program,
nesting geometry, cut time or posts, and it does not appear in DXF, DWG or SVG export.

- `Drawing.LabelBox` stores the center, the sides (`Width` along the text, `Height` across
  it) and the counterclockwise `Rotation` in radians, in the source frame, the frame of the
  converter's entities. Null when none is set.
- `LabelBoxFinder` (`OpenNest.Core/Labels`) is a pure, deterministic function of the part as
  it will be cut (visible, roles, breakoffs and tabbed cutouts applied, as the Dimensions
  code reads it), its bends and a `LabelBoxOptions`. The converter tries
  `LabelBoxOptions.Sizes(units)` largest first and keeps the first box found: eight characters
  (room for a four-digit part number and two circled bend group letters, `1234 AB`) of a half
  inch, three eighths and quarter inch (12, 9 and 6 mm) stroke font, each an eighth inch (3 mm)
  clear of anything else. Boxes placed before keep their size until found again.
- Material is what nesting reads (`NestingMaterial.ForNesting`, marks excluded), so a tabbed
  cutout's slug counts as part and etch, scribe and break cuts do not. The material is
  eroded by the clearance with the same Clipper inward offset the pierce search uses, a band
  of the clearance is cut out either side of each bend line, and a breakoff's scrap is cut
  out grown by the clearance, because it is snapped off after forming.
- For each orientation tried, the label gets the widest margin it can have on every side beyond
  the clearance: the largest m for which some empty rectangle of the rotated region
  (`MaximalRectangles`) holds the label grown by m all round, found by bisection. The box is
  centered in that rectangle, so it lands where it has the most room rather than halfway along
  a long, narrow strip (such as the gap between a hole and a slanted edge). Orientations are the
  axes, each bend's direction and the longest straight edge's, plus the quarter turn of each;
  text is never turned past a quarter so it never reads upside down. Horizontal is tried first.
  A later orientation replaces the best so far only with 5% more margin, and a turn off the axes
  replaces horizontal or vertical text only with half again its margin plus half the label
  height, so level text wins unless a slanted spot is clearly roomier. Rectangles are
  staircased against slanted and curved edges, so they stay inside the clearance and may fall a
  little short of the true best.
- No box when no orientation fits. The finder throws for a non-positive size or a negative
  clearance.
- Split pieces start with no label box, like dimensions, breakoffs and tabbed cutouts.

The box is placed when asked, not automatically on import.

File format: see [drawing label box](nest-file-format.md#drawing-label-box).

## Etching the label

Cutting Parameters > Marks > **Etch part label** (`CuttingParameters.LabelMode`, a `PartLabelMode`)
is **Off** by default and kept per machine thickness like the other cutting options
([machine cutting defaults](machine-cutting-defaults.md), saved with the nest and the machine's
thickness settings). It has three choices:

| Choice | Etched | Example |
|--------|--------|---------|
| Off | nothing | |
| Part number | the drawing name | `1234` |
| Part number and bend groups | the name, then each bend group letter in a circle | `1234 ⒶⒷ` |

When it is not Off, Plan Cutting adds each part's label to the clean program it
plans from, so the label is emitted as scribe (etch) moves like any other mark: it shows in the plan,
counts in cut time, posts through the etch path and never enters nesting or collision geometry
(marks are not material). Nothing changes while the option is off, and parts planned before it was
turned on keep no label until planned again. The plan fingerprint records the mode, so changing it
makes an earlier plan stale. While labels are on, so does changing a drawing's name, label box,
bends or source offset, or the plate's grain, between planning and Apply. Once applied, the etched
label stays as planned until the plate is planned again.

Settings saved before the mode existed held an on/off `etchPartLabels` value (in the nest's cutting
parameters, the desktop settings and a machine thickness's cutting settings). It meant the full
label, so `true` loads as **Part number and bend groups** and `false` or a missing value as **Off**;
the old key is read but never written again. When both keys are present, `labelMode` wins.
A `labelMode` number that names no choice (the reader accepts numbers) loads as if it were absent.

- **Text.** `PartLabels.Text` is the drawing name in capitals (characters the font lacks, and
  spaces, are dropped), then, in **Part number and bend groups** mode, a space
  (`PartLabels.GroupSeparator`) and the part's bend groups when it has bends: `1234 AB`. Layout
  etches each letter after the space inside its own circle, so a group never reads as part of the
  part number (a trailing `-A` looks like a revision). The circle is the text height across, the
  letter inside it 0.6 of that, three eighths of the text height clear of the name and an eighth
  apart (`PartLabels.Measure`). **Part number** mode leaves the groups out, so a part with bends
  still gets `1234`.
- **Bend groups.** A bend's group is its line's direction against the sheet grain in 15° steps
  (`BendGrain.Bucket`, bend line plus the part's rotation against `Plate.GrainAngle`), named by a
  fixed letter so a group means the same thing on every part and sheet: **A** across the grain,
  **B** with it, each within 5° (`BendGrain.AngleTolerance`), then **C** to **L** for the steps
  between, counterclockwise from the grain. A bend just past 5° goes to the neighboring step on its
  own side, never to A or B, and a bend 30° one way and one 30° the other way get different letters
  (D and K). A part whose bends fall in several groups lists each once, in alphabetical order.
  The letter does not say which way a part bends. It says how the grain runs against its bends,
  which changes springback and so the angle correction at the press brake: bends with the same
  letter take the same correction and can be formed together. The letters depend on how the part
  is rotated on the sheet, which is why the text is made when cutting is planned, not stored with
  the box.

  ![Bend group letters: A across the grain and B with it, each within 5°; C to G at 15° to 75°
  and H to L at 105° to 165°, counterclockwise from the grain](images/bend-groups.svg)

- **Font.** `StrokeFont` is a small built-in single-stroke font (digits, capitals, `- _ . /`), so
  the label needs no font file. Strokes are lines and arcs: the round digits (0, 2, 3, 5, 6, 8, 9)
  are drawn on arcs and etch as arc moves; the zero keeps its slash so it never reads as the letter
  O. The CHR reader (`ChrFont`) loads vendor fonts from disk and is not used.
- **Fit.** The text is centered in the box and reads along it at the box's height, circles
  included. A name longer than the box is shrunk to fit, to half the box height at the least; a longer one is not etched.
  The text turns with the part, so it can read upside down or sideways on the sheet.
- **Frames.** The box and the bends are in the source frame: the program's offset
  (`Drawing.Source.Offset`) comes off, then the part's rotation applies, as `BendToWorld` does.
  Imported drawings are incremental programs, so the strokes (and arc centers) are written as steps
  from where the program stops, not as absolute points.
- **Etch order.** A part's marks (label strokes and bend etches alike) are etched before its cuts in
  one short tour: tried from every mark, each open mark from either end, ending as near as it can
  to where the part's first cut starts. Bend etches on one side of a part are done together before
  the head crosses to the other side (`ContourCuttingStrategy.SequenceScribes`).
- **Reopening.** The label lives only in the applied part programs. Loading a nest never takes it
  back into the drawing as a lost etch mark (`DrawingMarkRecovery` skips drawings with a label box),
  so it is not etched twice and turning the option off removes it at the next plan.
- **What gets none.** A drawing with no box, parts whose cutting is locked or left out of the plan
  (the label is added only where Plan Cutting regenerates the program), and common-line members
  (their programs are rebuilt from their shared spans).

Not done yet: a plan-time warning for a label that cannot fit (it is skipped without a message),
a group letter next to each bend's own etch dashes, and a parts-per-bend-group list in the part
printout and nest report.

## Regression checks

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter "FullyQualifiedName~Labels"
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter "FullyQualifiedName~PartLabelTests"
scripts/Test-Hidden.ps1 OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj --filter FullyQualifiedName~CadConverterLabelBoxTests
```
