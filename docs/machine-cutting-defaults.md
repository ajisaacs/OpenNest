# Machine cutting defaults

A nest's assigned machine (`Nest.Machine`, see [nest file format](nest-file-format.md))
supplies the starting lead-ins, lead-outs, kerf and cut-off settings for the nest's
material and thickness, from that machine's Tools > Machine Configuration entry (in
Database mode, the server's [shared machines](shared-settings.md) once it holds them).
`MachineCuttingDefaults` (OpenNest.Data) holds the mapping; the desktop applies it.

## Lookup

The machine's material must match the nest's material name, and its grade when the nest
has one (otherwise the first grade on file). The thickness matches within 0.001 in after
converting the nest's thickness to the machine's units, so a thickness rounded to three
decimals (PEP stores 3/16 as 0.188) finds the machine's 0.1875; the nearest one wins. With no match, or no machine on
file, nothing changes and the previous behavior applies. Lengths are converted from the
machine's units to the nest's.

## Lead-ins and lead-outs

A plate with no cutting parameters of its own (Plan Cutting, Place Lead-in) starts from the
last applied parameters, as before, with the machine's leads and kerf laid over them and the
machine, material, grade and thickness recorded on the parameters. When the last parameters
already record the same machine, material, grade and thickness, the machine's settings are not
laid over them again, so lead and tab edits made for that job stick; the cutting panel keeps the
record (and the kerf) through an edit. Plan Cutting saves its settings as the last parameters as
soon as its settings dialog is accepted, not only on Apply. A plate that already has
parameters keeps them: changing the machine, material or thickness does not rewrite leads
the plate was planned with.

The machine holds one lead-in and one lead-out per thickness; both go to external and internal
contours (internal covers full circles). Types map as follows:

| Machine type | Lead-in |
|---|---|
| None | none |
| Line | line (length, angle) |
| Arc | arc (radius; the default 180-degree half-circle sweep) |
| Line + Arc | line-arc (length, radius, angle) |
| Clean Hole | clean hole (length, radius, the thickness's kerf), holes only: the external lead-in is kept |
| Line + Line | not mapped (the configuration has no second segment): every lead-in is kept |

Lead-outs map None, Line (length, angle) and Arc (radius); any other type is none.

## Cut-offs

The plate view's part clearance, overtravel, minimum segment length and direction come from
the machine's cut-off settings when a nest opens, when Nest > Machine changes the machine and
after Nest > Edit saves. The head-locked overcut choice is kept. Existing cut-offs are not
regenerated at that moment; they follow the settings the next time they regenerate.
