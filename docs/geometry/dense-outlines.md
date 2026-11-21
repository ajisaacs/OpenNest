# Dense outlines in spacing geometry

Fill, best-fit pairs and `Compactor` slide parts against each part's spacing outline:
`PartGeometry.GetOffsetPerimeterEntities`, the perimeter offset outward by the spacing (cutouts
are never part of it). Every slide costs in proportion to the entities on both sides, so a
perimeter carrying a curve that a CAD export wrote as hundreds of tiny lines makes every query
slow.

## Reproduced case

A braze-casing angle (`4980 A01 PT71`, 5.30 x 28.28, 3/16 steel) has a 0.98-inch curve of radius
22.9 written as 199 lines of about 0.005 inch. Its spacing outline held 414 entities, and Fill of
33 copies on a 72 x 144 sheet took 8.6 minutes; Auto Nest's Irregular engine, which proposes such
fills as Lattice blocks, never finished its first plate in 15 minutes.

Two causes, both fixed:

- **Near-circle joint arcs.** `Shape.JoinOffsetLines` tells a convex corner from a concave one by
  an unnormalized cross product against `Tolerance.Epsilon`. Between 0.005-inch lines a shallow
  concave turn falls under that threshold, the nearly parallel offsets do not intersect, and the
  fallback arc swept the long way round the corner: a near circle of the spacing radius at every
  joint. Those circles lie inside the true offset, so layouts stayed valid, but each one is a slide
  query. A turn that is not convex now gets a straight bridge between the offsets' ends, as the
  general join already does at concave corners; a convex turn keeps its short arc.
- **Dense runs are thinned** (`DenseOutline`). A perimeter holding a run of at least
  `MinRun` (16) consecutive lines or arcs shorter than `ShortLength` (0.02 inch) is offset by the
  spacing plus `Pad` (0.0015 inch) instead, and each such run is replaced by Douglas-Peucker chords
  through points on that offset (arcs sampled within 0.0005, chords within 0.001). Every chord
  stays within the pad of the padded offset, so it lies outside the offset at the true spacing:
  layouts spaced on the thinned outline keep the spacing, at most twice the pad (0.003 inch) wider
  beside a dense run. Perimeters without a dense run keep their exact offset.

The angle's spacing outline is now 21 entities, and the same Fill takes 69 seconds with every
part passing the overlap check. What remains is mostly the exact overlap check, which still
triangulates the true perimeter and is deliberately not thinned. On the whole braze-casing job
(219 parts, five stock sizes) Irregular now completes its normal search in under 5 minutes
(24 sheets, 88.6%), and searching until a 15-minute limit reaches 22 sheets (89.9%), against
28 sheets (78.3%) before.

## Boundaries

- Thinning only ever adds clearance; it never loosens a tolerance. Validation (`NestLayoutCheck`,
  `SpacingOutline`, `NativeContours`) and the overlap check still read the true geometry.
- Only the spacing outline is thinned. Drawings, programs, cut paths and posts are unchanged; to
  remove the dense run itself, simplify the drawing in the CAD Converter.
- `DenseOutlineTests` covers the joint arcs, the spacing bound on both sides of a dense run, and
  closure.
