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
  marks and open contours are ignored. Arcs are chorded at 0.001 model units, then the outline is
  eroded and each obstacle inflated by that much, so the stored polygons never reach past the
  true edges. The file's units are taken as the nest's, exactly as drawing import does.
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
| Rectangles, Circles, FlatBar, the fill strategies (Fill, Strip, remnants) | Place nothing on an outlined sheet; Default then keeps Irregular's layout. `PlateFillService` throws `PlateFillService.Refusal(plate)` for an outlined plate; every caller checks it first: the desktop's Fill (Ctrl+F on a picked-up part) and Fill Area put the message in the status bar and add nothing, Pattern Tile offers a new plate only, the MCP fill/pack tools return it as an `Error:` result, and the console exits 2 with it unless `--autonest` is given |
| Layout check and the runner's candidate validation | A part's circumscribed material must lie inside the usable area, within the shared work-area slack; a part in a missing corner or over an obstacle is a violation ("falls outside the outline ... or over one of its obstacles"). The area budget is the usable area |
| Edge spacing | The outline is eroded by the **largest** of the four edge spacings (`NestPlateStock.OutlineInset`), so no outline edge is closer to a part than the rectangle's own sides would be. The rectangular work area still applies and is never larger than that erosion |
| Clamps | Unchanged: the rail is resolved for the bounding box, zones are keep-outs as before |
| Support bed | Unchanged: it is fixed to the machine and reports contacts under material |
| Area, utilization, cost | `Plate.Area` and `NestPlateStock.Area` are the material area (outline less obstacles); area-based cost prices that area and utilization divides by it |
| Salvage and remnants | No salvage credit and no `expectedRemnant`: no rectangular edge strip is guaranteed inside an outline |
| Manual cut-offs | A line is cut only where it crosses material: each crossing is a segment of its own, started at the material's edge and overtravelled past the far one, within the user's limits and the usual part clearance. The head-locked overcut applies only to a span that reaches the bounding box's far edge |
| Automatic cut-offs | Refused: `AutomaticCutOffPlanner` rejects an outlined plate ("Automatic cut-offs need a rectangular sheet") |
| AutoSize, Snap to Standard Size | Leave an outlined plate unchanged: the outline is a particular piece of material |
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
  the target plate (no options), or through `NestPlateStock` directly (console, MCP, API callers
  that pass an outline).
- The stored polygon is inside the true material by up to the chord tolerance (0.001) on
  imported arcs and exactly on straight edges.
