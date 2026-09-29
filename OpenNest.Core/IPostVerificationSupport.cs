namespace OpenNest;

/// <summary>
/// Optional post contract for nest-level rapid verification. Opt in only when the
/// post preserves Plate.Parts order and each placed Program's contour order and
/// pierce positions. This does not certify retract height, parking or controller macros.
/// Unknown/reordering posts still get nest diagnostics, but require acknowledgment
/// that their final rapid sequence has not been verified.
/// </summary>
public interface IPostVerificationSupport
{
    bool PreservesPlacedProgramOrder { get; }
}
