using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

/// <summary>
/// Bounded, SQL-filtered browsing (<c>GET /api/nests/query</c>) through the production
/// routes, real SQLite and the desktop's <see cref="RemoteNestRepository"/>.
/// </summary>
public sealed class NestQueryTests : IDisposable
{
    private readonly ServerFactory _factory = new();
    private readonly HttpClient _client;
    private readonly RemoteNestRepository _repository;
    private readonly NestDatabase _database;

    public NestQueryTests()
    {
        _client = _factory.CreateClient();
        _repository = new RemoteNestRepository(_client, _client.BaseAddress!.ToString());
        _database = _factory.Services.GetRequiredService<NestDatabase>();
    }

    [Fact]
    public async Task Query_WithSelectiveSearch_ReturnsOnlyMatchesAndTheirTotal()
    {
        var seeded = Seed(24);
        var expected = Ordered(seeded.Where(r => r.Customer == "Beta Works"));

        var page = await _repository.QueryAsync(new NestQuery { Search = "  BETA  ", Limit = NestQuery.MaxLimit });

        Assert.Equal(expected.Length, page.Total);
        Assert.Equal(expected.Select(r => r.Id), page.Items.Select(r => r.Id));
        Assert.All(page.Items, r => AssertSameMetadata(seeded.Single(s => s.Id == r.Id), r));
    }

    [Fact]
    public async Task Query_WithSelectiveSearch_DoesNotTransferUnmatchedRows()
    {
        Seed(24);

        var body = await _client.GetStringAsync("/api/nests/query?search=beta&limit=500");

        Assert.Contains("Beta Works", body);
        Assert.DoesNotContain("Alpha Fabrication", body);
        Assert.DoesNotContain("Gamma 50% Shop", body);
        Assert.DoesNotContain("Delta_Under", body);
        Assert.DoesNotContain("\"file\"", body);
    }

