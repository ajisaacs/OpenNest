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
    /// <summary>True when the post preserves the placed order for every nest.</summary>
    bool PreservesPlacedProgramOrder { get; }

    /// <summary>
    /// Why posting <paramref name="nest"/> with the current settings would not move in the
    /// placed order, or null when it would. A post whose order depends on the nest or its
    /// settings overrides this; it must return null only when the posted motion sequence
    /// provably equals the placed one.
    /// </summary>
    string PlacedProgramOrderChange(Nest nest) =>
        PreservesPlacedProgramOrder ? null : "does not declare that it preserves placed part/contour order and pierce positions";
}
