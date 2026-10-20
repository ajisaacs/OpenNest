# Tabbed cutouts

A tabbed cutout is a hole whose slug stays attached to the part by small uncut tabs,
so it cannot drop or tip up while the sheet is cut and is knocked out afterwards. In the
CAD Converter's **Tabbed Cutouts** panel, **Add Tabbed Cutout**, then click a circle or
any line or arc of a closed hole. The clicked entity grows into the loop that continues
as a single path and closes on itself (within 0.001); it must lie inside another closed
outline drawn with it, so the part's perimeter is refused.

The dialog sets the tab count (at least 1) and tab width; tabs start evenly spaced, one
centered on the middle of the hole's first edge, and can be dragged around the hole. Edit
re-spaces the tabs only when the count or width changes. The view shades the slug, draws
the cut pieces and tabs, and marks where each piece is pierced.

## Geometry contract

`TabbedCutoutBuilder` (`OpenNest.Core/TabbedCutouts`) resolves each
`Drawing.TabbedCutouts` entry against the entities being cut, after breakoffs:

- The hole is chained into one loop with its joints made exact (a circle becomes two half
  arcs), oriented counterclockwise and started at its lowest-left vertex (smallest X,
  then Y). Tab centers are distances along that loop and wrap around it.
- The loop is replaced, in contour order, by open pieces between tabs (keeping the hole's
  layer) and the tab spans themselves on the `Tab` layer (`LayerType.Tab`,
  `SpecialLayers.Tab`). Each tab follows the piece before it, so pieces and tabs close the
  hole exactly.
- Tabs must not overlap and the loop must be longer than twice the total tab width.

## Tabs are material, never cut

Tab spans bound material like `Cut` motions: `SpecialLayers.IsMaterial` keeps them, so
area, material capture (`LeadMaterialSnapshot`) and the material-overlap check all see a
closed hole. They are never cut. The plate view fills them but does not draw them as cuts.

Nesting is the exception: the slug stays attached while the sheet is cut, so nothing may
be placed in the hole. Nesting and layout paths read material through
`NestingMaterial.ForNesting`, which drops any closed contour with tab spans so the slug
counts as part: part outlines and collision lines (`PartGeometry`, `Part`), the cutout
router (`JobPartGeometry`, whose material area then includes the slug), fill expansion,
best-fit, `NestLayoutCheck` and the GPU bitmaps. Programs without tab spans read exactly as
before. A planned program has open pieces instead of a closed hole, so it bounds no hole
either way.

Both Plan Cutting (`PreparedContours`) and the manual lead-in tool
(`ContourCuttingStrategy.Apply`/`ApplySingle`) take a closed contour with tab spans out of
the contour choices. Every full or prefix emission cuts its pieces after the break cuts
and before the contours, never in single-contour probes:

- Each piece is cut in the hole's own direction from the tab before it, entered from the
  slug with the internal lead-in; `NoLeadIn` pierces on the contour. There is no
  lead-out: the piece ends at a tab, which a run-out would cut into.
- A lead-in that would reach back over the tab behind its piece is mirrored about the
  entry normal, so it approaches from the piece's side. On an arc, a lead ending in an arc
  would meet the cut tangentially, a contact verification cannot resolve, so the piece is
  pierced straight in from the same distance instead.

## Verification

Plan Cutting certifies tabbed cutouts. Its contour accounting
(`ContourProgramVerifier`) requires each piece to start exactly where a span between tabs
starts and to cover that span whole, once; the ring counts as cut when every span is.
Lead validation treats a piece's start like any contour joint. The released-contour check
proves the open pieces retain their slug with the same open-cut proof pre-post review uses
(see [pre-post verification](post-verification.md)); planning still refuses its own tabs
followed by a lead-out.

Pre-post review passes a planned tabbed cutout through that proof. A program that still
has tab motions (the clean drawing program, never planned) is reported as incomplete,
since a post may cut through them: Gravograph IS skips the `Tab` layer, while CI Fiber and
Cincinnati cut any non-lead, non-scribe layer.

## Persistence

Definitions persist on the drawing, as `tabbedCutouts` in the
[nest file](nest-file-format.md), with the program already applied; tab motions are saved
tagged `:TAB`. `CadImporter.BuildDrawing` applies them and copies them to the drawing;
`SourceEntities` keep the original hole so a reopened drawing can be edited. Cutouts that
do not resolve (hidden or missing hole, overlapping tabs) are listed with the reason and
not applied, but stay on the drawing.
