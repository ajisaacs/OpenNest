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

Plan Cutting and Place Lead-in start from the user's last applied parameters, with the machine's
leads and kerf laid over them and the machine, material, grade and thickness recorded on the
parameters. A plate's own parameters are used only when no user parameters are saved or readable,
so an edit reaches nests already planned. When the last parameters
already record the same machine, material, grade and thickness, the machine's settings are not
laid over them again, so lead and tab edits made for that job stick; the cutting panel keeps the
record (and the kerf) through an edit. Plan Cutting saves its settings as the last parameters as
soon as its settings dialog is accepted, not only on Apply. Changing the settings, machine,
material or thickness does not rewrite the leads a plate was planned with: its programs and
stored parameters stay until a plan is applied.

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

## Cut-offs

The plate view's part clearance, overtravel, minimum segment length and direction come from
the machine's cut-off settings when a nest opens, when Nest > Machine changes the machine and
after Nest > Edit saves. The head-locked overcut choice is kept. Existing cut-offs are not
regenerated at that moment; they follow the settings the next time they regenerate.
