# Cut-time estimates

Calculate Cut Time and Plate > Simulate Cutting share one timing model
(`OpenNest.Core/CNC/CutSimulation.cs`, `ContourTiming.cs`, `MotionPlanner.cs`), and the
last-used settings are shared through `SavedCutParameters`.

## Inputs

- **Outside feed, hole feed, rapid and pierce** from the dialog or simulator. Inch rates are per
  minute; millimeter rates are per second (the existing `Timing.CalculateTime` convention).
- **Skip etch-only contours** (on by default) leaves out contours that only scribe, as the
  CI Fiber post's `SkipScribe` does, and joins the travel around them.
- **Acceleration** from a machine in Tools > Machine Configuration (`MachineConfig.Motion`,
  in the machine's units and converted to the nest's). The choice is remembered in the
  `CutTimeMachine` user setting. Until any machine is chosen, the first machine is used.
  "None (full speed)" gives the old constant-speed estimate.
- A machine with no saved motion values uses `MotionConfig.Placeholder`: about 0.5 G cutting,
  1 G travel and a 0.001 in (0.025 mm) corner deviation. These are stand-ins, not measured
  values, and the picker labels such machines "(placeholder)".

## Model

- Moves are walked in plate cutting order. Cut, Display, lead-in/out and Break moves cut; cutting
  strategies emit contour moves on Display, and the posts cut them. Rapids and Tab spans are
  head-up travel, and suppressed moves are left out. A cut that follows travel gets a pierce
  dwell. Scribes are not pierced.
- Contours follow the CI Fiber post's interior/exterior (`L2`/`L4`) rule: a cut-off, or a
  closed contour wound clockwise (sampled without its lead moves), takes the outside feed.
  Everything else, including open pieces, takes the hole feed. The CI Fiber post writes no feeds,
  so these are stand-ins for the controller's process tables until calibrated.
- Each cutting run (between a pierce and the next travel) starts and ends at rest. Travel moves
  are planned separately and also go from rest to rest.
- Each move follows a trapezoidal profile: it accelerates, cruises at the feed and decelerates. A
  move too short to reach the feed peaks where the two ramps meet.
- Corner speed uses the junction-deviation formula `v² = a·δ·sin(θ/2) / (1 − sin(θ/2))`. A zero
  deviation stops at every corner. Collinear joints do not slow.
- Arcs are capped at the centripetal limit `√(a·r)`, using the cut acceleration.
- A backward pass and then a forward pass keep every speed change within the acceleration
  (Grbl-style lookahead).

Not modeled: jerk (S-curves), controller-specific lookahead limits, feeds from controller
process tables beyond the outside/hole split, an etch feed (etch kept in the timing runs at the
outside feed), and power ramping. With real controller values, expect roughly 5–15% error. Calibrate by comparing
a known program against the machine's own run time.

The plate time is the simulation total plus clamp-removal minutes, times the plate quantity.
Distances and counts in the timing dialog still come from `Timing.GetTimingInfo`, which counts
only Cut-layer moves as cut distance.
