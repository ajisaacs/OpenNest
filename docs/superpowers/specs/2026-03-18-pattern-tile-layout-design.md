# Pattern Tile Layout Window

## Summary

A standalone tool window for designing two-part tile patterns and previewing how they fill a plate. The user selects two drawings, arranges them into a unit cell by dragging, and sees the pattern tiled across a configurable plate. The unit cell compacts on release using the existing angle-based `Compactor.Push`. The tiled result can be applied to the current plate or a new plate.

## Window Layout

`PatternTileForm` is a non-MDI dialog opened from the main menu/toolbar. It receives a reference to the active `Nest` (for drawing list and plate creation). Horizontal `SplitContainer`:

- **Left panel (Unit Cell Editor):** A `PlateView` with `Plate.Size = (0, 0)` — no plate outline drawn. `Plate.Quantity = 0` to prevent `Drawing.Quantity.Nested` side-effects when parts are added/removed. Shows only the two parts. The user drags parts freely to position them relative to each other. Standard `PlateView` interactions (shift+scroll rotation, middle-click 90-degree rotation) are available. On mouse up after a drag, gravity compaction fires toward the combined center of gravity. Part spacing from the preview plate is used as the minimum gap during compaction.

- **Right panel (Tile Preview):** A read-only `PlateView` (`AllowSelect = false`, `AllowDrop = false`) with `Plate.Quantity = 0` (same isolation from quantity tracking). Shows the unit cell pattern tiled across a plate with a visible plate outline. Plate size is user-configurable, defaulting to the current nest's `PlateDefaults` size. Rebuilds on mouse up in the unit cell editor (not during drag).

- **Top control strip:** Two `ComboBox` dropdowns ("Drawing A", "Drawing B") populated from the active nest's `DrawingCollection`. Both may select the same drawing. Plate size inputs (length, width). An "Auto-Arrange" button. An "Apply" button.

## Drawing Selection & Unit Cell

When both dropdowns have a selection, two parts are created and placed side by side horizontally in the left `PlateView`, centered in the view. Selecting the same drawing for both is allowed.

When only one dropdown has a selection, a single part is shown in the unit cell editor. The tile preview tiles that single part across the plate (simple grid fill). The compaction step is skipped since there is only one part.

When neither dropdown has a selection, both panels are empty.

## Compaction on Mouse Up

On mouse up after a drag, each part is pushed individually toward the combined centroid of both parts:

1. Compute the centroid of the two parts' combined bounding box.
2. For each part, compute the angle from that part's bounding box center to the centroid.
3. Call the existing `Compactor.Push(List<Part>, List<Part>, Box, double, double angle)` overload for each part individually, treating the other part as the sole obstacle. Use a large synthetic work area (e.g., `new Box(-10000, -10000, 20000, 20000)`) since the unit cell editor has no real plate boundary — the work area just needs to not constrain the push.
4. The push uses part spacing from the preview plate as the minimum gap.

This avoids the zero-size plate `WorkArea()` issue and uses the existing angle-based push that already exists in `Compactor`.

## Auto-Arrange

A button that tries rotation combinations (0, 90, 180, 270 for each part — 16 combinations) and picks the pair arrangement with the tightest bounding box after compaction. The user can fine-tune from there.

## Tiling Algorithm

1. Compute the unit cell bounding box from the two parts' combined bounds.
2. Add half the part spacing as a margin on all sides of the cell, so adjacent cells have the correct spacing between parts at cell boundaries.
3. Calculate grid dimensions: `cols = floor(plateWorkAreaWidth / cellWidth)`, `rows = floor(plateWorkAreaHeight / cellHeight)`.
4. For each grid position `(col, row)`, clone the two parts offset by `(col * cellWidth, row * cellHeight)`.
5. Place all cloned parts on the preview plate.

Tiling recalculates only on mouse up in the unit cell editor, or when drawing selection or plate size changes.

## Apply to Plate

The "Apply" button opens a dialog with two choices:
- **Apply to current plate** — clears the current plate, then places the tiled parts onto it in `EditNestForm`.
- **Apply to new plate** — creates a new plate in the nest with the preview plate's size, then places the parts.

`PatternTileForm` returns a result object containing the list of parts and the target choice. The caller (`EditNestForm`) handles actual placement and quantity updates.

## Components

| Component | Project | Purpose |
|-----------|---------|---------|
| `PatternTileForm` | OpenNest (WinForms) | The dialog window with split layout, controls, and apply logic |
| Menu/toolbar integration | OpenNest (WinForms) | Entry point from `EditNestForm` toolbar |

Note: The angle-based `Compactor.Push(movingParts, obstacleParts, workArea, partSpacing, angle)` overload already exists in `OpenNest.Engine/Compactor.cs` — no engine changes are needed.
