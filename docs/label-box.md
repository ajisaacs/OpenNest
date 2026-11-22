# Part number label box

A drawing can carry a label box: the rectangle where its part number will be etched, found
on flat material clear of everything else. This stage stores and shows the placement only;
nothing is etched yet.

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

Out of scope for now: etching the text, the bend group letters, and an automatic placement
on import; the box is placed when asked.

File format: see [drawing label box](nest-file-format.md#drawing-label-box).

## Regression checks

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter "FullyQualifiedName~Labels"
scripts/Test-Hidden.ps1 OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj --filter FullyQualifiedName~CadConverterLabelBoxTests
```
