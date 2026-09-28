# Best-fit pair spacing: native arc contact

## Corrected case

CPU best-fit slides and shared directional-distance queries now check both external and internal curve tangency. A convex offset corner inside a concave slot contacts at the difference of the radii, not their sum. Both forward ray/circle roots must be checked: the nearer root can be outside an arc's angular span while the farther root is the first actual contact. Tangent-point directions differ for internal contact, including when the moving curve is the larger one.

The raw `SpatialQuery.CurveTangencyDistance` helper and shared slide events implement this calculation. The subsequent [directional-slide repair](directional-slides.md) routes both callers through material-aware contact events; the measurements below describe the earlier native-tangency repair. It assumes a unit direction, nonnegative radii and world-frame centers. An optional arc supplies only angular limits; null represents a full circle. This helper supplements the existing vertex/line phases rather than replacing them. Equal-radius coincident curves have no isolated internal tangent and remain the vertex phases' responsibility; zero-radius curves are points. No spacing tolerances or acceptance policies were changed.

### Reproduced U-shaped part

The closed native outline is 2.5 by 3 inches, with a 1.5-inch outer semicircle, a 0.875-inch inner semicircle, and square-ended tips. `NativeUFixture` constructs it without external files. The independently imported source was `End sheet lift lug.DXF`, SHA-256 `b7802988dd56216fbc49b1cf8afdabd5df26e96da61763e3c8c2d9105ff19d83`; the input is unchanged and is not required by the tests.

At 0.25-inch requested spacing each outline is inflated by 0.125. The failing contact is between a 0.125 convex corner and a 0.75 concave arc. With the stationary slot centered at (1.5, 1.5) and moving corner at (x, 2), contact occurs at x = 1.125, from center distance 0.625. Sliding left from x = 5.5 therefore stops after 4.375, instead of the old endpoint-only 4.414578098794425. Independent raw-outline clearance at this contact is exactly 0.25.

Fresh-process import/cache/materialization probes used a 24-by-24 sheet, one-inch edge spacing (22-by-22 usable area), and 0.25-inch part spacing:

| Path | Before | Corrected |
| --- | --- | --- |
| Smallest-envelope kept pair, minimum raw clearance | 0.22548054744486495 | 0.24999929486961545 |
| `PairFiller`, 70 parts, minimum raw clearance | 0.22548054744486434 | 0.24999929486961417 |
| `PlateFillService.FillItem("Default")`, 70 parts, minimum raw clearance | 0.2254805474448648 | 0.24999929486961447 |

Measurements use raw material outlines tessellated at 1e-6 chord tolerance and independent Shapely boundary distances, not the slide solver or its offset contours. The roughly 7e-7 shortfall is within tessellation error. These specific pair/grid polygons were valid; no geometry repair was applied. The corrected top pair passes the existing validator. The full grids do **not** pass it; see below.

## Remaining limitations — not a complete spacing fix

- Other angled candidates remain physically too close. Of 568 kept candidates in the corrected real-DXF probe, 49 had boundary distance below 0.25 minus 2e-6, including 48 below the existing 0.2495 spacing-slack threshold. The worst measured boundary gap was about 0.205618. Their cause is not established by this repair; CPU projection filtering and missing arc/line interior contact are separate suspects.
- The existing offset-based validator still reports 28 spacing violations in each corrected 70-part grid despite the independent fine-outline measurement above. Do not loosen validator tolerance or declare these reports resolved without isolating the discrepancy. This report verifies physical outline clearance for that fixture, not validator acceptance of the whole grid.
- Across the complete candidate probe, 42 tessellated polygons were invalid. Their boundary distances were recorded without silently repairing them, but intersection-area verdicts were omitted for those invalid polygons. This caveat does not apply to the top pair or the two reported grids.
- Kept-pair acceptance still checks raw overlap, not requested clearance. Interactive fill/group paths can trust an invalid seed. This repair does not add a final spacing gate or repair existing user placements.
- Serialized best-fit results have no algorithm-version invalidation. Loading previously saved candidates can retain old poses. The measurements above use newly imported drawings/fresh computation, not restored caches. Reimport/recompute when evaluating this fix; existing nests and DXFs are not rewritten.
- A zero-demand `PlateFillService.Nest` probe returns no parts and is **not** evidence of a valid whole-job nest. The nonempty cases verified here are pair generation, pair fill, interactive fill, and the quantity-two shortcut.
- GPU behavior and Windows desktop execution were not verified on this Linux host.

Next hardening should isolate the remaining angular contact defect with a first-contact regression, then address clearance-aware seed acceptance/cache compatibility and the grid-validator disagreement as separate changes. Do not conflate those with the unrelated unchanged-row validation-reuse performance work.

## Regression verification

```bash
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug \
  --filter 'FullyQualifiedName~CurveContactDistanceTests|FullyQualifiedName~NativeUClearanceTests'
```

The 36 targeted cases cover both callers, swapped curves, reflected/translated/rotated directions, both roots, arc-span rejection, external-circle contact, zero spacing, point/equal-radius degeneracies, the real-shaped closed U fixture, top cached-pair source materialization, and nonempty interactive fill with quantity zero/two. The test clearance oracle uses fine raw line segments and independent point/segment distances, plus overlap checks; it does not use native offset curves or the slide solver. The complete-grid validator result is logged, not represented as passing.

Baseline `bc5fd86996da3abb98bb3b1da5f81de82692bf5e` failed 18 of the original 22 geometry regression cases and all three integration cases before the repair. Final scoped formatter and `--verify-no-changes` runs succeeded with `EnableWindowsTargeting=true`; all suites below passed on the repaired tree:

| Suite | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Main Release | 1655 | 21 | 0 |
| Main Debug | 1685 | 21 | 0 |
| Engine Release | 300 | 0 | 0 |
| IO Release | 41 | 0 | 0 |

The detached worktree includes the local fixture configuration. The 21 main-suite skips are reported rather than counted as passes. No Windows runtime result is implied.

Local evidence: `/home/aj/extracted/2026-09-27/u-part-spacing/` (original diagnosis, before/fixed raw layouts, input hashes, RED tests) and its `resumed/` directory (fresh DXF probe, independent measurements, full-suite TRX/logs and review). The synthetic regression sources are the portable reproduction; local evidence paths are supplementary, not a build dependency.
