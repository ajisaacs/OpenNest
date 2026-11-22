# Part number label box

A drawing can carry a label box: the rectangle where its part number and bend groups are
etched, found on flat material clear of everything else. The CAD Converter stores and shows the
placement; Plan Cutting etches the text when **Etch part label** is not Off (see
[Etching the label](#etching-the-label)).

In the CAD Converter, **Label Box...** (bottom bar) finds the best spot for the shown part.
The box is drawn as a dashed rectangle. Drag its body to move it and a corner to resize it
(the opposite corner stays put); the box keeps its angle. Once a box exists the button
offers **Find Best Location** again and **Remove**. When nothing fits, the button says so and
leaves any existing box alone.

## Contract

The box is an annotation, like a [dimension](dimensions.md): it never affects the program,
nesting geometry, cut time or posts, and it does not appear in DXF, DWG or SVG export.

- `Drawing.LabelBox` stores the center, the sides (`Width` along the text, `Height` across
  it) and the counterclockwise `Rotation` in radians, in the source frame, the frame of the
  converter's entities. Null when none is set.
- `LabelBoxFinder` (`OpenNest.Core/Labels`) is a pure, deterministic function of the part as
  it will be cut (visible, roles, breakoffs and tabbed cutouts applied, as the Dimensions
  code reads it), its bends and a `LabelBoxOptions`. The converter uses
  `LabelBoxOptions.Default(units)`: seven characters (a part number and a few bend group
  letters such as `1234-AB`) of a quarter inch (6 mm) stroke font, an eighth inch (3 mm)
  clear of anything else.
- Material is what nesting reads (`NestingMaterial.ForNesting`, marks excluded), so a tabbed
  cutout's slug counts as part and etch, scribe and break cuts do not. The material is
  eroded by the clearance with the same Clipper inward offset the pierce search uses, a band
  of the clearance is cut out either side of each bend line, and a breakoff's scrap is cut
  out grown by the clearance, because it is snapped off after forming.
- For each orientation tried, the largest empty rectangles of the rotated region
  (`MaximalRectangles`) are searched and the label-sized box is centered in the one with the
  most room around it. Orientations are the axes, each bend's direction and the longest
  straight edge's, plus the quarter turn of each; text is never turned past a quarter so it
  never reads upside down. A later orientation replaces an earlier one only with 5% more
  room, which favors horizontal text. Rectangles are staircased against slanted and curved
  edges, so they stay inside the clearance and may fall a little short of the true best.
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
| Part number and bend groups | the name, then a dash and the bend groups | `1234-AB` |

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

- **Text.** `PartLabels.Text` is the drawing name in capitals (characters the font lacks are
  dropped), then, in **Part number and bend groups** mode, a dash and the part's bend groups when it
  has bends: `1234-AB`. **Part number** mode leaves the groups out, so a part with bends still gets
  `1234`.
- **Bend groups.** A bend's group is its line's direction against the sheet grain in 15° steps
  (`BendGrain.Bucket`, bend line plus the part's rotation against `Plate.GrainAngle`), named by a
  fixed letter so a group means the same thing on every part and sheet: **A** across the grain,
  **B** with it, then **C** to **L** for the steps between, in bucket order from the grain. Bends
  with the same letter can be formed in one setup. The letters depend on how the part is
  rotated on the sheet, which is why the text is made when cutting is planned, not stored with the
  box.
- **Font.** `StrokeFont` is a small built-in single-stroke font (digits, capitals, `- _ . /`), so
  the label needs no font file. The CHR reader (`ChrFont`) loads vendor fonts from disk and is not
  used.
- **Fit.** The text is centered in the box and reads along it at the box's height. A name longer
  than the box is shrunk to fit, to half the box height at the least; a longer one is not etched.
  The text turns with the part, so it can read upside down or sideways on the sheet.
- **Frames.** The box and the bends are in the source frame: the program's offset
  (`Drawing.Source.Offset`) comes off, then the part's rotation applies, as `BendToWorld` does.
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
