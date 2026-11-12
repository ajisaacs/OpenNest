using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenNest.CNC;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Server.Worker;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;
using CncProgram = OpenNest.CNC.Program;

namespace OpenNest.Server.Tests;

/// <summary>
/// The library's Filter dialog ranges: thickness in inches across units, measured size and
/// holes from the worker's package summaries, saved dates, and the measurement routes.
/// </summary>
public sealed class DrawingLibraryRangeFilterTests
{
    private static DrawingGeometrySummary Measured(double length, double width, int holes) => new()
    {
        Version = DrawingGeometrySummary.CurrentVersion,
        Length = length,
        Width = width,
        Area = length * width,
        Holes = holes,
    };

    // Name, thickness, units, and the summary stored for its package (null: not yet measured).
    private static async Task Seed(HttpClient client)
    {
        var rows = new (string Name, double Thickness, Units Units, DrawingGeometrySummary? Geometry)[]
        {
            ("Gauge washer", 0.0598, Units.Inches, Measured(2, 2, 2)),
            ("Plate tab", 0.1875, Units.Inches, Measured(3.5, 1, 3)),
            ("Thick tab", 0.25, Units.Inches, Measured(3, 1, 2)),
            ("Big gauge panel", 0.06, Units.Inches, Measured(12, 4, 8)),
            ("Metric clip", 3, Units.Millimeters, Measured(3.9, 0.5, 2)),
            ("No thickness", 0, Units.Inches, Measured(1, 1, 4)),
            ("Unmeasured", 0.105, Units.Inches, null),
            ("Unreadable", 0.105, Units.Inches, DrawingGeometrySummary.Failed("Synthetic failure")),
        };
        foreach (var row in rows)
        {
            var record = Record(row.Name);
            record.Thickness = row.Thickness;
            record.Units = row.Units;
            var package = Package(row.Name);
            await Upload(client, record, package);
            if (row.Geometry is not null)
            {
                using var put = await client.PutAsJsonAsync($"/api/drawing-packages/{Sha256(package)}/geometry", row.Geometry, Options);
                Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
            }
        }
    }

    [Theory]
    [InlineData("minThickness=0.0598&maxThickness=0.1875", "Big gauge panel|Gauge washer|Metric clip|No thickness|Plate tab|Unmeasured|Unreadable")]
    [InlineData("minThickness=0.0598&maxThickness=0.1875&includeBlankThickness=false", "Big gauge panel|Gauge washer|Metric clip|Plate tab|Unmeasured|Unreadable")]
    [InlineData("includeBlankThickness=false&maxThickness=0.1", "Big gauge panel|Gauge washer")]
    [InlineData("minThickness=0.2&includeBlankThickness=false", "Thick tab")]
    [InlineData("maxLength=4", "Gauge washer|Metric clip|No thickness|Plate tab|Thick tab")]
    [InlineData("maxLength=4&minHoles=3", "No thickness|Plate tab")]
    [InlineData("minWidth=1&maxWidth=2", "Gauge washer|No thickness|Plate tab|Thick tab")]
    [InlineData("minHoles=0", "Big gauge panel|Gauge washer|Metric clip|No thickness|Plate tab|Thick tab")]
    [InlineData("maxHoles=2&minLength=3", "Metric clip|Thick tab")]
    [InlineData("minThickness=0.0598&maxThickness=0.1875&includeBlankThickness=false&maxLength=4&minHoles=2", "Gauge washer|Metric clip|Plate tab")]
    [InlineData("savedFrom=2000-01-01", "Big gauge panel|Gauge washer|Metric clip|No thickness|Plate tab|Thick tab|Unmeasured|Unreadable")]
    [InlineData("savedTo=2000-01-01", "")]
    public async Task Ranges_MatchInclusivelyInInchesAndOnlyMeasuredSizes(string query, string expected)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        var page = await DrawingLibraryApiTests.Query(client, query + "&sort=name&order=asc");

