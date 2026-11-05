using System;
using System.Linq;

namespace OpenNest.CNC
{
    /// <summary>
    /// How a planned program cuts a common line once (see docs/common-line-cutting.md). A shared cut
    /// is a run of <see cref="LayerType.CommonLine"/> moves: an open centerline cut made without
    /// kerf compensation, with straight leads on its own axis. A remainder (a member's perimeter
    /// between its shared spans) is an open contour whose rapid is followed directly by one of the
    /// <see cref="RemainderMarker(RotationType)"/> comments, naming the winding of the perimeter it
    /// belongs to, so a post compensates it on the perimeter's side even though it encloses nothing.
    /// </summary>
    public static class CommonLineCuts
    {
        /// <summary>The comment after the rapid into a remainder of a clockwise perimeter.</summary>
        public const string ClockwiseRemainder = "COMMON LINE REMAINDER CW";

        /// <summary>The comment after the rapid into a remainder of a counterclockwise perimeter.</summary>
        public const string CounterclockwiseRemainder = "COMMON LINE REMAINDER CCW";

        /// <summary>The remainder comment for a perimeter wound <paramref name="winding"/>.</summary>
        public static string RemainderMarker(RotationType winding) =>
            winding == RotationType.CW ? ClockwiseRemainder : CounterclockwiseRemainder;

        /// <summary>The perimeter winding a remainder comment names, or null for any other code.</summary>
        public static RotationType? RemainderWinding(ICode code) => code switch
        {
            Comment { Value: ClockwiseRemainder } => RotationType.CW,
            Comment { Value: CounterclockwiseRemainder } => RotationType.CCW,
            _ => null,
        };

        /// <summary>Whether <paramref name="program"/>'s own codes cut a common line once.</summary>
        public static bool IsSinglePass(Program program) =>
            program?.Codes != null && program.Codes.Any(code =>
                code is LinearMove { Layer: LayerType.CommonLine } or ArcMove { Layer: LayerType.CommonLine }
                || RemainderWinding(code) != null);

        /// <summary>
        /// Refuses, before any output, a nest with a common line cut once when
        /// <paramref name="postProcessor"/> cannot write shared cuts and remainders: it would cut the
        /// shared cut with compensation (into a part) or a remainder on the wrong side. Posts never
        /// fall back to cutting the line twice; set the plate to cut its common lines twice instead.
        /// </summary>
        /// <exception cref="InvalidOperationException">The post cannot write the nest.</exception>
        public static void EnsurePostable(Nest nest, IPostProcessor postProcessor)
        {
            ArgumentNullException.ThrowIfNull(nest);
            if (postProcessor is ICommonLinePostProcessor)
                return;
            for (var p = 0; p < nest.Plates.Count; p++)
            {
                var plate = nest.Plates[p];
                for (var i = 0; i < plate.Parts.Count; i++)
                    if (IsSinglePass(plate.Parts[i]?.Program))
                        throw new InvalidOperationException(
                            $"Plate {p + 1}, part {i + 1} cuts a common line once, but the "
                            + $"{postProcessor?.Name ?? "selected"} post cannot write single-pass common lines. "
                            + "Set the plate to cut its common lines twice (Plate > Arrange > Cut Common Lines Twice), "
                            + "plan cutting again, then post.");
            }
        }
    }

    /// <summary>
    /// A post that writes common lines cut once (<see cref="CommonLineCuts"/>): each shared cut
    /// without kerf compensation and each remainder compensated on its perimeter's side. Posts
    /// without it refuse such a nest (<see cref="CommonLineCuts.EnsurePostable"/>).
    /// </summary>
    public interface ICommonLinePostProcessor : IPostProcessor
    {
    }
}
