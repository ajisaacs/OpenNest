# Dimensions

A drawing can carry dimensions, shown in the CAD Converter:

- Overall dimensions: the part's size along X and along Y. In the **Dimensions** panel,
  **Add Overall Dimensions** adds whichever of the two is missing, below and to the right of
  the part. Drag a dimension line to move it, across the part to its other side if wanted.
- Radius and diameter dimensions on circles and arcs. Select one or more circles or arcs,
  right-click and choose **Add Radius Dimension** or **Add Diameter Dimension**. Each curve
  gets at most one of each. Drag the leader to swing the label around the curve and move it
  in or out; a radius on an arc stays on the arc's sweep.

**Remove** in the Dimensions panel deletes the selected dimension.

## Contract

Dimensions are annotations. They never affect the program, nesting, cut time or posts, and
they do not appear on plates, in PDFs or in DXF export.

- `Drawing.Dimensions` stores only placement, so a value cannot go stale:
  - Overall: the kind and the signed offset from the part's extents. Positive is above
    (horizontal) or right (vertical).
  - Radius and diameter: the kind, the measured curve's entity ID, the leader angle from the
    curve's center (radians) and the signed offset from the curve to the label along the
    leader, positive outside.
  - Zero is never a valid offset.
- `DimensionBuilder` (`OpenNest.Core/Dimensions`) measures overall dimensions from the
  entities as they will be cut: visible, with roles, breakoffs and tabbed cutouts applied, as
  the Program tab builds them. `FileListItem.Bounds` is not used because it includes hidden
  layers.
- Marks do not count toward overall dimensions: anything `ScribeLayerRepair.IsMarkLayer`
  matches (scribe, etch and engrave, by layer name) and anything `SpecialLayers.IsMaterial`
  rejects (rapids, break cuts). `IsMaterial` compares layer objects, so it alone would count
  source marks on their own layers. Tabs and a breakoff's scrap count: both are material of
  the part as cut.
- Radius and diameter find their curve by ID among the visible entities with roles applied,
  before breakoffs and tabbed cutouts, which split a curve into new pieces. A cut or etched
  curve can be dimensioned; an excluded, hidden or display-only one is not shown. Adding one
  to a display-only curve explains that it must be set to Cut or Etch first. An arc that is
  a full circle is measured as a circle.
- New radius and diameter labels sit outside the curve by half its radius, or a twentieth of
  the part's larger size when that is more. On a circle the radius leader points up and right
  and the diameter leader up and left, so the two do not overlap; on an arc both point
  through the middle of its sweep. Dragging keeps the label a few pixels clear of both the
  curve and the center; a curve too small for that keeps the label outside.
- A dimension with nothing to measure, a part with no size along its axis, or a curve that
  is gone or no longer cut is listed as not shown and keeps its placement until the geometry
  returns.
- Values show three decimals with no unit, in the drawing's units; radius labels are drawn
  with an `R` prefix and diameter labels with `Ø`.
- Split pieces start with no dimensions, like breakoffs and tabbed cutouts.

## Hole table

**Holes...** in the CAD Converter's bottom bar opens a small Hole Table window beside the
view. It lists the drawing's round holes by diameter, smallest first, with the quantity of
each and a color swatch. While it is open, every hole on the view is drawn in its size's color
with a heavier line; closing the window restores the normal colors. The table follows the
selected file and updates when entities are cut, etched, set to display, excluded or
simplified.

- A round hole is a cut circle, or a closed contour made only of arcs on one center and
  radius, that lies inside another cut contour. The outline of a round part is not a hole.
  Slots and other shapes are not listed.
- Holes are read from the entities as they will be cut (visible, with roles applied), before
  breakoffs and tabbed cutouts. Etched, display-only, excluded and hidden circles are not
  listed.
- Diameters are rounded to four decimals, in the drawing's units; holes that agree to four
  decimals share a row. `HoleTable` (`OpenNest.Core/Dimensions`) builds the rows.
- The table is a view only: it stores nothing in the drawing and never affects the program.

File format: see [drawing dimensions](nest-file-format.md#drawing-dimensions).
