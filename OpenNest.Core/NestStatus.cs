namespace OpenNest
{
    /// <summary>
    /// Shop-floor workflow state of a saved nest, persisted as a string in
    /// <c>nest.json</c> so unknown future values can fall back safely.
    /// </summary>
    /// <remarks>
    /// New values go at the end: the nest server still accepts integer statuses from
    /// existing callers, so existing values must keep their numbers. Lists shown to the
    /// operator use <see cref="NestStatuses.WorkflowOrder"/>, not declaration order.
    /// </remarks>
    public enum NestStatus
    {
        Quote,
        ToBeCut,
        HasBeenCut,

        /// <summary>
        /// Created but not yet ready to quote or cut, such as a nest released from a
        /// drawing set before it has been nested and checked. Comes before Quote.
        /// </summary>
        Draft,
    }

    public static class NestStatuses
    {
        /// <summary>Every status in the order a nest moves through them.</summary>
        public static readonly NestStatus[] WorkflowOrder =
        {
            NestStatus.Draft,
            NestStatus.Quote,
            NestStatus.ToBeCut,
            NestStatus.HasBeenCut,
        };
    }
}