    [Fact]
    public async Task Query_WithNoMatch_ReturnsEmptyPageAndZeroTotal()
    {
        Seed(10);

        var page = await _repository.QueryAsync(new NestQuery { Search = "no such synthetic text" });

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Query_Pages_PartitionTheOrderedSetExactly()
    {
        var expected = Ordered(Seed(25)).Select(r => r.Id).ToArray();
        var collected = new List<Guid>();

        for (var offset = 0; offset < expected.Length; offset += 7)
        {
            var page = await _repository.QueryAsync(new NestQuery { Offset = offset, Limit = 7 });
            Assert.Equal(25, page.Total);
            Assert.Equal(offset, page.Offset);
            Assert.Equal(7, page.Limit);
            Assert.Equal(System.Math.Min(7, expected.Length - offset), page.Items.Count);
            collected.AddRange(page.Items.Select(r => r.Id));
        }

        Assert.Equal(expected, collected);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public async Task Query_OffsetPastTheEnd_ReturnsNoItemsWithTheTrueTotal(int offset)
    {
        Seed(25);

        var page = await _repository.QueryAsync(new NestQuery { Offset = offset });

        Assert.Empty(page.Items);
        Assert.Equal(25, page.Total);
    }

    [Fact]
    public async Task Query_DefaultLimit_BoundsAnUnfilteredRequest()
    {
        Seed(NestQuery.DefaultLimit + 5);

        var page = await _repository.QueryAsync(new NestQuery());
        var raw = JsonDocument.Parse(await _client.GetStringAsync("/api/nests/query"));

        Assert.Equal(NestQuery.DefaultLimit, page.Items.Count);
        Assert.Equal(NestQuery.DefaultLimit + 5, page.Total);
        Assert.Equal(NestQuery.DefaultLimit, raw.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Query_BlankSearch_EqualsTheUnfilteredOrder(string search)
    {
        var expected = Ordered(Seed(12)).Select(r => r.Id);

        var page = await _repository.QueryAsync(new NestQuery { Search = search });

        Assert.Equal(expected, page.Items.Select(r => r.Id));
        Assert.Equal(12, page.Total);
    }

    [Theory]
    [InlineData("0%", "Gamma 50% Shop")]
    [InlineData("_", "Delta_Under")]
    [InlineData(@"\", @"Path C:\jobs")]
    public async Task Query_TreatsLikeWildcardsAndEscapeAsLiteralText(string search, string expectedText)
    {
        var seeded = Seed(24);
        var expected = Ordered(seeded.Where(r => r.Customer == expectedText || r.Comments == expectedText));
        Assert.NotEmpty(expected);
        Assert.True(expected.Length < seeded.Count);

        var page = await _repository.QueryAsync(new NestQuery { Search = search, Limit = NestQuery.MaxLimit });

        Assert.Equal(expected.Select(r => r.Id), page.Items.Select(r => r.Id));
        Assert.Equal(expected.Length, page.Total);
    }

    [Theory]
    [InlineData("to be cut", new[] { NestStatus.ToBeCut })]
    [InlineData("HAS BEEN", new[] { NestStatus.HasBeenCut })]
    [InlineData("tobecut", new[] { NestStatus.ToBeCut })]
    [InlineData("cut", new[] { NestStatus.ToBeCut, NestStatus.HasBeenCut })]
    [InlineData("quote", new[] { NestStatus.Quote })]
    public async Task Query_MatchesStoredAndDisplayedStatusNames(string search, NestStatus[] statuses)
    {
        var seeded = Seed(24);
        var expected = Ordered(seeded.Where(r => statuses.Contains(r.Status)));

        var page = await _repository.QueryAsync(new NestQuery { Search = search, Limit = NestQuery.MaxLimit });

        Assert.Equal(expected.Select(r => r.Id), page.Items.Select(r => r.Id));
    }

    [Theory]
    [InlineData("material", "Synthetic steel")]
    [InlineData("madeBy", "Synthetic operator B")]
    [InlineData("comments", "rush")]
    [InlineData("name", "browse nest 07")]
    public async Task Query_MatchesEachTextColumn(string column, string search)
    {
        var seeded = Seed(24);
        Func<NestRecord, string> value = column switch
        {
            "material" => r => r.Material,
            "madeBy" => r => r.MadeBy,
            "comments" => r => r.Comments,
            _ => r => r.Name,
        };
        var expected = Ordered(seeded.Where(r => value(r).Contains(search, StringComparison.OrdinalIgnoreCase)));
        Assert.NotEmpty(expected);
        Assert.True(expected.Length < seeded.Count);

        var page = await _repository.QueryAsync(new NestQuery { Search = search, Limit = NestQuery.MaxLimit });

        Assert.Equal(expected.Select(r => r.Id), page.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task Query_DoesNotMatchDatesOrNumbers()
    {
        Seed(12);

        var thickness = await _repository.QueryAsync(new NestQuery { Search = "0.25" });
        var date = await _repository.QueryAsync(new NestQuery { Search = "2026-01" });

        Assert.Equal(0, thickness.Total);
        Assert.Equal(0, date.Total);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=501")]
    [InlineData("limit=abc")]
    [InlineData("limit=")]
    [InlineData("limit=-1")]
    [InlineData("limit=1.5")]
    [InlineData("offset=-1")]
    [InlineData("offset=99999999999")]
    [InlineData("limit=1&limit=2")]
    [InlineData("serach=beta")]
    [InlineData("status=quote")]
    public async Task Query_InvalidParameters_Return400WithoutItems(string queryString)
    {
        Seed(3);

        using var response = await _client.GetAsync("/api/nests/query?" + queryString);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("items", body);
        Assert.DoesNotContain("Synthetic browse nest", body);
    }

    [Fact]
    public async Task Query_OverLongSearch_Returns400()
    {
        Seed(3);
        var search = new string('x', NestQuery.MaxSearchLength + 1);

        using var response = await _client.GetAsync("/api/nests/query?search=" + search);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<NestSortField, bool> SortCases()
    {
        var data = new TheoryData<NestSortField, bool>();
        foreach (var field in Enum.GetValues<NestSortField>())
        {
            data.Add(field, false);
            data.Add(field, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SortCases))]
    public async Task Query_SortsEachAllowlistedColumnAcrossPages(NestSortField field, bool descending)
    {
        var expected = Sorted(Seed(25), field, descending).Select(r => r.Id).ToArray();
        var collected = new List<Guid>();

        for (var offset = 0; offset < expected.Length; offset += 6)
        {
            var page = await _repository.QueryAsync(
                new NestQuery { Sort = field, Descending = descending, Offset = offset, Limit = 6 });
            collected.AddRange(page.Items.Select(r => r.Id));
        }

        Assert.Equal(expected, collected);
    }

    [Fact]
    public async Task Query_SortAndOrderNamesAreCaseInsensitive()
    {
        var expected = Sorted(Seed(12), NestSortField.PlateCount, descending: false).Select(r => r.Id);

        var body = await _client.GetStringAsync("/api/nests/query?sort=PLATECOUNT&order=ASC");
        var ids = JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid());

        Assert.Equal(expected, ids);
    }

    [Theory]
    [InlineData("sort=unknown")]
    [InlineData("sort=1")]
    [InlineData("sort=-1")]
    [InlineData("sort=")]
    [InlineData("sort=saved_at")]
    [InlineData("sort=savedAt&sort=name")]
    [InlineData("order=up")]
    [InlineData("order=")]
    [InlineData("order=asc&order=desc")]
    public async Task Query_InvalidSortOrOrder_Returns400WithoutItems(string queryString)
    {
        Seed(3);

        using var response = await _client.GetAsync("/api/nests/query?" + queryString);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("items", body);
    }

    [Fact]
    public async Task List_RemainsTheUnboundedFullEnumeration()
    {
        var seeded = Seed(NestQuery.DefaultLimit + 5);

        var listed = await _repository.ListAsync();

        Assert.Equal(seeded.Select(r => r.Id).Order(), listed.Select(r => r.Id).Order());
    }

    public void Dispose()
    {
        _repository.Dispose();
        _client.Dispose();
        _factory.Dispose();
    }

    /// <summary>Synthetic records with rotating customers/statuses; returns the stored rows.</summary>
    private List<NestRecord> Seed(int count)
    {
        string[] customers = { "Alpha Fabrication", "Beta Works", "Gamma 50% Shop", "Delta_Under" };
        var statuses = Enum.GetValues<NestStatus>();
        var stored = new List<NestRecord>();
        for (var index = 0; index < count; index++)
        {
            var record = new NestRecord
            {
                // Alternate case so text sorts must fold case rather than order by code point.
                Name = (index % 2 == 0 ? "Synthetic" : "synthetic") + $" browse nest {index:D2}",
                Customer = customers[index % customers.Length],
                DateCreated = new DateTime(2026, 1, 1 + index % 28, 8, 0, 0, DateTimeKind.Unspecified),
                DateModified = new DateTime(2026, 1, 2 + index % 27, 9, 0, 0, DateTimeKind.Unspecified),
                Material = index % 3 == 0 ? "Synthetic steel" : "Synthetic alloy",
                Thickness = 0.25 + index % 3 * 0.125,
                Status = statuses[index % statuses.Length],
                PlateCount = 1 + index % 4,
                PartCount = 3 + index,
                Comments = index % 5 == 0 ? "rush" : index % 7 == 1 ? @"Path C:\jobs" : "",
                MadeBy = index % 2 == 0 ? "Synthetic operator A" : "Synthetic operator B",
            };
            stored.Add(_database.Insert(Guid.NewGuid(), record, new byte[3 + index % 4]));
        }

        return stored;
    }

    /// <summary>Independent oracle for the browse order: newest saved first, then id.</summary>
    private static NestRecord[] Ordered(IEnumerable<NestRecord> records) =>
        records
            .OrderByDescending(r => r.SavedAt)
            .ThenByDescending(r => r.Id.ToString(), StringComparer.Ordinal)
            .ToArray();

    /// <summary>Independent oracle for allowlisted sorts: ASCII case-folded text, then id.</summary>
    private static IEnumerable<NestRecord> Sorted(IEnumerable<NestRecord> records, NestSortField field, bool descending)
    {
        static Comparison<NestRecord> Text(Func<NestRecord, string> value) =>
            (a, b) => string.CompareOrdinal(value(a).ToLowerInvariant(), value(b).ToLowerInvariant());

        Comparison<NestRecord> compare = field switch
        {
            NestSortField.SavedAt => (a, b) => a.SavedAt.CompareTo(b.SavedAt),
            NestSortField.Name => Text(r => r.Name),
            NestSortField.Customer => Text(r => r.Customer),
            NestSortField.Status => Text(r => r.Status.ToString()),
            NestSortField.Material => Text(r => r.Material),
            NestSortField.DateCreated => (a, b) => a.DateCreated.CompareTo(b.DateCreated),
            NestSortField.DateModified => (a, b) => a.DateModified.CompareTo(b.DateModified),
            NestSortField.Thickness => (a, b) => a.Thickness.CompareTo(b.Thickness),
            NestSortField.PlateCount => (a, b) => a.PlateCount.CompareTo(b.PlateCount),
            NestSortField.PartCount => (a, b) => a.PartCount.CompareTo(b.PartCount),
            NestSortField.MadeBy => Text(r => r.MadeBy),
            NestSortField.Comments => Text(r => r.Comments),
            NestSortField.FileSize => (a, b) => a.FileSize.CompareTo(b.FileSize),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var sorted = records.ToList();
        sorted.Sort((a, b) =>
        {
            var result = compare(a, b);
            if (result == 0)
                result = string.CompareOrdinal(a.Id.ToString(), b.Id.ToString());
            return descending ? -result : result;
        });
        return sorted;
    }

    private static void AssertSameMetadata(NestRecord expected, NestRecord actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
}
