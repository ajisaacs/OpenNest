# Lead-in placement at corners

Straight (`LineLeadIn`) lead-ins at closed internal contour corners use the
inward angle bisector instead of the normal of whichever edge was selected.
With the default 90-degree approach angle, a rectangular cutout therefore gets
a diagonal lead-in into the scrap, rather than one lying along the other edge.
This applies to automatic assignment and manual placement. The manual preview
uses the same Core calculation, including when the lead-in style changes while
the cursor is stationary.

`ContourCuttingStrategy.ComputeLeadInNormal` combines the two adjacent inward
unit normals. It handles either winding, either selected edge, rotated parts,
and line/arc junctions using their local normals. Summing vectors avoids angle
wraparound and edge-length weighting. Open contours, disconnected endpoints,
zero-length edges, and cusps without a unique bisector retain the entity normal.

The configured approach-angle offset is still applied relative to the computed
normal; 90 degrees follows the bisector. Lead-in length is unchanged. Mid-edge
points, external contours, circles, curved/composite lead-in styles, and
lead-outs keep their existing placement rules.

This is a local direction correction, not a whole-path clearance guarantee.
An excessively long lead-in or an approach angle rotated away from the bisector
can still leave a small cutout. General non-circular containment/length clamping
and sharp-corner handling for curved/composite lead-ins remain separate work.

## Outside perimeter corners

A straight (`LineLeadIn`) lead-in at a convex corner of the outside perimeter
(interior angle under 180 degrees) extends the edge cut first: the pierce sits on
that edge's line, behind the corner, and the torch travels straight into the corner
and keeps cutting along the same line. Which of the two edges was picked (auto
assignment or the manual cursor) does not matter; the cut direction never changes.
The approach angle is ignored at such a corner.

The straight lead is used only while its pierce stays at least
`CuttingParameters.PierceClearance` (or the [pierce zone](pierce-zone.md) radius, when larger)
from the contour and the lead crosses the
contour nowhere but at the corner. Very flat corners (about 165 degrees and over for
a 0.25 lead with 0.0625 clearance) fall back to the normal lead-in, perpendicular to
the first-cut edge, so tessellated curves do not get straight leads. Reflex perimeter
corners (the inside corner of an L) use the notch bisector, like cutout corners.

A `LineLeadOut` mirrors this: at a convex perimeter corner it runs straight on past
the corner along the last-cut edge, with the same clearance fallback to the last-cut
edge's normal and a bisector at reflex corners. A tabbed perimeter gets no run-out:
every lead-out style leaves from where the trimmed cut actually ends, on that end's
normal, so an arc lead-out starts on its own radius and the tab gap stays uncut.
`ContourCuttingStrategy.ResolveLeadIn`/`ResolveLeadOut` own these rules;
program generation and the manual preview share them. Other lead-in styles are
unchanged.

## Rounded corners in Plan Cutting

Plan Cutting prefers to start a contour at a convex corner. The tool returns to the start
after it has put heat into the plate, so a start partway along a straight edge can leave a
step. A start at a corner hides it. An edge midpoint is a fallback, used only when no corner on
a side facing the next cut fits.

A corner rounded by a small fillet counts as a corner. The fillet must be tangent to straight
edges at both ends, turn convexly through less than a half circle, and have a reach
(`radius * tan(sweep / 2)`) no longer than either edge. Each of its two tangent joints is then
a convex corner entry (`PreparedContours.FilletReach`). Ranking counts the joint on both sides of
the corner it rounds, as it would a sharp corner. Longer curves, half-circle ends and inside
(reflex) fillets keep their tangent joints, which rank level with edge midpoints. The lead-in at
a fillet's joint is the normal one for its edge, since emission still sees a smooth joint there.

## Curved leads

Every curved lead (Arc, Line + Arc, Clean Hole, the arc lead-out) meets the cut tangentially and
in the cut direction. Its arc center lies along the contour normal, away from the material, so
the arc turns with a cutout's winding and against an outside perimeter's;
`ContourCuttingStrategy.LeadTurn` gives that direction, and program generation, the pierce-zone
check and the manual preview all pass it to the lead. A Line + Arc approach angle is measured
back from the normal on the side the arc turns from, so the arc takes the short way round
under either winding.

An Arc lead-in's `SweepAngle` (degrees, in (0, 180], default 180) sets how much of a circle it
is: 90 is a quarter circle, 180 a half circle that pierces two radii out on the normal. Settings
and cut pins saved before the sweep existed read as 180.

