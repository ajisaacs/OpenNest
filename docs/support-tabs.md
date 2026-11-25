# Support grid and support tabs

A cut part drops onto the machine's support bed. If its center of gravity is not over the
points it rests on, it can tip into the head's path or fall through. OpenNest models the bed,
checks every part against it, and can tab only the parts the bed cannot hold level, at the
perimeter point where the cutting route enters the part.

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

Cutting Parameters > Tabs > "Add tabs to hold parts in the sheet" sets
`CuttingParameters.SupportTabs`, and its two choices say which parts need a tab. Like every
cutting setting they are kept per machine thickness (Tools > Machine Configuration > Tabs &
Pierce), so thick plate can go untabbed; see [machine cutting defaults](machine-cutting-defaults.md):

- "Only tab parts the support grid can't hold level": the plate's support bed decides. A plate
  with no bed falls back to the size range ([below](#tabbing-by-size)).
- "Tab parts by size" (`CuttingParameters.TabBySize`): the bed is ignored and the size range
  decides on every plate.

There is no setting that tabs every part: the former "Tab every part" choice is gone (see
[migration](#settings-saved-with-tab-every-part)). `CuttingParameters.TabsEnabled`, which tabs
every contour cut with those parameters, remains only on the copies Plan Cutting, Place Lead-in
and drawing pins give the parts they tab, and on parameters recorded that way.

With support tabs on, `CuttingPlanService` capture analyzes each regenerated part
(`SupportTabCapture`). A part that needs a tab gets a tabbed perimeter
(`PreparedContours.WithSupportTab`), using `TabConfig` for the tab size; all other parts keep the
plate settings, so parts that need no tab are not tabbed.

The emitter tabs a perimeter at its entry: the cut stops short of the entry by the tab size. A
round perimeter, one circle, is cut as an arc that stops the tab's chord short of its entry.
The lead-out leaves from where the cut stops, as configured; Plan Cutting never shortens or drops
it. A line lead-out angled forward runs toward the lead-in whenever its run along the edge is
longer than the tab, as a 0.1 lead-out at 60 degrees (0.05 along the edge) beside a 0.03 tab
does on a counterclockwise outside, and can then cut the tab free. The strict search refuses
such an entry and looks for another. The check measures each lead against the gap between the
outside cut's two ends: at a convex corner the cut starts on one edge and the gap lies along the
other, so a straight in-line corner lead-in, which runs back along its first edge's line off the
part, is clear of the tab. A cutout cut in pieces around several tabs keeps measuring along each
piece's own ends. Only when that refusal is all that stops a plan does Plan
Cutting plan again keeping those lead-outs (`CuttingPlanSnapshot.WithTabLeadOuts`): each such
part gets a `PostVerificationKind.TabLeadOut` warning (Plan Cutting's "Tab lead-out" issue), is
treated as cut free for later rapids, and the plan is UNVERIFIED, so applying it needs explicit
acceptance. A lead-out that would cut its tab is fixed in the cutting settings: shorten or turn
it, or enlarge the tab. Programs judged as they are (locked, fixed or posted) keep the
incomplete-check finding.
A support-tabbed perimeter's automatic entries are every catalog point clear of inside corners,
plus the closest native point to its best tab point (`AutomaticEntryKind.SupportTab`, never on a
reflex or cusp vertex), ranked toward the next cut like any untabbed part's, in the search and in
contour recovery. How well the tab would steady the part does not rank them: the route matters
more. A tab holds most parts wherever it is, and one that does not is still tabbed and avoided
once cut, while a detour to the steadiest point lengthens the way for every part. Led by the
steadiest instead, a long strip resting on one line of contacts started mid-length, or on its
side away from the next cut, so the head left it back over the strip. Lead checks are unchanged.

## Tabbed parts are held by their tab

A tabbed part stays in the sheet on its tab, however long it is. On a plate with a support bed,
`SupportTabSettings.Analyze` tabs every part the bed cannot hold level and
`SupportAnalyzer.HeldByTab` marks its analysis `IsHeldByTab`: it keeps its contacts and margin,
every tab clear of inside corners steadies it, and its best tab point is chosen as for a part
tabbed by size (below). A part the bed holds is not tabbed. The former length limit ("Tab alone
holds parts up to", `TabHeldMaxLength`) is gone; settings and nests saved with it read
unchanged and ignore it. `SupportTabSettings.Analyze` applies this for Plan Cutting,
hand-placed lead-ins and the pre-post review alike, so such a part warns only when it is cut
with no tab at all. View > Draw Support does not read cutting settings and still shows it red,
since it would tip without its tab.

Some outlines have two minimum-area rectangles (a right isosceles triangle fits both its legs'
square and its hypotenuse's 2:1 box), and which one rounding finds depends on how the part is
turned; of rectangles tied for area, `SupportAnalyzer` measures the one with the shorter long
side, so a drawing's width is the same at every rotation.

## Tabbing by size

Cutting Parameters > Tabs > "By size, or with no support bed, tab parts whose narrow side is" sets
`CuttingParameters.TabSizeMinWidth` to `TabSizeMaxWidth` (stored under their earlier JSON names
`noBedTabMinWidth` and `noBedTabMaxWidth`, so saved settings and nests read unchanged). Support
tabs go by size under "Tab parts by size", and under the support grid choice on a plate with no
bed, where there are no contacts to judge a part by (`SupportTabSettings.UsesBed`). They then tab
every part whose width (the short side of its outline's minimum-area bounding rectangle,
`SupportAnalyzer.Width`) is in that range, bounds included. That rectangle may be turned at any
angle, so a drawing is tabbed or not however the nest rotated it. A maximum of 0 turns the rule
off. `SupportTabSettings.Analyze` makes this choice for Plan Cutting, hand-placed lead-ins and
the pre-post review alike; the support grid choice never uses the range on a plate with a bed.

`SupportAnalyzer.AnalyzeWithoutBed` gives such a part a `PartSupport` with `HasBed` false: no
contacts, never supported, and every tab clear of inside corners steadies it, so the perimeter's
automatic entries are every catalog point clear of inside corners plus the best tab point, the
clear outline point nearest the line through the center of gravity across the long side. Which
of those wins is left to the usual ranking toward the next cut.

When tabs go by size and no range is set, nothing is tabbed and the plan carries one advisory
note saying so (`SupportTabSettings.SkipsForNoRange`); its status is unchanged.

### Settings saved with "Tab every part"

The user's saved cutting settings with the former "Tab every part" (`TabsEnabled` on,
`SupportTabs` off) read as "Tab parts by size" from 0 to 9999 (`SupportTabSettings.MigrateTabEveryPart`),
which tabs the same parts. This happens only where the user's settings are read (the saved
settings and the cutting panel): a plate's recorded parameters and drawing pins keep
`TabsEnabled` and replay as planned.

If none of those entries has a lead-in that fits, the part is still tabbed. The search falls
back to every entry (`SupportEntries.Any`), inside corners included; since every tab holds a
tabbed part, the route decides among those whose lead-in fits. The open-scrap pierce fallback
then tries that catalog.

A tabbed part counts as steady wherever its tab is: where its tab lands is never a warning. No message says a tab cannot steady its part, and once cut a tabbed part stays
crossable, as its retained material allows, whether or not its tab holds it level. Plan Cutting
still routes around tabbed parts where it can and passes over only those it finds no way around
(see [tabbed parts are avoided first](cutting-planner.md#tabbed-parts-are-avoided-first)). The pre-post
review does not read support settings for rapids, so it checks rapid crossings against untabbed
contours only.

A drawing pin on the perimeter wins: its entry, leads and tab are kept as pinned, and the
analysis is only carried for review. Replay and best-effort plans (`SupportTabCapture.Review`)
warn with `PostVerificationKind.PartSupport` only when an unsupported part's emitted perimeter
is untabbed, which happens when its pin has no tab. Like every planner warning, this makes the
proposal UNVERIFIED, so applying it needs explicit acceptance.

A planned tab that sits in an inside corner, because no lead-in fits clear of one, gets an
advisory note instead (`SupportTabCapture.Notes`; never for a pin): a Ready plan keeps its
status and lists it in Plan Cutting's warnings, and the console and MCP summaries print it under
the plate.

## Slug slices

A cutout's slug drops onto the bed too, and one the bed cannot hold level can tip up into the
head's path. Cutting Parameters > Cutouts > "Slice slugs the support grid can't hold level" sets
`CuttingParameters.SliceTippingSlugs` (off by default, kept per machine thickness like every
cutting setting, and written to settings and nest files only when on). With it on and a support
bed, Plan Cutting cuts each such slug into pieces before it cuts the cutout, so the pieces drop
between the supports instead of the whole slug tipping.

- Which slugs: `SlugSlicer.Tips` analyzes the cutout's inside as material on the bed (as above,
  at the part's placed position). A slug the bed holds level is not sliced, and nor is one that
  rests on no contact, which falls through whole.
- Where: along the bed's slice lines (`SupportBed.SliceLines`). A grid's are midway between its
  rows and between its columns of points, so no slice cuts over a point and into the grid: each
  piece is at most one pitch square on a single point, which cannot hold it, so it drops off. A
  slug no midline crosses (smaller than a pitch, between two of them) is left whole. A slat bed's
  are its slat center lines, so its pieces are strips between slats that rest on no slat.
- How far: each slice stops `CuttingParameters.Kerf` short of the cutout (measured from the
  cutout chorded at 0.001, eroded with round corners), so the pieces stay attached to the part
  until the cutout itself is cut, its kerf meets the slices' ends and they all drop together. No
  kerf, no slices. A slice no longer than the kerf is left out.
- Which cutouts: one that holds another of the part's cuts (a contour, break cut or tabbed
  cutout), or whose box holds another part's box (a part nested in it), is never sliced, since a
  slice would cut what is inside. Tabbed cutouts keep their slugs, so they are never sliced.
- Cutting: the slices are break cuts (`LayerType.Break`) inside the slug: never material, never
  rapid obstacles, cut by every post that cuts break cuts. `PreparedContours.WithSlugSlices` keeps
  each cutout's in cutting order, and every emission cuts them directly before that cutout, so
  a slug is sliced and dropped in one visit and never left sliced while the head cuts elsewhere.
  The grid's
  rows come first, then its columns, each set from its end nearer the last slice, every slice
  started at its end nearer where the one before ended. Each is pierced on itself, the cutouts'
  lead-in reach (but at least the pierce clearance, and at most halfway) along it, and led
  straight back to its start; it has no lead-out. That pierce must keep the larger of the
  effective pierce clearance and the kerf from the cutout, from one end or the other; a slice
  whose pierce fits at neither is left out.
- Checks: inner grid cells are cut free as soon as their four slices are cut. The released-contour
  check's open-cut proof therefore leaves out a straight break cut, leads included, that lies
  wholly inside one of the part's closed holes (any closed cut but its largest) and touches none
  of it: what it encloses is that hole's slug. Break cuts in material still take part in the proof.

The hole's own lead-in is chosen as usual and may pierce in a slice's kerf or a piece already
dropped. Slices are planned for the nominal bed: on slats, a sheet registered off by up to its
tolerance can leave a contact just inside a piece's edge.

## Lead-ins placed by hand

Plate > Place Lead-in cuts where it is clicked. With tabs on, `SupportTabSettings.ForManualLeadIn`
enables tabs on a copy of the settings when the clicked part needs one (the plate's bed cannot
hold it level or, by size, its width is in the range), so the tab sits at the clicked entry. The
overlay's orange diamond shows where it steadies the part; the pre-post review checks the
result. Plan Cutting is the only automatic lead-in assignment.

## Pre-post review

`PartSupportReview` runs in every pre-post verification and in Validate Nest. Each part whose
settings (its own, else the plate's) ask for support tabs and that needs one (the plate's bed
cannot hold it level or, by size, its width is in the size range) is checked, however its
program was made. The tab is the gap between
the end and the start of the program's outside cut (the chain of cutting motions with the
largest extent). A part with no such gap gets a `PartSupport` warning at its center of gravity
naming the best tab point; posting needs consent as for every warning. Any gap counts as a tab
that steadies the part, wherever it is. A part whose material cannot be read is reported as not
checked.

Not covered yet:

- Locked or ineligible parts keep their programs in Plan Cutting; the pre-post review still
  checks them.
- Only one perimeter tab per part. A part that needs more (one tab cannot reach the stability
  margin) gets the best single tab, and no warning.
