# Dimensions

A drawing can carry overall dimensions: the part's size along X and along Y, shown in the
CAD Converter. In the **Dimensions** panel, **Add Overall Dimensions** adds whichever of the
two is missing, below and to the right of the part. Drag a dimension line to move it, across
the part to its other side if wanted; **Remove** deletes the selected one.

## Contract

Dimensions are annotations. They never affect the program, nesting, cut time or posts, and
they do not appear on plates, in PDFs or in DXF export.

- `Drawing.Dimensions` stores only a `DrawingDimension`'s kind and signed offset from the
  part's extents, so a value cannot go stale. Positive is above (horizontal) or right
  (vertical); zero is not a valid offset.
- `DimensionBuilder` (`OpenNest.Core/Dimensions`) measures the entities as they will be cut:
  visible, with roles, breakoffs and tabbed cutouts applied, as the Program tab builds them.
  `FileListItem.Bounds` is not used because it includes hidden layers.
- Marks do not count: anything `ScribeLayerRepair.IsMarkLayer` matches (scribe, etch and
  engrave, by layer name) and anything `SpecialLayers.IsMaterial` rejects (rapids, break
  cuts). `IsMaterial` compares layer objects, so it alone would count source marks on their
  own layers. Tabs and a breakoff's scrap count: both are material of the part as cut.
- A dimension with nothing to measure, or a part with no size along its axis, is listed as
  not shown and keeps its placement until the geometry returns.
- Values show three decimals with no unit, in the drawing's units.
- Split pieces start with no dimensions, like breakoffs and tabbed cutouts.

File format: see [drawing dimensions](nest-file-format.md#drawing-dimensions).
