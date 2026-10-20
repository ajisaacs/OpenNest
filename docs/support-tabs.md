# Support grid and support tabs

A cut part drops onto the machine's support bed. If its center of gravity is not over the
points it rests on, it can tip into the head's path or fall through. OpenNest models the bed,
checks every part against it, and can tab only the parts the bed cannot hold level, at the
perimeter point where one tab steadies the part most.

## The support bed

`OpenNest.Support.SupportBed` is the machine's bed, fixed to the machine and measured from the
plate origin into the sheet; a sheet in another quadrant sees it mirrored. The built-in layout
is the laser grid, `SupportGrid`: one contact point every `PitchX` along X and `PitchY` along
Y, the first at (`OffsetX`, `OffsetY`). Lengths are in the nest's units. Straight slats, and
curved slats imported from a CAD layout (as on the plasma table), are planned as further
`SupportBed` subclasses: a layout only reports its contacts under a part's material.

Every bed also carries:

- `RegistrationTolerance`: how far a sheet may sit from its nominal place. The bed is checked
  shifted by up to this much along X, Y and both diagonals, and the worst result is kept.
- `StabilityMargin`: how far inside its contacts the center of gravity must lie to count as
  supported, allowing for slag, gas pressure and worn contacts.

The bed belongs to the nest (`Nest.SetSupportBed`); every plate follows it, including plates
added later. It is display, analysis and tab placement only: never material, spacing or a
keep-out. Nest > Support Grid... edits it; Save as Machine Defaults writes
`%APPDATA%\OpenNest\support-grid.json`, which new nests receive while it is enabled. The
nest file stores it in `plateDefaults.supportBed` (see [nest file format](nest-file-format.md)).

## The support check

`SupportAnalyzer.Analyze` takes a placed part's material (`SupportMaterial`: outline minus
holes, chorded at 0.001; leads, rapids, scribes and break cuts are not material, and a tabbed
cutout's slug counts as part since it stays attached). Plate is of even thickness, so the
center of gravity is the area centroid. The part rests on the bed contacts inside its material;
the margin is the signed distance of the center of gravity inside the convex hull of those
contacts (negative outside). Fewer than three contacts that are not in line can only balance a
part, so their margin is at most zero; no contacts at all is negative infinity. A part is
supported when its worst margin is positive and at least the stability margin.

A perimeter tab holds the part at the tab, so a tab point counts as one more contact.
`PartSupport.MarginWith(point)` is the worst margin with a tab there, and `BestTabPoint` the
outline point (vertices plus 256 even samples) that maximizes it. `AcceptsTabAt(point)` accepts
any point that reaches the stability margin, or, when no single tab can, only points as good as
the best one found. A tab's bending strength is ignored: it is a support point, not a cantilever.

View > Draw Support shows the grid (while its points are at least 6 pixels apart), each part's
contacts, and its center of gravity: green when supported, red when it can tip, with an orange
diamond on a red part's best tab point.

## Support tabs in Plan Cutting

Cutting Settings > Tabs > "Tab parts the support grid cannot hold level" sets
`CuttingParameters.SupportTabs`. With it and a plate support bed, `CuttingPlanService` capture
analyzes each regenerated part (`SupportTabCapture`). A part the bed cannot hold level gets a
tabbed perimeter (`PreparedContours.WithSupportTab`), using `TabConfig` for the tab size; all
other parts keep the plate settings, so supported parts are not tabbed.

The emitter tabs a perimeter at its entry: the cut stops short of the entry by the tab size.
A support-tabbed perimeter's automatic entries are therefore limited to catalogue points the
analysis accepts, plus the closest native point to its best tab point (`AutomaticEntryKind.SupportTab`,
never on a reflex or cusp vertex). Ranking, lead checks and replay are unchanged. If none of
those entries has a lead-in that fits, the search falls back to the full catalogue and the part
is still tabbed, wherever a lead-in fits; this fallback is not reported yet.

Not covered yet:

- A drawing pin on the perimeter wins: its entry and tab are kept as pinned.
- Locked or ineligible parts keep their programs; the older Assign Lead-ins command and
  Auto Tab sizes ignore the bed.
- Only one perimeter tab per part. A part that needs more (one tab cannot reach the stability
  margin) gets the best single tab.
- Pre-post verification does not yet warn about unsupported, untabbed parts.
