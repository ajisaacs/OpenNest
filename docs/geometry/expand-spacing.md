# Expand Spacing

`Plate > Expand Spacing` moves selected parts by translation only. The first selected part is the anchor for selected pairs; the later selected member moves, and nonselected parts remain fixed. The plate retains its own `EdgeSpacing` floor; the command does not change `PartSpacing` or `EdgeSpacing`.

Every selected/selected and selected/nonselected pair has an operation-entry clearance floor, measured against material with cutouts subtracted. A later move may reuse slack opened by an earlier move, but it cannot reduce a pair below its own starting gap. Existing overlaps are accepted for attempted separation; a remaining overlap is reported as a violation, not a successful positive gap. Temporarily blocked pairs are retried after another part moves, while a pass without positive movement stops the relaxation.

Probes run on scratch poses. Cancellation before application leaves the live layout unchanged; `Separate` never changes it. The desktop command is synchronous and has no Cancel dialog or automatic save. A bounded search reports the spacing it found, not an optimum. See `docs/geometry/directional-slides.md` for directional contact behavior.
