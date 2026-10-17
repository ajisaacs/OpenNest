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
    }
}
