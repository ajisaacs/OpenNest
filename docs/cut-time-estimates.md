# Cut-time estimates

Calculate Cut Time and Plate > Simulate Cutting share one timing model
(`OpenNest.Core/CNC/CutSimulation.cs`, `MotionPlanner.cs`).

## Inputs

- **Feed, rapid and pierce** from the dialog or simulator. Inch rates are per minute;
  millimeter rates are per second (the existing `Timing.CalculateTime` convention).
- **Acceleration** from a machine in Tools > Machine Configuration (`MachineConfig.Motion`,
  in the machine's units and converted to the nest's). The choice is remembered in the
  `CutTimeMachine` user setting. Until any machine is chosen, the first machine is used.
  "None (full speed)" gives the old constant-speed estimate.
- A machine with no saved motion values uses `MotionConfig.Placeholder`: about 0.5 G cutting,
  1 G travel and a 0.001 in (0.025 mm) corner deviation. These are stand-ins, not measured
  values, and the picker labels such machines "(placeholder)".

## Model

- Moves are walked in plate cutting order. Rapids, Display-layer moves and Tab spans are head-up
  travel. A cut that follows travel gets a pierce dwell. Scribes are not pierced.
- Each cutting run (between a pierce and the next travel) starts and ends at rest. Travel moves
  are planned separately and also go from rest to rest.
- Each move follows a trapezoidal profile: it accelerates, cruises at the feed and decelerates. A
  move too short to reach the feed peaks where the two ramps meet.
- Corner speed uses the junction-deviation formula `v² = a·δ·sin(θ/2) / (1 − sin(θ/2))`. A zero
  deviation stops at every corner. Collinear joints do not slow.
- Arcs are capped at the centripetal limit `√(a·r)`, using the cut acceleration.
- A backward pass and then a forward pass keep every speed change within the acceleration
  (Grbl-style lookahead).

Not modeled: jerk (S-curves), controller-specific lookahead limits, per-move feed overrides and
power ramping. With real controller values, expect roughly 5–15% error. Calibrate by comparing
a known program against the machine's own run time.

With acceleration, the plate time is the simulation total plus clamp-removal minutes, times the
plate quantity. Distances and counts in the timing dialog still come from `Timing.GetTimingInfo`.