At a convex corner of an outside perimeter, an Arc, Line + Arc or Clean Hole lead-in uses the
normal of the edge cut first, and an Arc lead-out that of the edge cut last, whichever of the
corner's edges was picked; the arc then lies wholly on the scrap side of that edge. Reflex
corners and cutout corners keep the picked edge's normal: a tangent arc there would cross the
part.

## Cutouts too small for their lead-in

Text, narrow slots and small holes often have no entry where the configured lead-in fits. When
Plan Cutting has tried every entry of a cutout and none fits, it erodes the cutout instead
(`ErodedPierce`, OpenNest.Core):

1. The cutout's pole of inaccessibility gives its inscribed radius, the deepest any erosion can go.
2. The cutout is offset inward (`Clipper.InflatePaths`, negative) by the pierce clearance
   (`GetEffectivePierceClearance`) when it has room for it, else by nine tenths
   (`ErodedPierce.DepthFraction`) of its inscribed radius, plus a margin for flattening, round
   joins and Clipper rounding. Shallower levels, down to half that depth, back it up.
3. Each of the cutout's ranked entries gets the nearest point of the deepest level that reaches
   the wall beside it: a letter stroke narrower than the cutout's widest place is still pierced
   on its own centre line next to the entry, so the route can enter and leave a letter where it
   needs to.
4. The lead-in runs straight from the pierce to that entry, or else to the cutout's native point
   closest to the pierce (`PreparedContours.ErodedPierceEntries`, a `ContourChoice` whose `Pierce`
   is set). A cutout so pierced has no lead-out: a feature too small for its lead-in has no room
   for one either.
5. A cutout too small to erode is pierced at its pole.
6. `LeadPathValidator` checks each candidate like any other planner-chosen entry, pierce zone
   included. When none is clear, the "No tested lead-in fits" finding says why.

A configured lead-in that could not be settled counts as no fit: text outlines are full of leads
tangent to a wall, which the check reports as numerically uncertain, and a clear eroded pierce
outranks them.

Cutouts where a configured lead-in fits keep it, so plans of other parts are unchanged. Hole entry
selection, the preferred hole path and contour recovery all use the fallback; recovery's
reading-order sweep also pierces a letter this way at the side it leaves from. Only Plan Cutting
uses it: the emitter's own automatic assignment and the manual lead-in preview keep the
configured lead.

The one exception is a part recovery cannot finish well. A letter can have a single fitting lead
facing the wrong way, which either stops the hole tour or sends the outside cut round to the side
the next part cannot be reached from without crossing this one. Recovery therefore holds back a
route that ends facing away from the next cut (in part order, its straight line to that cut
crosses the part's own perimeter) and tries the outside entries ranked ahead of the first it
reached again, this time adding the eroded pierces of every cutout that refused any configured
lead. Held routes are offered after those. These tries get their own allowance, since the
ordinary search may have spent all of its share on the outside entries. A lead tangent to a wall
can check clear alone and still touch in the whole part, where rounding moves it by about 1e-15:
a widened try drops such an entry and repeats that outside entry instead of rebuilding the chain.

## Regression checks

Run `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~CutoutCornerLeadInTests|FullyQualifiedName~PerimeterCornerLeadInTests|FullyQualifiedName~ErodedPierceTests"`.
The cutout tests exercise generated part programs, default automatic placement, every
rectangular corner with both adjoining edges and windings, part rotation, acute
and obtuse angles, reflex corners, line/arc corners, preview agreement, and
unchanged/fallback behavior. The perimeter tests cover every square corner under both
windings and a rotation, auto and manual placement from either edge, preview
agreement, approach-angle handling, the flat-corner clearance fallback, reflex
notches, and straight lead-outs. `ErodedPierceTests` covers the eroded pierces of a slot and a small round hole, a cutout that keeps
its configured lead, the containment of text cutouts that sit a micro-inch apart and, with the
optional `SmallFeatureLeadNestPath` input, a plate of stencil-text tags planned in reading order.
`ArcLeadInTangentTests` checks curved-lead tangency, direction
and sweep on perimeters and cutouts under both windings, and at every perimeter corner from
either edge.

Windows manual check: import a part with small rectangular cutouts and use Plate >
Plan Cutting. Confirm diagonal lead-ins at the cutout corners. Under Plate >
Place Lead-in, select the part, lock a cutout, and hover a corner: the preview
should point into the cutout and the committed lead-in should match. Hover a
mid-edge point and confirm perpendicular placement is unchanged. Windows visual
interaction is not verified by the Linux cross-build.
