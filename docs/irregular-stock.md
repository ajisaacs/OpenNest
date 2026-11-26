# Irregular stock sheets

A sheet need not be a rectangle: a remnant or drop, a sheet with a burned or damaged area, or
one already cut into can be nested on as it is. Such a sheet carries a `SheetOutline`
(`OpenNest.Core/SheetOutline.cs`): a closed polygon in plate coordinates (the sheet's
lower-left frame, as clamp zones) with optional obstacle polygons for areas inside it that must
stay empty. The plate's `Size` stays the outline's bounding box, so every rectangular code path
keeps working on the box; the outline narrows what may hold parts.

## Defining an outline

- **Import a CAD file.** `SheetOutline.FromGeometry` takes the file's entities: the largest
  closed contour (by bounding box) is the sheet and every other closed contour an obstacle;
  marks and open contours are ignored. Arcs are chorded at 0.001 model units on the side that
  takes material away (inside the outline, outside an obstacle), so the stored polygons never
  reach past the true edges; straight edges are kept exactly, so a sheet drawn 60 x 27 is
  60 x 27. The file's units are taken as the nest's, exactly as drawing import does.
  - Desktop: Plate > Edit Plate > **Outline...** reads a DXF/DWG; **Clear** makes the sheet a
    rectangle of its current size again. The size box is read-only while there is an outline.
  - Console: `--outline <dxf>` with `--autonest` makes the target plate that sheet.
- The constructor normalizes any polygon: counterclockwise, starting at its lowest-left vertex,
  translated so the bounds start at the origin. It refuses fewer than three distinct vertices,
  nonfinite coordinates, zero area and self-crossing polygons, and drops an obstacle wholly
  outside the outline.

Drawing an outline in the plate view and taking the leftover of a cut plate are not implemented.

## What honors it

| Area | Behavior |
|---|---|
| Irregular engine (and Default through it) | Fills the outline: the stock's usable material (outline less obstacles, eroded by the outline inset below) is the `FrontierPacker`'s `PackBoundary`, the same boundary the cutout packer uses, so every orientation's free region starts inside it |
| Rectangles, Circles, Raster, FlatBar, the fill strategies (Lattice, Strip, remnants) | Place nothing on an outlined sheet; Default then keeps Irregular's layout. `PlateFillService` throws `PlateFillService.Refusal(plate)` for an outlined plate; every caller checks it first: the desktop's Fill (Ctrl+F on a picked-up part) and Fill Area put the message in the status bar and add nothing, Pattern Tile offers a new plate only, the MCP fill/pack tools return it as an `Error:` result, and the console exits 2 with it unless `--autonest` is given |
| Layout check and the runner's candidate validation | A part's material must lie inside the usable area; a part in a missing corner or over an obstacle is a violation ("falls outside the outline ... or over one of its obstacles"). The spacing outline, which circumscribes arcs by up to 0.001, finds the candidates and the part's native contour confirms them (`SheetOutline.Holds` with `NativeContours.Region`: arcs circumscribed at 0.000025, clipped to the part's true bounds), as for part spacing, so a round part resting on an edge passes as a square one does. The runner calls the layout check's own test (`NestLayoutCheck.InsideOutline`), so the two accept the same poses. The area budget is the usable area |
| Edge spacing | The outline is eroded by the **largest** of the four edge spacings (`NestPlateStock.OutlineInset`), so no outline edge is closer to a part than the rectangle's own sides would be. The rectangular work area still applies and is never larger than that erosion |
| Push, clone push placement, settling | A pushed part stops at the edge of the usable area as it stops at the work-area edge: the unusable part of the work area (a missing corner, an obstacle, each grown by the outline inset) is a wall for `Compactor`'s plate pushes (`InteractiveOutline`). The part is measured by the perimeter of its clean material (no leads) with its arcs slid as arcs, so a round part rests on a wall exactly as it rests on the plate edge, where the edge review passes. A bounding-box push (Ctrl-click clone placement, the coarse pass of a settle) rests the box on the wall. A part already lying in a wall, deeper than the review's slack, is not held by it and can be pushed back out; with uneven edge spacing the walls join into one, so such a part is held by none of them |
| Nudge, drag, Expand Spacing, Fix Spacing | Do not stop at the outline: the layout check reports a part they leave outside it |
| Clamps | Unchanged: the rail is resolved for the bounding box, zones are keep-outs as before |
| Support bed | Unchanged: it is fixed to the machine and reports contacts under material |
| Area, utilization, cost | `Plate.Area` and `NestPlateStock.Area` are the material area (outline less obstacles); area-based cost prices that area and utilization divides by it |
| Salvage and remnants | No salvage credit and no `expectedRemnant`: no rectangular edge strip is guaranteed inside an outline |
| Manual cut-offs | A line is cut only where it crosses material: each crossing is a segment of its own, started at the material's edge and overtraveled past the far one, within the user's limits and the usual part clearance. The head-locked overcut applies only to a span that reaches the bounding box's far edge |
| Automatic cut-offs | Refused: `AutomaticCutOffPlanner` rejects an outlined plate ("Automatic cut-offs need a rectangular sheet") |
| AutoSize (Resize Plate to Fit Parts) | Leaves an outlined plate unchanged: the outline is a particular piece of material |
| Rotate 90/180 | Turn the outline with the sheet |
| Repeated layouts, clamp-removal recovery, stock matching | An outlined stock is never the same stock as its rectangle |

Setting `Plate.Outline` sets the plate's size to the outline's bounds; setting any other size
drops the outline. The Edit Plate dialog therefore applies the outline instead of a parsed size.

## File format and undo

The outline is saved per plate under an optional `outline` key (see
[nest file format](nest-file-format.md#sheet-outline)); files without it load rectangles as
before, and a malformed value loads the saved size as a rectangle with a reader warning. Undo
snapshots go through the same writer and reader, so a restore rebuilds the outline.

## Limits

- Only the Irregular engine nests on an outlined sheet. A job offering an outlined sheet and a
  rectangle to Default may well land on the rectangle: Default keeps the cheaper valid layout.
- Auto Nest's stock-options grid offers rectangles only; an outlined sheet is nested through
  the target plate (no options), or through `NestPlateStock` directly: the console's `--outline`,
  or API callers that set `NestPlateStock.Outline`. MCP has no outline input.
- The stored polygon is inside the true material by up to the chord tolerance (0.001) on
  imported arcs and exactly on straight edges. Outlines imported before straight edges were
  kept exactly sit 0.001 inside every edge (a 60 x 27 sheet reads 59.998 x 26.998); import the
  outline again to get the drawn size.
- The layout check and the runner judge an outline on Clipper's 0.0001 grid with a slack
  below it. A round part resting exactly on a slanted or rounded edge of the usable area can
  fail them by one grid unit, depending on where its confirming contour's vertices fall, and
  passes from 0.0001 clear; on an edge parallel to the sheet's box its true bounds decide, so
  it passes resting on it. The pre-post edge review allows 0.0005 and passes all of these.
