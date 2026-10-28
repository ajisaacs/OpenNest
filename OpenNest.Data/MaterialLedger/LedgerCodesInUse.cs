using System.Net.Http;

namespace OpenNest.Data.MaterialLedger;

/// <summary>
/// The ledger item codes other open server nests are coded with, or why they could not be
/// read. ML's free counts drop only once a hold exists, so these codes are what another
/// Auto Nest run must leave out until then. Reading them never blocks: a failure is a result.
/// </summary>
public sealed record LedgerCodesInUse
{
    /// <summary>The nest server gets this long to answer, so a stock load or save never waits on it.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private LedgerCodesInUse(IReadOnlyList<NestStockCodeUse> uses, string? unavailable)
    {
        Uses = uses;
        Unavailable = unavailable;
        Codes = uses.Select(use => use.ItemCode).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>One entry per code and nest; empty when <see cref="Unavailable"/>.</summary>
    public IReadOnlyList<NestStockCodeUse> Uses { get; }

    /// <summary>The distinct codes of <see cref="Uses"/>.</summary>
    public IReadOnlySet<string> Codes { get; }

    /// <summary>Why the codes could not be read; null when they were.</summary>
    public string? Unavailable { get; }

    public bool IsKnown => Unavailable == null;

    public static LedgerCodesInUse Known(IEnumerable<NestStockCodeUse> uses) =>
        new((uses ?? throw new ArgumentNullException(nameof(uses))).Where(use => use != null).ToList(), null);

    public static LedgerCodesInUse Unknown(string reason) =>
        new(Array.Empty<NestStockCodeUse>(), string.IsNullOrWhiteSpace(reason) ? "not available" : reason);

    /// <summary>The names of the other nests using <paramref name="code"/>, in name order.</summary>
    public IReadOnlyList<string> NestsUsing(string code) =>
        Uses.Where(use => string.Equals(use.ItemCode, code, StringComparison.Ordinal))
            .Select(use => use.NestName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Reads the codes from <paramref name="repository"/>, leaving out the nest
    /// <paramref name="excludeNestId"/> (the one being edited). A null repository is File
    /// mode. Only the caller's own cancellation throws.
    /// </summary>
    public static async Task<LedgerCodesInUse> ReadAsync(INestRepository? repository, Guid excludeNestId,
        CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        if (repository == null)
            return Unknown("nests are saved as files");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? RequestTimeout);
        try
        {
            return Known(await repository.GetStockCodesInUseAsync(excludeNestId, timeoutSource.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unknown("the nest server did not answer");
        }
        catch (NotSupportedException)
        {
            return Unknown("the nest server needs an update");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException
            or ArgumentException or InvalidOperationException)
        {
            return Unknown("the nest server could not be reached");
        }
    }
}
