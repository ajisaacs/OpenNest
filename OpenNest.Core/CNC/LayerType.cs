namespace OpenNest.CNC
{
    public enum LayerType
    {
        Display,
        Scribe,
        Cut,
        Leadin,
        Leadout,

        /// <summary>
        /// An intentionally open cut through part material, left uncut at tabs (a breakoff's
        /// break line). It cuts like <see cref="Cut"/> but is not a material boundary; the scrap
        /// it may be pierced from lies to the left of travel.
        /// </summary>
        Break,

        /// <summary>
        /// An uncut span of a material boundary (a tabbed cutout's tab). It bounds material like
        /// <see cref="Cut"/> so the cutout stays a closed hole, but it is never cut: emission
        /// leaves it out, cutting the rest of the contour as open pieces.
        /// </summary>
        Tab,
    }
}
