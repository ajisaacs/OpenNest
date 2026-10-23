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
keep-out. Nest > Support Grid... edits it; Save as Machine Defaults writes it to the nest's
machine configuration (`MachineConfig.SupportGrid`), which nests assigned that machine receive
while it is enabled: File > New and BOM import assign the Tools > Nest Defaults machine (the
first by name when none is chosen), and Nest > Machine (or the status bar's machine label) reassigns a nest and replaces its grid
with the new machine's (none when that machine gives nests no grid). A machine with no grid
saved yet uses the legacy shared `%APPDATA%\OpenNest\support-grid.json`. The nest file stores
the bed in `plateDefaults.supportBed` and the machine in `machine` (see
[nest file format](nest-file-format.md)).

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
outline point (vertices plus 256 even samples) that maximizes it. A tab cannot lift the margin
past the contacts' own hull edges, so a whole stretch of outline often ties; among samples
within `Tolerance.Epsilon` of the best, the one nearest the line through the center of gravity
across the long side of the outline's minimum-area bounding rectangle wins. The search itself
is not narrowed, so a part held only near one end is still tabbed at the far end. `AcceptsTabAt(point)` accepts
any point that reaches the stability margin, or, when no single tab can, only points as good as
the best one found. A tab's bending strength is ignored: it is a support point, not a cantilever.

A tab never goes in an inside corner, where its nub cannot be ground flush. An inside corner is
a run of consecutive inward-turning outline vertices that turn at least 30 degrees together, so a
chorded fillet counts while a shallow bend does not. Tab points keep the analysis's inside-corner
clearance from those vertices (Plan Cutting and the overlay use twice the tab size; 0 keeps a tab
off the corner only), and `AcceptsTabAt` rejects closer points. The tie-break above runs over
the clear points only; points equally near the center line go to the one farthest from an
inside corner. Only when every outline point is too close does the clearance give way.

View > Draw Support shows the grid (while its points are at least 6 pixels apart), each part's
contacts, and its center of gravity: green when supported, red when it can tip, with an orange
diamond on a red part's best tab point (keeping the plate's tab-size clearance).

## Support tabs in Plan Cutting

Cutting Settings > Tabs > "Tab parts the support grid cannot hold level" sets
`CuttingParameters.SupportTabs`. With it and a plate support bed, `CuttingPlanService` capture
analyzes each regenerated part (`SupportTabCapture`). A part the bed cannot hold level gets a
tabbed perimeter (`PreparedContours.WithSupportTab`), using `TabConfig` for the tab size; all
other parts keep the plate settings, so supported parts are not tabbed.

The emitter tabs a perimeter at its entry: the cut stops short of the entry by the tab size.
A support-tabbed perimeter's automatic entries are therefore limited to catalogue points the
analysis accepts, plus the closest native point to its best tab point (`AutomaticEntryKind.SupportTab`,
never on a reflex or cusp vertex). Ranking and lead checks are unchanged.

If none of those entries has a lead-in that fits, the part is still tabbed. The search falls
back first to every catalogue entry clear of inside corners, then to every entry
(`SupportEntries`), each ordered by how well the tab steadies the part; of the entries whose
lead-in fits, it keeps every one that holds the part level, or else only the steadiest. The
open-scrap pierce fallback then tries the last of those catalogues.

A drawing pin on the perimeter wins: its entry, leads and tab are kept as pinned, and the
analysis is only carried for review. Replay and best-effort plans (`SupportTabCapture.Review`)
warn with `PostVerificationKind.PartSupport` whenever an unsupported part's emitted perimeter is
untabbed or its entry does not steady the part: its pin has no tab or a tab elsewhere, no
steadying entry had a lead-in that fits, or no single tab can steady it. Like every planner
warning, this makes the proposal UNVERIFIED, so applying it needs explicit acceptance.

A planned tab that sits in an inside corner, because no lead-in fits clear of one, gets an
advisory note instead (`SupportTabCapture.Notes`; never for a pin): a Ready plan keeps its
status and lists it in Plan Cutting's warnings, and the console and MCP summaries print it under
the plate.

## Lead-ins placed by hand

Plate > Place Lead-in cuts where it is clicked. Under "Tab parts the support grid cannot hold
level", `SupportTabSettings.ForManualLeadIn` enables tabs on a copy of the settings when the
plate's bed cannot hold the clicked part level, so the tab sits at the clicked entry. The
overlay's orange diamond shows where it steadies the part; the pre-post review checks the
result. Plan Cutting is the only automatic lead-in assignment.

## Pre-post review

`PartSupportReview` runs in every pre-post verification and in Validate Nest. On a plate with a
support bed, each part whose settings (its own, else the plate's) ask for support tabs and that
the bed cannot hold level is checked, however its program was made. The tab is the gap between
the end and the start of the program's outside cut (the chain of cutting motions with the
largest extent). A part with no such gap, or whose gap does not steady it, gets a
`PartSupport` warning at its center of gravity naming the best tab point; posting needs
consent as for every warning. A part whose material cannot be read is reported as not checked.

Not covered yet:

- Locked or ineligible parts keep their programs in Plan Cutting; the pre-post review still
  checks them.
- Only one perimeter tab per part. A part that needs more (one tab cannot reach the stability
  margin) gets the best single tab and a warning.
