# Breakoffs

A breakoff is scrap drawn on a part, such as a forming backgauge tab, that must stay
attached through cutting and forming and is snapped off afterwards. In the CAD
Converter's **Breakoffs** panel, **Add Breakoff**, then click the line that joins the
part and the scrap. Lines or arcs that end partway along another visible line or arc
first split it there (the first piece keeps its entity ID), so a joining line can end
exactly where it meets the outline. The clicked entity then grows into the chain that
continues as a single path. Its ends must touch, within 0.001, one closed outline
drawn around both part and scrap, and the chain must run inside it: either a separate
line drawn across the outline, or a section of the part outline with the scrap's
outside edges drawn separately.

The smaller side is the scrap; **Flip** (or "Larger side is scrap" in the dialog)
swaps it. The dialog sets the tab count (both end tabs included, at least 2) and tab
width; tabs start evenly spaced and can be dragged along the joining line. Edit
re-spaces the tabs only when the count or width changes. The view shades the scrap,
draws the cuts and marks where each cut is pierced.

## Geometry contract

`BreakoffBuilder` (`OpenNest.Core/Breakoffs`) resolves each `Drawing.Breakoffs` entry
against the entities being cut:

- The two sides are the outline from one end of the joining line to the other, closed
  back along the joining line. They must add up to the outline within 1%. Tab
  positions are measured from the joining line's end with the smaller X (then Y).
- The outline is kept, with every joint snapped exact (lines move their endpoint; a
  short connecting line is added between arcs), because the planner closes contours within
  1e-6 and the program is re-chained from any entity.
- The joining line between consecutive tabs becomes open cuts on the `Break` layer
  (`LayerType.Break`, `SpecialLayers.Break`), each travelling with the scrap on its
  left. The joining line itself is not cut at the tabs; end tabs keep cuts off the
  outline.

## Cutting break cuts

Break cuts cut like `Cut` motions but are not material: `SpecialLayers.IsMaterial`,
material capture, overlap checks, nesting and area ignore them. `ShapeProfile` and
`ConvertGeometry` pass them through in order without re-chaining or re-winding.

Both Plan Cutting (`PreparedContours`, emitted after scribes and before contours in
every full or prefix emission, never in single-contour probes) and the manual lead-in tool
(`ContourCuttingStrategy.Apply`/`ApplySingle`) emit them through
`ContourCuttingStrategy.EmitBreakCuts`:

- Each cut is entered from its scrap side with the internal lead-in; `NoLeadIn`
  pierces on the line. There is no lead-out, since the cut ends at a tab.
- It is entered at whichever end has more scrap before the part's contours along the
  scrap direction (`BreakCutEntry`), because the scrap narrows to a wedge near the
  outline. Entering at the far end cuts it in reverse.

Checks treat them as intentional open cuts retained by tabs: `ReleasedContourState`
never makes them rapid obstacles or "open fragment" warnings, but reports a missing
lead-in like any contour. `LeadPathValidator` lets a lead into a break cut run
through its own part's material, and still refuses one that touches any of the part's
boundaries or another part. The posts cut them: Gravograph IS maps `Break` to its cut
settings; CI Fiber and Cincinnati treat any non-lead, non-scribe layer as cut.

Breakoffs that do not resolve (hidden or missing joining line, overlapping tabs) are
listed with the reason and not applied, but stay on the drawing.
`CadImporter.BuildDrawing` applies breakoffs and copies them to the drawing;
`SourceEntities` keep the original geometry so a reopened drawing can be edited. Split
pieces get no breakoffs.
