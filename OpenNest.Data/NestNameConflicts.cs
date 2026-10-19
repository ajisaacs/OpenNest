using System.Net.Http;
using System.Text.Json;

namespace OpenNest.Data;

/// <summary>
/// Duplicate-name check for a nest's first upload. Names may already be on posted CNC
/// programs and labels, so a nest is never renamed silently: callers offer the
/// replacement only when another active nest already has the exact same name.
/// </summary>
public static class NestNameConflicts
{
    /// <summary>
    /// A freshly allocated name for <paramref name="year"/> when an active server nest
    /// already uses <paramref name="name"/>; null when the name is free, or when the
    /// check or the allocation fails, so the caller keeps the name it has.
    /// </summary>
    public static async Task<NestNameAllocation?> FindReplacementAsync(
        INestRepository repository, string name, int year, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            if (!await IsNameTakenAsync(repository, name, cancellationToken))
                return null;
            var number = await repository.AllocateNestNumberAsync(year, cancellationToken);
            return new NestNameAllocation { Year = year, Number = number, Name = NestNames.Format(year, number) };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException
            or NotSupportedException or ArgumentException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>True when an active (not deleted) server nest is named exactly <paramref name="name"/>.</summary>
    public static async Task<bool> IsNameTakenAsync(
        INestRepository repository, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        // The search is a case-insensitive substring match; page through it for the exact name.
        for (var offset = 0; ;)
        {
            var page = await repository.QueryAsync(new NestQuery
            {
                Search = name,
                Sort = NestSortField.Name,
                Descending = false,
                Offset = offset,
                Limit = NestQuery.MaxLimit,
            }, cancellationToken);
            if (page.Items.Any(r => !r.Deleted && string.Equals(r.Name, name, StringComparison.Ordinal)))
                return true;
            offset += page.Items.Count;
            if (page.Items.Count == 0 || offset >= page.Total)
                return false;
        }
    }
}
