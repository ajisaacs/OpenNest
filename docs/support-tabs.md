# Support grid and support tabs

A cut part drops onto the machine's support bed. If its center of gravity is not over the
points it rests on, it can tip into the head's path or fall through. OpenNest models the bed,
checks every part against it, and can tab only the parts the bed cannot hold level, at the
perimeter point where one tab steadies the part most.

## The support bed

`OpenNest.Support.SupportBed` is the machine's bed, fixed to the machine and measured from the
plate origin into the sheet; a sheet in another quadrant sees it mirrored. The built-in layout
is one of two layouts; a layout only reports its contacts under a part's material. Lengths are
in the nest's units.

- The laser grid, `SupportGrid`: one contact point every `PitchX` along X and `PitchY` along
  Y, the first at (`OffsetX`, `OffsetY`).
- Straight slats, `SupportSlats` (as on the plasma table): each slat a strip `Thickness` wide
  running the full length (`Axis` X) or width (`Axis` Y) of the table, one every `Pitch`
  across it, the first centered `Offset` from the origin. A part rests on the region where its
  material crosses a slat, so its contacts are that region's corners: where the outline and
  holes cross each slat's two edges, and the outline and hole vertices on a slat. A part
  narrower than a slat rests on its own corners; one balanced across a single slat has a margin
  of at most half the thickness, which the stability margin rejects.

Curved slats imported from a CAD layout are not modeled yet.

Every bed also carries:

- `RegistrationTolerance`: how far a sheet may sit from its nominal place. The bed is checked
  shifted by up to this much along X, Y and both diagonals, and the worst result is kept.
- `StabilityMargin`: how far inside its contacts the center of gravity must lie to count as
  supported, allowing for slag, gas pressure and worn contacts.

The bed belongs to the nest (`Nest.SetSupportBed`); every plate follows it, including plates
added later. It is display, analysis and tab placement only: never material, spacing or a
keep-out. Nest > Support Bed... edits it and picks the grid or slats; Save as Machine Defaults
writes it to the nest's machine configuration, which the Support Bed tab of Tools > Machine
Configuration also edits (`MachineConfig.SupportGrid`: both layouts' values
and which one is in use; settings saved before slats existed are a grid), which nests assigned that
machine receive while it is enabled: File > New and BOM import assign the Tools > Nest Defaults
machine (the first by name when none is chosen), and Nest > Machine (or the status bar's machine
label) reassigns a nest and replaces its bed with the new machine's (none when that machine gives
nests no bed). A machine with no bed saved yet uses the legacy shared
`%APPDATA%\OpenNest\support-grid.json`; the Support Bed tab shows those values and its first edit
stores them on the machine, converted to the machine's units. The nest file stores the bed in
`plateDefaults.supportBed` and the machine in `machine` (see [nest file format](nest-file-format.md)).

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

View > Draw Support shows the grid or the slats, cut off to the sheet (while its points or slats
are at least 6 pixels apart), each part's
contacts, and its center of gravity: green when supported, red when it can tip, with an orange
diamond on a red part's best tab point (keeping the plate's tab-size clearance). While Plan
Cutting inspects a proposal, the overlay shows the proposal's parts on the same bed.

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

## Parts the tab alone holds

A tabbed part stays in the sheet on its tab, and a part short enough does not need the bed at
all: the tab holds it wherever it goes. Cutting Settings > Tabs > "Tab alone holds parts up to"
sets `CuttingParameters.TabHeldMaxLength`: on a plate with a support bed, a part the bed cannot
hold level whose length (the long side of its outline's minimum-area bounding rectangle,
`SupportAnalyzer.Length`) is at most that, bound included, is still tabbed, but
`SupportAnalyzer.HeldByTab` marks its analysis `IsHeldByTab`: it keeps its contacts and margin,
every tab clear of inside corners steadies it, and its best tab point is chosen as on a plate
with no bed (below). A part the bed holds is not tabbed. 0 turns the rule off.
`SupportTabSettings.Analyze` applies it for Plan Cutting, hand-placed lead-ins and the pre-post
review alike, so such a part warns only when it is cut with no tab at all. View > Draw Support
does not read cutting settings and still shows it red, since it would tip without its tab.

## Plates without a support bed

With no bed there are no contacts to judge a part by. Cutting Settings > Tabs > "No bed: tab
widths" sets `CuttingParameters.NoBedTabMinWidth` to `NoBedTabMaxWidth`: on a plate with no
support bed, support tabs then tab every part whose width (the short side of its outline's
minimum-area bounding rectangle, `SupportAnalyzer.Width`) is in that range, bounds included. A
maximum of 0 turns the rule off. `SupportTabSettings.Analyze` makes this choice for Plan Cutting,
hand-placed lead-ins and the pre-post review alike; a plate with a bed never uses the range.

`SupportAnalyzer.AnalyzeWithoutBed` gives such a part a `PartSupport` with `HasBed` false: no
contacts, never supported, and every tab clear of inside corners steadies it, so the perimeter's
automatic entries are every catalogue point clear of inside corners plus the best tab point, the
clear outline point nearest the line through the center of gravity across the long side. Which
of those wins is left to the usual ranking toward the next cut.

With support tabs on, no bed and no range, nothing is tabbed and the plan carries one advisory
note saying so; its status is unchanged.

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

`PartSupportReview` runs in every pre-post verification and in Validate Nest. Each part whose
settings (its own, else the plate's) ask for support tabs and that needs one (the plate's bed
cannot hold it level or, with no bed, its width is in the no-bed range) is checked, however its
program was made. The tab is the gap between
the end and the start of the program's outside cut (the chain of cutting motions with the
largest extent). A part with no such gap, or whose gap does not steady it (without
a bed, or for a part its tab alone holds, any gap does), gets a `PartSupport` warning at its center of gravity naming the best tab point; posting needs
consent as for every warning. A part whose material cannot be read is reported as not checked.

Not covered yet:

- Locked or ineligible parts keep their programs in Plan Cutting; the pre-post review still
  checks them.
- Only one perimeter tab per part. A part that needs more (one tab cannot reach the stability
  margin) gets the best single tab and a warning.
