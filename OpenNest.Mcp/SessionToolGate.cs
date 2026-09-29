using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenNest.Mcp;

/// <summary>Serializes tool operations sharing a mutable nesting session.</summary>
public sealed class SessionToolGate : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async ValueTask<T> RunAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await action(token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}
