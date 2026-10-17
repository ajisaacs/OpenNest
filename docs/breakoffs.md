# Breakoffs

A breakoff is scrap drawn outside a part, such as a forming backgauge tab, that must
stay attached through cutting and forming and is snapped off afterwards. The CAD
Converter's **Breakoffs** panel adds one: **Add Breakoff**, then click any outside edge
of the scrap piece. The clicked edge grows into the open chain of visible lines and
arcs that continues as a single path; both ends of that chain must touch one closed
outline within 0.001. The settings dialog sets the tab count (both end tabs included,
at least 2), tab width and slot width; tabs start evenly spaced and can be dragged
along the break line. Edit re-spaces the tabs only when the count or tab width
changes.

## Geometry contract

`BreakoffBuilder` (`OpenNest.Core/Breakoffs`) resolves each `Drawing.Breakoffs` entry
against the entities being cut:

- The **break line** is the outline section between the chain's ends that, with the
  chain, bounds the smaller region. The other region must equal the outline plus the
  breakoff within 1%, so a chain inside the part is refused.
- The **outline** becomes the original outline with the break line replaced by the
  chain. The chain's ends are snapped exactly onto the split points, because the
  cutting planner closes contours within 1e-6 (a joining line is added when the end
  entity is an arc).
- Between consecutive tabs, the break line is cut by a **closed slot** on the breakoff
  side: its part side is the exact break line, and its scrap side runs `slotWidth`
  into the breakoff. Near the breakoff's edges the slot is pulled back so at least
  `tabWidth` of breakoff remains; its ends move inward until the slot is
  `slotWidth / 4` deep. Interior tabs keep exactly their width; end tabs can be wider.
- Tab positions are measured from the chain end with the smaller X (then Y).

Slots are closed contours on purpose: `PreparedContours` accepts only closed
contours, so the open cuts this replaces could not get planned lead-ins. As internal
contours, every slot pierces inside itself, which is breakoff scrap. The automatic
entry may still finish its lead-in at a slot corner on the break line, which is a tab
edge.

Breakoffs that do not resolve (hidden or missing edges, overlapping tabs, no room for
a slot) are listed in the panel with the reason and are not applied, but are kept on
the drawing. `CadImporter.BuildDrawing` applies breakoffs to the program and copies
them to the drawing; `SourceEntities` keep the original edges so a reopened drawing
can be edited. Split pieces get no breakoffs. The breakoff counts as part material
for area, nesting and overlap checks.
