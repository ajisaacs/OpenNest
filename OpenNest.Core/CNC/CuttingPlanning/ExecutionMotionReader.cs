using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Guarded read of stable placed programs; never clones or mutates their graphs.</summary>
public static class ExecutionMotionReader
{
    public static OwnedExecution Read(Program program, Vector origin, Vector? previous,
        CancellationToken token)
    {
        var moves = new List<ExecutionMotion>();
        var visiting = new HashSet<Program>(ReferenceEqualityComparer.Instance);
        var budget = 1000000;
        Walk(program, origin, previous);
        return new OwnedExecution(moves);

        Vector Walk(Program current, Vector frame, Vector? arrival)
        {
            token.ThrowIfCancellationRequested();
            PostVerificationGeometry.Validate(frame);
            if (current?.Codes == null || !visiting.Add(current) || visiting.Count > 64)
                throw new ArgumentException("Missing, recursive or excessively nested program.");
            var pos = frame;
            var first = true;
            var countBefore = moves.Count;
            foreach (var code in current.Codes)
            {
                token.ThrowIfCancellationRequested();
                if (--budget < 0)
                    throw new ArgumentException("Program expansion exceeds the verification limit.");
                if (code == null)
                    throw new ArgumentException("Program contains a missing instruction.");
                if (code is SubProgramCall call)
                {
                    if (!double.IsFinite(call.Rotation))
                        throw new ArgumentException("Subprogram rotation is not finite.");
                    // Call rotation is baked into the shared program by its setter. Do not
                    // rotate again; offsets are frame-relative even in incremental mode.
                    pos = Walk(call.Program, frame + call.Offset, first ? arrival : pos);
                    first = false;
                    continue;
                }
                if (code is not Motion motion)
                {
                    if (code is not (Comment or Feedrate or Kerf))
                        throw new NotSupportedException("Unsupported program instruction.");
                    continue;
                }
                // Posts disagree about incremental position after suppressed instructions.
                // Never silently certify a trajectory whose semantics are ambiguous.
                if (motion.Suppressed)
                    throw new NotSupportedException("Suppressed motion requires post-specific verification.");
                if (motion is not (RapidMove or LinearMove or ArcMove))
                    throw new NotSupportedException("Unsupported motion.");
                var reference = current.Mode == Mode.Incremental ? pos : frame;
                var end = reference + motion.EndPoint;
                PostVerificationGeometry.Validate(end);
                var rapid = motion is RapidMove;
                if (first && !rapid)
                    moves.Add(new(arrival, frame, true, LayerType.Display, null));
                var start = first && rapid ? arrival : pos;
                var layer = motion switch
                {
                    LinearMove line => line.Layer,
                    ArcMove arc => arc.Layer,
                    _ => LayerType.Display
                };
                if (!Enum.IsDefined(layer))
                    throw new NotSupportedException("Unsupported motion layer.");
                if (motion is ArcMove direction && !Enum.IsDefined(direction.Rotation))
                    throw new NotSupportedException("Unsupported arc direction.");
                var curve = rapid ? null : PostVerificationGeometry.Curve.Create(pos, end,
                    motion is ArcMove arcMove ? reference + arcMove.CenterPoint : null,
                    motion is ArcMove { Rotation: RotationType.CW });
                moves.Add(new(start, end, rapid, layer, curve));
                pos = end;
                first = false;
            }
            visiting.Remove(current);
            if (moves.Count == countBefore)
                throw new ArgumentException("Program has no motions.");
            return pos;
        }
    }

}
