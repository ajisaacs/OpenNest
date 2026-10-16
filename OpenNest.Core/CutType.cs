namespace OpenNest
{
    /// <summary>
    /// Cutting process a library drawing is prepared for, stored by name so new
    /// processes can be added without a migration.
    /// </summary>
    public enum CutType
    {
        Laser,
        Waterjet,
        Plasma,
    }
}
