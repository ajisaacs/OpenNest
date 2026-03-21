namespace OpenNest.Engine.Strategies
{
    /// <summary>
    /// Groups engine scoring and direction policy into a single object.
    /// Set by the engine, consumed by strategies via FillContext.Policy.
    /// </summary>
    public record FillPolicy(IFillComparer Comparer, NestDirection? PreferredDirection = null);
}
