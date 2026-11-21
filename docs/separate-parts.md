# Separating parts

A file can draw several parts, such as a sheet of ornaments. Import still makes one item of it, whose program would cut every part as one drawing. The CAD Converter offers to separate it.

## Finding the parts

`PartBodies.Find` (Core) takes the item's outlines: shown entities that cut (`EntityRoles.Of` is `Cut`) on material layers. Closed contours are sorted by what holds what:

- A contour no other contour holds starts a part. The contours directly inside it are its holes, and a contour inside one of those holes starts another part (a part in a hole is never treated as a hole).
- One contour holds another when its bounds hold the other's, it is larger, and its polygon holds most of a sample of the other's vertices, so a vertex on a shared edge cannot decide alone.
- Open chains never start a part.
- Every other entity (open paths, marks, hidden entities, bend lines, text) goes to the innermost part whose outline holds a point on it, or to the nearest part when none does.
- Parts are numbered in reading order: rows from the top, each left to right.

The search runs off the UI thread whenever an item loads or its filters change. With two or more parts, the **Separate (N)** button shows in the detail bar, and the entity right-click menu adds **Separate This Part** and **Separate All N Parts**.

## What a separated item keeps

Each new item takes the entities of its part, with their IDs, so entity roles, exclusions, suppression, breakoffs, tabbed cutouts and dimensions follow them. Bends go with their source entity or, without one, their midpoint; text goes by its position. Quantity, customer and source path are copied, so every drawing records the same file. `BuildDrawing` then gives each its own source offset.

Names come from the file's labels (SVG element titles): the smallest label region that holds the part's outline and no other part's. Without one, the part is `<file>-<number>`. Names are made unique within the list.

**Separate This Part** moves only the chosen parts out; the rest stay together under the item's name.

Separation is refused when a breakoff or tabbed cutout has edges in more than one part. Edits made in the Program tab are lost, after a warning, since each part's program is rebuilt.
