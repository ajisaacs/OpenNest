using System.Net.Http;
using System.Text.Json;

namespace OpenNest.Data;

/// <summary>
/// Duplicate-name checks for a nest's first upload and for renames. Names may already be
/// on posted CNC programs and labels, so a nest is never renamed silently: callers offer
/// a replacement, or warn, only when another active nest has the exact same name.
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
            if (!await IsNameTakenAsync(repository, name, cancellationToken: cancellationToken))
                return null;
            var number = await repository.AllocateNestNumberAsync(year, cancellationToken);
            return new NestNameAllocation { Year = year, Number = number, Name = NestNames.Format(year, number) };
        }
        catch (Exception ex) when (IsCheckFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// <see cref="IsNameTakenAsync"/>, or null when the server could not answer
    /// (offline, too slow, or an older server), so the caller can go ahead without warning.
    /// </summary>
    public static async Task<bool?> TryIsNameTakenAsync(
        INestRepository repository, string name, Guid excludeId = default,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await IsNameTakenAsync(repository, name, excludeId, cancellationToken);
        }
        catch (Exception ex) when (IsCheckFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// True when an active (not deleted) server nest other than <paramref name="excludeId"/>
    /// is named exactly <paramref name="name"/>.
    /// </summary>
    public static async Task<bool> IsNameTakenAsync(
        INestRepository repository, string name, Guid excludeId = default,
        CancellationToken cancellationToken = default)
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
            if (page.Items.Any(r => !r.Deleted && r.Id != excludeId
                && string.Equals(r.Name, name, StringComparison.Ordinal)))
                return true;
            offset += page.Items.Count;
            if (page.Items.Count == 0 || offset >= page.Total)
                return false;
        }
    }

    private static bool IsCheckFailure(Exception ex) =>
        ex is HttpRequestException or IOException or OperationCanceledException
            or NotSupportedException or ArgumentException or JsonException or InvalidOperationException;
}
