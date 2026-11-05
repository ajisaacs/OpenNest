# Machine cutting defaults

A nest's assigned machine (`Nest.Machine`, see [nest file format](nest-file-format.md))
supplies the lead-ins, lead-outs, kerf, lead-in angle rounding, tabs, pierce, cut-off settings
and spacing for the nest's material and thickness, from that machine's Tools > Machine Configuration entry (in
Database mode, the server's [shared machines](shared-settings.md) once it holds them).
`MachineCuttingDefaults` (OpenNest.Data) holds the mapping; the desktop applies it.

## Lookup

The machine's material must match the nest's material name, and its grade when the nest
has one (otherwise the first grade on file). The thickness matches within 0.001 in after
converting the nest's thickness to the machine's units, so a thickness rounded to three
decimals (PEP stores 3/16 as 0.188) finds the machine's 0.1875; the nearest one wins. With no match, or no machine on
file, nothing changes and the previous behavior applies. Lengths are converted from the
machine's units to the nest's.

## Where the Cutting Parameters dialog saves

Every setting in Plan Cutting's Cutting Parameters dialog and the Place Lead-in panel is kept on
the nest's machine thickness: leads on the Outside Cuts and Inside Cuts tabs, lead-in angle
rounding, tabs and pierce on the Tabs & Pierce tab (`ThicknessConfig.Cutting`). So 0.25 in can
tab parts while 0.5 in tabs none, and in Database mode every workstation gets the same
settings. The dialog names what it edits above its fields ("Saved for Fiber 1 · Mild Steel A36
0.25 in") or why it edits this PC's settings instead (no machine, no material or thickness, or
a thickness the machine does not list).

Plan Cutting and Place Lead-in start from this PC's last settings (`CuttingParametersJson` in
user-settings.json), with the machine's leads and kerf laid over them, and its rounding, tabs and
pierce when the thickness sets them (a thickness whose Tabs & Pierce box is unchecked keeps this
PC's), and the machine, material, grade and thickness recorded on the parameters. A plate's own
parameters are used only when no user parameters are saved or readable, so an edit reaches nests
already planned.

The dialog saves when it is accepted and when a plan is applied, and Place Lead-in when its panel
closes after an edit. With a matching thickness the settings are written to the machine
(`MachineCuttingDefaults.WriteTo`, converted to the machine's units; inside cuts equal to the
outside ones keep following them, and kerf, cut-off, spacing and plate sizes are untouched), and
this PC's settings are left alone, so a thin job's tabs never become another job's. Matched, the
dialog offers only the leads a thickness can hold: no Line + Line, no arc sweep, no outside Clean
Hole. Without a matching thickness the settings are saved as this PC's.

When the machine cannot be saved (the nest server is offline, someone else changed the machine,
or the thickness was removed), the settings are saved as this PC's, recorded for that machine,
material, grade and thickness, and the user is told. While they are recorded for that job the
machine's settings are not laid over them, so the edit sticks on this PC; the next successful
save to the machine clears the record. Changing the settings, machine, material or thickness does
not rewrite the leads a plate was planned with: its programs and stored parameters stay until a
plan is applied.

Tools > Machine Configuration's thickness menu has Copy Tabs & Pierce To..., which copies one
thickness's Tabs & Pierce (or their absence) to the thicknesses picked, with Thinner and Thicker
shortcuts.

Each thickness holds a lead-in and lead-out for outside cuts (the Outside Cuts tab, external
contours) and, optionally, its own pair for inside cuts (the Inside Cuts tab, internal contours,
full circles included). Inside cuts use the outside pair until "Same as outside cuts" is cleared,
which starts them from a copy of it; configurations saved before the split have no inside pair
and keep applying one pair to both. Both tabs use the shared `LeadsEditor` control that Cutting
Settings uses, without the fields the configuration cannot hold (an arc's sweep; a clean hole's kerf,
which comes from the thickness). Types map as follows:

| Machine type | Lead-in |
|---|---|
| None | none |
| Line | line (length, angle) |
| Arc | arc (radius; the default 180-degree half-circle sweep) |
| Line + Arc | line-arc (length, radius, angle) |
| Clean Hole | clean hole (length, radius, the thickness's kerf), inside cuts only; an outside Clean Hole (older configurations) keeps the external lead-in |
| Line + Line | not offered; an older configuration's is not mapped (it has no second segment): the lead-in it would set is kept |

Lead-outs map None, Line (length, angle) and Arc (radius); any other type is none.

## Spacing

Each thickness can set a part spacing and an edge spacing (the Spacing tab), each optional:
one left unset stays at the nest defaults' value. The machine's spacing replaces the nest's
plate defaults when Nest > Edit or Nest Information changes the material, grade or thickness
(a spacing edited in the same dialog stays), when Nest > Machine changes the machine, and when a
drawing set release creates a nest. BOM import starts each group's row from it. File > New has
no material yet, so a new nest starts from the nest defaults; adding a library drawing to a nest
with no material or thickness gives it the record's (`LibraryDrawingInsert`), and with them this
spacing. Drawings from files carry no thickness, so they leave the nest blank. Plates are kept as they are, except
an untouched trailing empty sheet, which follows the plate defaults.

## Cut-offs

The plate view's part clearance, overtravel, minimum segment length and direction come from
the machine's cut-off settings when a nest opens, when Nest > Machine changes the machine and
after Nest > Edit saves. The head-locked overcut choice is kept. Existing cut-offs are not
regenerated at that moment; they follow the settings the next time they regenerate.
