using System.Net;
using System.Net.Http.Json;
using OpenNest.Data;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;

namespace OpenNest.Server.Tests;

public sealed class DrawingLibraryQueryTests
{
    private static async Task<Dictionary<string, DrawingLibraryRecord>> SeedNamed(HttpClient client)
    {
        var rows = new (string Name, string Customer, string Material, string Grade, double Thickness, CutType CutType, string Notes)[]
        {
            ("Alpha bracket", "Alpha Fabrication", "Carbon Steel", "A36 HR", 0.25, CutType.Laser, ""),
            ("Beta plate", "beta  works", "carbon  steel", " a36 hr", 0.1875, CutType.Waterjet, "Gamma 50% notes"),
            ("Delta_Under", "Gamma 50% Shop", "Stainless", "304", 0.25, CutType.Plasma, ""),
            ("Echo", "Alpha Fabrication", "Carbon Steel", "A36 HR", 0.25004, CutType.Laser, ""),
        };
        var saved = new Dictionary<string, DrawingLibraryRecord>();
        foreach (var row in rows)
        {
            var record = Record(row.Name);
            record.Customer = row.Customer;
            record.MaterialName = row.Material;
            record.MaterialGrade = row.Grade;
            record.Thickness = row.Thickness;
            record.CutType = row.CutType;
            record.Notes = row.Notes;
            saved[row.Name] = await Upload(client, record, Package(row.Name));
        }

        using var archived = await client.SendAsync(Guarded(HttpMethod.Post, $"/api/drawings/{saved["Echo"].Id}/archive", "\"1\""));
        archived.EnsureSuccessStatusCode();
        return saved;
    }

    [Theory]
    [InlineData("", "Alpha bracket|Beta plate|Delta_Under")]
    [InlineData("includeArchived=true", "Alpha bracket|Beta plate|Delta_Under|Echo")]
    [InlineData("includeArchived=false", "Alpha bracket|Beta plate|Delta_Under")]
    [InlineData("cutType=laser", "Alpha bracket")]
    [InlineData("cutType=LASER&includeArchived=true", "Alpha bracket|Echo")]
    [InlineData("cutType=Waterjet", "Beta plate")]
    [InlineData("materialName=CARBON%20%20steel%20", "Alpha bracket|Beta plate")]
    [InlineData("materialGrade=a36%20HR", "Alpha bracket|Beta plate")]
    [InlineData("customer=ALPHA%20fabrication", "Alpha bracket")]
    [InlineData("customer=Beta%20Works", "Beta plate")]
    [InlineData("thickness=0.25", "Alpha bracket|Delta_Under")]
    [InlineData("thickness=0.25&includeArchived=true", "Alpha bracket|Delta_Under|Echo")]
    [InlineData("thickness=0.1875", "Beta plate")]
    [InlineData("thickness=0.1874", "")]
    [InlineData("thickness=0.25&materialName=Carbon%20Steel&cutType=laser", "Alpha bracket")]
    [InlineData("materialName=Stainless&cutType=laser", "")]
    [InlineData("customer=&materialName=", "Alpha bracket|Beta plate|Delta_Under")]
    [InlineData("search=0%25", "Beta plate|Delta_Under")]
    [InlineData("search=_", "Delta_Under")]
    [InlineData("search=%5C", "")]
    [InlineData("search=%20GAMMA%20", "Beta plate|Delta_Under")]
    [InlineData("search=stainless", "")]
    [InlineData("search=bracket&cutType=plasma", "")]
    public async Task Filters_MatchExactKeysAndLiteralSearch(string query, string expected)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        await SeedNamed(client);

        var page = await DrawingLibraryApiTests.Query(client, query + "&sort=name&order=asc");

