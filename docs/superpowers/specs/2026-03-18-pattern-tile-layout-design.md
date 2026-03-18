# Pattern Tile Layout Window

## Summary

A standalone tool window for designing two-part tile patterns and previewing how they fill a plate. The user selects two drawings, arranges them into a unit cell by dragging, and sees the pattern tiled across a configurable plate in real time. The unit cell compacts on release using angle-based push. The tiled result can be applied to the current plate or a new plate.

## Window Layout

`PatternTileForm` is a non-MDI dialog opened from the main menu/toolbar. Horizontal `SplitContainer`:

- **Left panel (Unit Cell Editor):** A `PlateView` with `Plate.Size = (0, 0)` — no plate outline drawn. Shows only the two parts. The user drags parts freely to position them relative to each other. On mouse up after a drag, gravity compaction fires toward the combined center of gravity using the generalized angle-based `Compactor.Push`. Part spacing from the preview plate is used as the minimum gap during compaction.

- **Right panel (Tile Preview):** A read-only `PlateView` (`AllowSelect = false`, `AllowDrop = false`) showing the unit cell pattern tiled across a plate with a visible plate outline. Plate size is user-configurable. Rebuilds on mouse up in the unit cell editor (not during drag).

- **Top control strip:** Two `ComboBox` dropdowns ("Drawing A", "Drawing B") populated from the active nest's `DrawingCollection`. Plate size inputs (length, width). An "Auto-Arrange" button. An "Apply" button.

## Drawing Selection & Unit Cell

When both dropdowns have a selection, two parts are created and placed side by side horizontally in the left `PlateView`, centered in the view.

The user drags parts to arrange them. On mouse up:
1. Gravity compaction fires — each part is pushed toward the combined centroid using angle-based `Compactor.Push`, stopping when spacing is satisfied.
2. The tile preview on the right rebuilds.

## Auto-Arrange

A button that tries rotation combinations (0, 90, 180, 270 for each part — 16 combinations) and picks the pair arrangement with the tightest bounding box. The user can fine-tune from there.

## Generalized Compactor.Push

Refactor `Compactor.Push` to accept an arbitrary angle instead of only cardinal `PushDirection`. The push ray-casts at the given angle to find the closest obstacle, then slides the part along that vector.

Existing `PushDirection.Up/Down/Left/Right` callers continue to work by mapping to 0/90/180/270 degrees (or equivalent radians).

This is a prerequisite for the unit cell compaction behavior and benefits the rest of the codebase.

## Tiling Algorithm

1. Compute the unit cell bounding box from the two parts' combined bounds (including part spacing).
2. Calculate grid dimensions: `cols = floor(plateWidth / cellWidth)`, `rows = floor(plateHeight / cellHeight)`.
3. For each grid position `(col, row)`, clone the two parts offset by `(col * cellWidth, row * cellHeight)`.
4. Place all cloned parts on the preview plate.

Tiling recalculates only on mouse up in the unit cell editor, or when drawing selection or plate size changes.

## Apply to Plate

The "Apply" button opens a dialog with two choices:
- **Apply to current plate** — places the tiled parts onto the active plate in `EditNestForm`.
- **Apply to new plate** — creates a new plate in the nest with the preview plate's size, then places the parts.

The form returns the list of parts and the target choice to the caller (`EditNestForm`), which handles actual placement.

## Components

| Component | Project | Purpose |
|-----------|---------|---------|
| `PatternTileForm` | OpenNest (WinForms) | The dialog window with split layout, controls, and apply logic |
| `Compactor.Push(angle)` | OpenNest.Engine | Generalized angle-based push (refactor of existing cardinal-only push) |
| Menu/toolbar integration | OpenNest (WinForms) | Entry point from `MainForm` or `EditNestForm` toolbar |
