using System.Threading;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>Deterministic effort meter shared by all packers in one solve.</summary>
internal sealed class WorkCounter
{
    private long value;
    public long Value => Interlocked.Read(ref value);
    public void Add(long amount) => Interlocked.Add(ref value, amount);
}
