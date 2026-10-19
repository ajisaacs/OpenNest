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
2. Otherwise the pierce moves along the line from the contour entry toward the hole's pole of
   inaccessibility (`PolyLabel`; the centre of a round hole), stopping at the first point that
   keeps the zone. The lead becomes a straight line from there to the entry. In a convex hole
   clearance grows steadily along that line, so this is the shortest clear straight lead from
   that entry; in other holes the point found is still clear.
3. A hole too small for the zone is pierced at its pole, the point with the most clearance.
   The shortfall is still reported (see below), never hidden.

The fallback reads only the hole's own geometry, so it does not know about a part nested
inside the hole. Such a part is still checked as other material.

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

- Perimeters have no fallback: an outside lead-in whose pierce is inside the zone (a lead
  shorter than the zone radius, or parts closer than it) is rejected or reported, never moved
  to open scrap. A free-space search for perimeter pierces is planned work.
- Cutoff pierces are not checked.
- Nesting does not know about the zone, so part spacing smaller than the zone radius can leave
  perimeters with no clear pierce.

## Regression checks

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter "FullyQualifiedName~PierceZoneTests"
```
