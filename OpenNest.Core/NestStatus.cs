namespace OpenNest
{
    /// <summary>
    /// Shop-floor workflow state of a saved nest, persisted as a string in
    /// <c>nest.json</c> so unknown future values can fall back safely.
    /// </summary>
    public enum NestStatus
    {
        Quote,
        ToBeCut,
        HasBeenCut,
    }
}
