# Pierce zones

On thick plate, a laser or plasma pierce blows a crater wider than the kerf. Set
`CuttingParameters.PierceDiameter` (Cutting Settings, **Pierce Diameter**) to the crater's
diameter for that machine, material and thickness. 0, the default, turns the zone off, and
lead generation, planning and posting then behave exactly as before.

## The rule

With a diameter set, every lead-in pierce must keep half of it, the zone radius, from all part
material: its own contour, its other holes and every other part on the plate. The check reads
clean material, so marks (scribes, rapids) are not material, and cutoffs are not checked.

- A contour pierced directly on its own line, with no lead-in (`NoLeadIn`), is not checked. The
  pierce zone applies to lead-in pierces only.
- A break cut's lead-in pierces in its breakoff scrap, inside its own part's material, so only
  other parts are checked for it.
- The own-contour clearance that straight corner leads and round-hole clamping already used,
  `PierceClearance`, becomes the larger of `PierceClearance` and the zone radius.

## Hole fallback

`ContourCuttingStrategy.ResolvePierceZone` runs for every internal contour and round hole the
emitter cuts, and for the manual lead-in preview:

1. A configured lead-in whose pierce already keeps the zone from the hole wall is kept as is.
2. Otherwise the pierce moves straight in from the wall to the hole's centre line, the centre of
   a round hole, or the lead's own length in when that comes first (see
   [leads stop at the centre line](lead-in-placement.md#leads-stop-at-the-centre-line)). The lead
   becomes a straight line from there to the entry.
3. If that point cannot keep the zone, the entry sits too close to another wall or the lead is
   shorter than the zone. The pierce moves toward the nearest eroded point beside the entry
   (`ErodedPierce`), on the stroke's centre line when the lead reaches that far, else the hole's
   pole of inaccessibility (`PolyLabel`). A lead shorter than the zone stops at the first point
   that keeps the zone: in a convex hole clearance grows steadily along that line, so this is
   the shortest clear straight lead from that entry.
4. A hole too small for the zone is pierced at its pole, the point with the most clearance.
   The shortfall is still reported (see below), never hidden.

The fallback reads only the hole's own geometry, so it does not know about a part nested
inside the hole. Such a part is still checked as other material.

## Perimeter fallback

A perimeter's fallback needs the parts around it, which the emitter never reads (it emits each
contour from its own geometry and the planner's choice alone). So the cutting planner owns it:
`JointCuttingPlanSearch` runs `ScrapPierceSearch` (OpenNest.Engine) when Plan Cutting finds no
configured lead-in that fits a perimeter, and a pierce diameter is set.

1. The planner first tries every configured entry as before. Any entry whose lead fits, pierce
   zone included, is used and nothing moves.
2. Otherwise it looks for a pierce in the open scrap around the part: a window reaching four zone
   radii past the part's extent, minus the clean material of the part and of every part near it,
   grown by the zone radius and a margin (`ClipperBridge`, flattened conservatively). The margin,
   a hundredth of the radius (at least 0.001), covers flattening and rounding. Holes of other
   parts count as scrap, so a part nested in a cutout pierces in that cutout. The part's own holes
   do not.
3. For each of the perimeter's ranked entries, in planner order, the clear point nearest it is a
   candidate pierce. Its entry is the perimeter's nearest point, and the lead runs straight from
   the pierce to it. The candidate is a `ContourChoice` whose `Pierce` is set
   (`PreparedContours.ScrapPierceEntry`). The emitter emits that straight lead in place of the
   configured lead-in and keeps the configured lead-out.
4. `LeadPathValidator` checks each candidate exactly like any planner-chosen entry: lead paths,
   lead-out and pierce zone. Only clear candidates are used, and the rest of the search (rapids,
   holes, final replay) treats them like any other entry.
5. When the window has no clear scrap, the best effort is the pole of inaccessibility
   (`PolyLabel`) of the scrap in the window, with the parts as holes, keeping the most clearance.
   It is tried like any other candidate. If it does not keep the zone, Plan Cutting reports
   **No open scrap ... keeps the pierce diameter clear**, with that point and its clearance. The
   pierce is never accepted inside the zone.

A drawing-pinned perimeter keeps its pin's entry and lead. A pin whose pierce is inside the zone
is a fixed-emission warning. With no pierce diameter none of this runs, and plans are unchanged.

## Where it is checked

- **Plan Cutting:** each candidate entry's emitted leads, and the final replay, check the
  pierce zone through `LeadPathValidator`. A planner-chosen pierce inside the zone rejects
  that entry, and the planner tries others. When no entry on a contour fits, the
  "No tested lead-in fits" finding says why. A locked program's or drawing pin's pierce inside
  the zone is a fixed-emission warning, as for any fixed lead.
- **Before posting:** `PierceZoneReview` checks every lead-in pierce of every part's placed
  program, however the leads were made, using the part's own cutting parameters or else the
  plate's. Each pierce too close to material is a **Pierce zone** warning that needs the usual
  per-attempt consent. Material that cannot be captured makes the check incomplete for that
  part.

## Not yet covered

- The perimeter fallback runs only in strict Plan Cutting. A best-effort plan (made when strict
  planning fails on regeneration) keeps the configured perimeter lead-ins, so pre-post review
  reports their pierce zones.
- The scrap window is not clipped to the plate. A pierce near the plate edge can lie off the
  plate.
- Cutoff pierces are not checked.
- Nesting does not know about the zone. Parts packed closer than the pierce diameter on every
  side can leave a perimeter with no clear scrap nearby; Plan Cutting reports it, as above.

## Regression checks

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter "FullyQualifiedName~PierceZoneTests"
```