        Assert.Equal(expected, string.Join("|", page.Items.Select(r => r.Name)));
        Assert.Equal(page.Items.Count, page.Total);
    }

    [Fact]
    public async Task Query_ReturnsEachDrawingsSummary_AndSortsByLengthAndHoles()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        var page = await DrawingLibraryApiTests.Query(client, "sort=length&order=desc");
        Assert.Equal("Big gauge panel", page.Items[0].Name);
        Assert.Equal(new DrawingGeometrySummary { Version = 1, Length = 12, Width = 4, Area = 48, Holes = 8 }, page.Items[0].Geometry);
        Assert.Null(page.Items.Single(r => r.Name == "Unmeasured").Geometry);
        Assert.Equal("Synthetic failure", page.Items.Single(r => r.Name == "Unreadable").Geometry!.Error);

        var byHoles = await DrawingLibraryApiTests.Query(client, "sort=holes&order=desc&limit=1");
        Assert.Equal("Big gauge panel", Assert.Single(byHoles.Items).Name);
    }

    [Fact]
    public async Task SavedDates_CompareTheServersLocalDate()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        await Upload(client, Record("Today"), Package("today"));
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var tomorrow = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");

        Assert.Equal(1, (await DrawingLibraryApiTests.Query(client, $"savedFrom={today}&savedTo={today}")).Total);
        Assert.Equal(0, (await DrawingLibraryApiTests.Query(client, $"savedFrom={tomorrow}")).Total);
    }

    [Theory]
    [InlineData("minThickness=-1")]
    [InlineData("maxLength=abc")]
    [InlineData("minLength=5&maxLength=4")]
    [InlineData("minHoles=-2")]
    [InlineData("minHoles=1.5")]
    [InlineData("savedFrom=11/12/2026")]
    [InlineData("savedFrom=2026-02-01&savedTo=2026-01-01")]
    [InlineData("includeBlankThickness=maybe")]
    [InlineData("maxLength=NaN")]
    public async Task InvalidRanges_AreRefused(string query)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/drawings/query?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unmeasured_ListsEachCurrentPackageOnceUntilMeasuredAtTheVersion()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var shared = Package("shared");
        await Upload(client, Record("First"), shared);
        await Upload(client, Record("Same geometry"), shared);
        var other = Package("other");
        await Upload(client, Record("Other"), other);
        var expected = new[] { Sha256(shared), Sha256(other) }.Order().ToList();

        Assert.Equal(expected, await client.GetFromJsonAsync<List<string>>("/api/drawing-packages/unmeasured?version=1&limit=10"));

        using (var put = await client.PutAsJsonAsync($"/api/drawing-packages/{Sha256(shared)}/geometry", Measured(2, 1, 0), Options))
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(new[] { Sha256(other) }, await client.GetFromJsonAsync<List<string>>("/api/drawing-packages/unmeasured?version=1&limit=10"));
        // A newer measurement version lists it again.
        Assert.Equal(expected, await client.GetFromJsonAsync<List<string>>("/api/drawing-packages/unmeasured?version=2&limit=10"));
        Assert.Equal(shared, await client.GetByteArrayAsync($"/api/drawing-packages/{Sha256(shared)}"));
    }

    [Fact]
    public async Task OlderVersionSummary_NeverReplacesANewerOne()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var package = Package("versioned");
        await Upload(client, Record("Versioned"), package);
        var url = $"/api/drawing-packages/{Sha256(package)}/geometry";

        (await client.PutAsJsonAsync(url, Measured(5, 5, 5) with { Version = 2 }, Options)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(url, Measured(1, 1, 1), Options)).EnsureSuccessStatusCode();

        var page = await DrawingLibraryApiTests.Query(client, "");
        Assert.Equal(5, Assert.Single(page.Items).Geometry!.Holes);
    }

    [Theory]
    [InlineData("{\"version\":0,\"length\":1,\"width\":1,\"area\":1,\"holes\":0}")]
    [InlineData("{\"version\":1,\"length\":1,\"width\":2,\"area\":1,\"holes\":0}")]
    [InlineData("{\"version\":1,\"length\":1,\"width\":1,\"area\":1,\"holes\":-1}")]
    [InlineData("not json")]
    public async Task InvalidSummary_IsRefused(string json)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var package = Package("invalid");
        await Upload(client, Record("Invalid"), package);

        using var response = await client.PutAsync($"/api/drawing-packages/{Sha256(package)}/geometry",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownOrMalformedHash_IsNotFoundOrRefused()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var unknown = await client.PutAsJsonAsync($"/api/drawing-packages/{new string('a', 64)}/geometry", Measured(1, 1, 0), Options);
        using var malformed = await client.GetAsync("/api/drawing-packages/ABC");
        using var badLimit = await client.GetAsync("/api/drawing-packages/unmeasured?version=1&limit=501");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
    }

    private static Drawing Washer(string name, double size, int holes)
    {
        var program = new CncProgram();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(0, size)));
        program.Codes.Add(new LinearMove(new Vector(size, size)));
        program.Codes.Add(new LinearMove(new Vector(size, 0)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        for (var i = 0; i < holes; i++)
        {
            var x = 0.25 + i * 0.75;
            program.Codes.Add(new RapidMove(new Vector(x, 0.25)));
            program.Codes.Add(new LinearMove(new Vector(x + 0.5, 0.25)));
            program.Codes.Add(new LinearMove(new Vector(x + 0.5, 0.75)));
            program.Codes.Add(new LinearMove(new Vector(x, 0.75)));
            program.Codes.Add(new LinearMove(new Vector(x, 0.25)));
        }
        return new Drawing(name, program);
    }

    [Fact]
    public async Task WorkerBackfill_MeasuresRealPackages_SoTheSizeFilterFindsThem()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var url = client.BaseAddress!.ToString();
        using var library = new RemoteDrawingLibrary(client, url);
        using var geometry = new RemoteDrawingGeometry(client, url);
        foreach (var (name, size, holes) in new[] { ("Small two-hole", 3.0, 2), ("Small one-hole", 3.0, 1), ("Large two-hole", 6.0, 2) })
        {
            var record = Record(name);
            record.Thickness = 0.1046;
            await library.UploadAsync(record, DrawingPackage.Write(Washer(name, size, holes), Units.Inches));
        }
        var badPackage = Package("not a drawing");
        await Upload(client, Record("Not a drawing"), badPackage);
        var backfill = new DrawingGeometryBackfill(geometry, NullLogger<DrawingGeometryBackfill>.Instance);

        Assert.Equal(4, await backfill.RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, await backfill.RunOnceAsync(CancellationToken.None));

        var page = await library.QueryAsync(new DrawingLibraryQuery
        {
            Filter = new DrawingLibraryFilter { MinThickness = 0.0598, MaxThickness = 0.1875, MaxLength = 4, MinHoles = 2 },
        });
        var found = Assert.Single(page.Items);
        Assert.Equal("Small two-hole", found.Name);
        Assert.Equal((3, 3, 2), (found.Geometry!.Length, found.Geometry.Width, found.Geometry.Holes));
        Assert.Equal(9 - 2 * 0.25, found.Geometry.Area, 6);
        var failed = (await DrawingLibraryApiTests.Query(client, "search=Not%20a%20drawing")).Items.Single();
        Assert.False(failed.Geometry!.IsMeasured);
    }
}
