# Lead-in placement at cutout corners

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

## Regression checks

Run `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter FullyQualifiedName~CutoutCornerLeadInTests`.
The tests exercise generated part programs, default automatic placement, every
rectangular corner with both adjoining edges and windings, part rotation, acute
and obtuse angles, reflex corners, line/arc corners, preview agreement, and
unchanged/fallback behavior.

A headless before/after import of `4980 A01 PT07.dxf` (SHA-256
`1535D77BC1EEEDD21A27E7CE91EA4C51055118D019C5A09C144F1F41740895B6`)
reproduced the issue on all five 0.282 × 0.532 rectangular cutouts. At the default
0.125 lead-in length, all five corrected pierce points are approximately 0.08838835
inside both adjacent edges. The straight segments stay inside the rectangles
except for their contour endpoints; the other two generated lead-ins remain
unchanged. Coordinates were compared with a 1e-8 tolerance to allow incremental
program round-trip floating-point noise. The source drawing is not bundled.

Windows manual acceptance: import the part and use Plate > Assign Lead-ins.
Confirm diagonal lead-ins at all five small rectangular cutouts. Under Plate >
Place Lead-in, select the part, lock a cutout, and hover a corner: the preview
should point into the cutout and the committed lead-in should match. Hover a
mid-edge point and confirm perpendicular placement is unchanged. Windows visual
interaction is not verified by the Linux cross-build.