        Assert.Equal(expected, string.Join("|", page.Items.Select(r => r.Name)));
        Assert.Equal(page.Items.Count, page.Total);
    }

    [Theory]
    [InlineData("name", false)]
    [InlineData("name", true)]
    [InlineData("customer", false)]
    [InlineData("materialName", true)]
    [InlineData("materialGrade", false)]
    [InlineData("thickness", true)]
    [InlineData("thickness", false)]
    [InlineData("cutType", false)]
    [InlineData("revision", true)]
    public async Task Sort_MatchesOracleAndPagesPartitionMatches(string sort, bool descending)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        string[] names = { "beta", "Alpha", "alpha", "BETA", "gamma" };
        string[] grades = { "a36", "A36", "304" };
        double[] thicknesses = { 0.25, 0.1875, 0.25 };
        CutType[] cutTypes = { CutType.Plasma, CutType.Laser, CutType.Waterjet };
        var saved = new List<DrawingLibraryRecord>();
        for (var i = 0; i < 13; i++)
        {
            var record = Record(names[i % names.Length]);
            record.Customer = names[(i + 2) % names.Length];
            record.MaterialName = names[(i + 1) % names.Length];
            record.MaterialGrade = grades[i % grades.Length];
            record.Thickness = thicknesses[i % thicknesses.Length];
            record.CutType = cutTypes[i % cutTypes.Length];
            var uploaded = await Upload(client, record, Package(i.ToString()));
            if (i % 4 == 0)
            {
                record.Notes = "edited";
                using var edit = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/metadata", "\"1\"",
                    JsonContent.Create(record, options: Options)));
                uploaded = (await edit.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!;
            }
            saved.Add(uploaded);
        }

        var expected = Oracle(saved, sort, descending).Select(r => r.Id).ToList();
        var order = descending ? "desc" : "asc";
        var full = await DrawingLibraryApiTests.Query(client, $"sort={sort}&order={order}");
        var paged = new List<Guid>();
        for (var offset = 0; offset < saved.Count; offset += 5)
            paged.AddRange((await DrawingLibraryApiTests.Query(client, $"sort={sort}&order={order}&offset={offset}&limit=5")).Items.Select(r => r.Id));

        Assert.Equal(expected, full.Items.Select(r => r.Id));
        Assert.Equal(expected, paged);
        Assert.Equal(saved.Count, full.Total);
    }

    [Fact]
    public async Task DefaultOrder_IsNewestSavedFirst()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var first = await Upload(client, Record("First"), Package("1"));
        var second = await Upload(client, Record("Second"), Package("2"));

        var page = await DrawingLibraryApiTests.Query(client, "");

        var expected = new[] { first, second }
            .OrderByDescending(r => r.SavedAt.ToString("o"), StringComparer.Ordinal)
            .ThenByDescending(r => r.Id.ToString(), StringComparer.Ordinal)
            .Select(r => r.Id);
        Assert.Equal(expected, page.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task Limit_BoundsThePageAndTotalCountsEveryMatch()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        for (var i = 0; i < 3; i++)
            await Upload(client, Record($"Drawing {i}"), Package(i.ToString()));

        var page = await DrawingLibraryApiTests.Query(client, "limit=2&offset=1");
        var maximum = await DrawingLibraryApiTests.Query(client, "limit=500");
        var beyond = await DrawingLibraryApiTests.Query(client, "offset=2147483647");

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.Total);
        Assert.Equal(1, page.Offset);
        Assert.Equal(2, page.Limit);
        Assert.Equal(3, maximum.Items.Count);
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.Total);
    }

    [Theory]
    [InlineData("unknown=1")]
    [InlineData("limit=0")]
    [InlineData("limit=501")]
    [InlineData("limit=-1")]
    [InlineData("offset=-1")]
    [InlineData("offset=x")]
    [InlineData("sort=1")]
    [InlineData("sort=package")]
    [InlineData("sort=name%20desc")]
    [InlineData("order=up")]
    [InlineData("cutType=1")]
    [InlineData("cutType=Laser,Plasma")]
    [InlineData("cutType=oxyfuel")]
    [InlineData("cutType=")]
    [InlineData("cutType=laser&cutType=plasma")]
    [InlineData("thickness=-1")]
    [InlineData("thickness=NaN")]
    [InlineData("thickness=Infinity")]
    [InlineData("thickness=1e3")]
    [InlineData("thickness=")]
    [InlineData("includeArchived=yes")]
    [InlineData("search=a%00b")]
    [InlineData("customer=a%00b")]
    public async Task InvalidParameters_AreRejected(string query)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/drawings/query?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OverlongSearchOrFilter_IsRejected()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var search = await client.GetAsync("/api/drawings/query?search=" + new string('s', 201));
        using var customer = await client.GetAsync("/api/drawings/query?customer=" + new string('c', 201));
        using var boundary = await client.GetAsync("/api/drawings/query?search=" + new string('s', 200));

        Assert.Equal(HttpStatusCode.BadRequest, search.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, customer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
    }

    /// <summary>Independent oracle: ASCII case-folded text or numeric value, then id, in one direction.</summary>
    private static IEnumerable<DrawingLibraryRecord> Oracle(List<DrawingLibraryRecord> records, string sort, bool descending)
    {
        Func<DrawingLibraryRecord, IComparable> key = sort switch
        {
            "name" => r => r.Name.ToUpperInvariant(),
            "customer" => r => r.Customer.ToUpperInvariant(),
            "materialName" => r => r.MaterialName.ToUpperInvariant(),
            "materialGrade" => r => r.MaterialGrade.ToUpperInvariant(),
            "thickness" => r => r.Thickness,
            "cutType" => r => r.CutType.ToString(),
            "revision" => r => r.Revision,
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };
        var comparer = Comparer<IComparable>.Create((a, b) =>
            a is string x && b is string y ? string.CompareOrdinal(x, y) : a.CompareTo(b));
        return descending
            ? records.OrderByDescending(key, comparer).ThenByDescending(r => r.Id.ToString(), StringComparer.Ordinal)
            : records.OrderBy(key, comparer).ThenBy(r => r.Id.ToString(), StringComparer.Ordinal);
    }
}
