using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenNest.Data;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;
using static OpenNest.Server.Tests.DrawingSetFixtures;

namespace OpenNest.Server.Tests;

public sealed class DrawingSetApiTests
{
    [Fact]
    public async Task Create_StoresRevisionOneWithMembersJoinedToTheirDrawings()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var a = await AddDrawing(client, "Synthetic A");
        var b = await AddDrawing(client, "Synthetic B", r => r.CutType = CutType.Waterjet);
        var set = Set(members: [(a, 4), (b, 1)]);
        set.Revision = 9;
        set.MemberCount = 99;

        var saved = await Create(client, set);

        Assert.Equal((set.Id, 1, 2, false), (saved.Id, saved.Revision, saved.MemberCount, saved.Archived));
        Assert.Equal(("Synthetic kit", "Synthetic customer", "Synthetic set notes", "Synthetic operator"),
            (saved.Name, saved.Customer, saved.Notes, saved.SavedBy));
        Assert.NotEqual(default, saved.SavedAt);
        Assert.Equal(new[] { (a, 4), (b, 1) }, MembersOf(saved));
        Assert.Equal(new[] { "Synthetic A", "Synthetic B" }, saved.Members.Select(m => m.Drawing!.Name));
        var fetched = await client.GetFromJsonAsync<DrawingSetRecord>($"/api/sets/{set.Id}", Options);
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(fetched));

        var json = JsonNode.Parse(await client.GetStringAsync($"/api/sets/{set.Id}"))!;
        var joined = json["members"]![1]!["drawing"]!;
        Assert.Equal(b.ToString(), json["members"]![1]!["drawingId"]!.GetValue<string>());
        Assert.Equal("waterjet", joined["cutType"]!.GetValue<string>());
        Assert.Equal("inches", joined["units"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_ExistingId_ConflictsWithTheCurrentRecord()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var set = Set();
        await Create(client, set);

        using var response = await client.PostAsync("/api/sets", Json(Set("Second writer", set.Id)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var current = await Read(response);
        Assert.Equal(("Synthetic kit", 1), (current.Name, current.Revision));
    }

    public static TheoryData<string, string> RejectedSets()
    {
        var drawing = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
        var valid = JsonSerializer.Serialize(Set(members: (drawing, 2)), Options);
        string With(string property, JsonNode? value)
        {
            var node = JsonNode.Parse(valid)!.AsObject();
            node[property] = value;
            return node.ToJsonString();
        }
        string Without(string property)
        {
            var node = JsonNode.Parse(valid)!.AsObject();
            node.Remove(property);
            return node.ToJsonString();
        }
        JsonArray Members(params (Guid Drawing, object? Quantity)[] members) =>
            new(members.Select(m => (JsonNode)new JsonObject { ["drawingId"] = m.Drawing.ToString(), ["quantity"] = JsonValue.Create(m.Quantity) }).ToArray());

        return new TheoryData<string, string>
        {
            { "empty id", With("id", Guid.Empty.ToString()) },
            { "blank name", With("name", " ") },
            { "null name", With("name", null) },
            { "long name", With("name", new string('n', 201)) },
            { "NUL customer", With("customer", "a\0b") },
            { "long notes", With("notes", new string('x', 4001)) },
            { "missing members", Without("members") },
            { "null members", With("members", null) },
            { "members not an array", With("members", "a") },
            { "null member", With("members", new JsonArray((JsonNode?)null)) },
            { "empty drawing id", With("members", Members((Guid.Empty, 1))) },
            { "zero quantity", With("members", Members((drawing, 0))) },
            { "quantity over the limit", With("members", Members((drawing, 100_001))) },
            { "fractional quantity", With("members", Members((drawing, 1.5))) },
            { "repeated drawing", With("members", Members((drawing, 1), (drawing, 2))) },
            { "unknown drawing", With("members", Members((Guid.NewGuid(), 1))) },
            { "too many members", With("members", Members(Enumerable.Range(0, 501).Select(_ => (drawing, (object?)1)).ToArray())) },
            { "not an object", "[1]" },
            { "malformed", "{" },
            { "empty", "" },
        };
    }

    [Theory]
    [MemberData(nameof(RejectedSets))]
    public async Task Create_InvalidSet_IsRejectedWithNoState(string row, string json)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        // The one drawing the valid members name.
        await AddDrawing(client, "Synthetic A", r => r.Id = Guid.Parse("00000000-0000-0000-0000-0000000000d1"));

        using var response = await client.PostAsync("/api/sets", Json(json));

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, row);
        await AssertNoSets(client);
    }

    [Fact]
    public async Task Create_WrongContentTypeOrOversizedBody_IsRejected()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var text = await client.PostAsync("/api/sets", new StringContent(JsonSerializer.Serialize(Set(), Options)));
        var padded = Set();
        padded.Notes = new string(' ', DrawingSetRules.MaxBodyBytes);
        using var oversized = await client.PostAsync("/api/sets", Json(padded));

        Assert.Equal(HttpStatusCode.BadRequest, text.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Contains("too large", await oversized.Content.ReadAsStringAsync());
        await AssertNoSets(client);
    }

    [Fact]
    public async Task Update_FollowsTheIfMatchContract()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var drawing = await AddDrawing(client);
        var set = await Create(client, Set(members: (drawing, 1)));
        var url = $"/api/sets/{set.Id}";

        async Task<HttpStatusCode> Put(string? ifMatch, DrawingSetRecord body, string target)
        {
            using var response = await client.SendAsync(Guarded(HttpMethod.Put, target, ifMatch, Json(body)));
            return response.StatusCode;
        }

        Assert.Equal((HttpStatusCode)428, await Put(null, Set("Edit", set.Id), url));
        Assert.Equal(HttpStatusCode.BadRequest, await Put("1", Set("Edit", set.Id), url));
        Assert.Equal(HttpStatusCode.BadRequest, await Put("\"1\"", Set("Edit", Guid.NewGuid()), url));
        Assert.Equal(HttpStatusCode.NotFound, await Put("\"1\"", Set("Edit", Guid.Empty), $"/api/sets/{Guid.NewGuid()}"));
        using (var stale = await client.SendAsync(Guarded(HttpMethod.Put, url, "\"2\"", Json(Set("Edit", set.Id)))))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var current = await Read(stale);
            Assert.Equal((1, "Synthetic kit"), (current.Revision, current.Name));
            Assert.Single(current.Members);
        }

        // An id-less body updates the route's set.
        var edit = Set("Edited kit", Guid.Empty, (drawing, 7));
        using var saved = await client.SendAsync(Guarded(HttpMethod.Put, url, "\"1\"", Json(edit)));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var updated = await Read(saved);
        Assert.Equal((2, "Edited kit"), (updated.Revision, updated.Name));
        Assert.Equal(new[] { (drawing, 7) }, MembersOf(updated));
    }

    [Fact]
    public async Task Update_EchoingAReadRecord_IsANoOp()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var drawing = await AddDrawing(client);
        var created = await Create(client, Set(members: (drawing, 3)));
        var set = (await client.GetFromJsonAsync<DrawingSetRecord>($"/api/sets/{created.Id}", Options))!;
        Assert.NotNull(set.Members[0].Drawing);

        using var response = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/sets/{set.Id}", "\"1\"", Json(set)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await Read(response)).Revision);
        var history = await client.GetFromJsonAsync<DrawingSetPage>($"/api/sets/{set.Id}/revisions", Options);
        Assert.Equal(1, history!.Total);
    }

    [Fact]
    public async Task ArchiveAndRestore_HideFromBrowsingAndGuardWrites()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var set = await Create(client, Set());
        var url = $"/api/sets/{set.Id}";

        using (var missing = await client.SendAsync(Guarded(HttpMethod.Post, url + "/archive", null)))
            Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        using (var archived = await client.SendAsync(Guarded(HttpMethod.Post, url + "/archive", "\"1\"")))
            Assert.True((await Read(archived)).Archived);
        using (var again = await client.SendAsync(Guarded(HttpMethod.Post, url + "/archive", "\"2\"")))
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        using (var write = await client.SendAsync(Guarded(HttpMethod.Put, url, "\"2\"", Json(Set("Edit", set.Id)))))
            Assert.Equal(HttpStatusCode.Conflict, write.StatusCode);

        Assert.Empty((await client.GetFromJsonAsync<DrawingSetPage>("/api/sets/query", Options))!.Items);
        Assert.Single((await client.GetFromJsonAsync<DrawingSetPage>("/api/sets/query?includeArchived=true", Options))!.Items);
        Assert.True((await client.GetFromJsonAsync<DrawingSetRecord>(url, Options))!.Archived);

        using (var restored = await client.SendAsync(Guarded(HttpMethod.Post, url + "/restore", "\"2\"")))
        {
            var record = await Read(restored);
            Assert.Equal((3, false), (record.Revision, record.Archived));
        }
        using (var unknown = await client.SendAsync(Guarded(HttpMethod.Post, $"/api/sets/{Guid.NewGuid()}/restore", "\"1\"")))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Query_FiltersSortsAndPagesInSql()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var drawing = await AddDrawing(client);
        await Create(client, Set("Gamma frame", members: (drawing, 1)));
        var alpha = Set("alpha kit");
        alpha.Customer = "Other  Customer";
        await Create(client, alpha);
        var beta = Set("Beta kit");
        beta.Notes = "Contains 100% literal_text";
        await Create(client, beta);

        async Task<string[]> Names(string query) =>
            (await client.GetFromJsonAsync<DrawingSetPage>("/api/sets/query" + query, Options))!.Items.Select(s => s.Name).ToArray();

        Assert.Equal(new[] { "alpha kit", "Beta kit", "Gamma frame" }, await Names("?sort=name&order=asc"));
        Assert.Equal(new[] { "alpha kit", "Beta kit" }, await Names("?search=KIT&sort=name&order=asc"));
        Assert.Equal(new[] { "Beta kit" }, await Names("?search=100%25"));
        Assert.Equal(new[] { "Beta kit" }, await Names("?search=l_t"));
        Assert.Equal(new[] { "alpha kit" }, await Names("?customer=%20other%20customer"));
        Assert.Equal(new[] { "Beta kit" }, await Names("?sort=name&order=asc&offset=1&limit=1"));
        var page = await client.GetFromJsonAsync<DrawingSetPage>("/api/sets/query?limit=1", Options);
        Assert.Equal((3, 1), (page!.Total, page.Items.Count));
        Assert.Empty(page.Items[0].Members);
        var gamma = (await client.GetFromJsonAsync<DrawingSetPage>("/api/sets/query?search=gamma", Options))!.Items.Single();
        Assert.Equal(1, gamma.MemberCount);
    }

    [Theory]
    [InlineData("?bogus=1")]
    [InlineData("?search=a&search=b")]
    [InlineData("?sort=memberCount")]
    [InlineData("?sort=1")]
    [InlineData("?order=up")]
    [InlineData("?includeArchived=yes")]
    [InlineData("?offset=-1")]
    [InlineData("?limit=0")]
    [InlineData("?limit=501")]
    [InlineData("?materialName=Steel")]
    public async Task Query_RefusesUnknownRepeatedOrOutOfRangeParameters(string query)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/sets/query" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Revisions_PageWithoutMembersAndReadOneWithItsMembers()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var a = await AddDrawing(client, "Synthetic A");
        var b = await AddDrawing(client, "Synthetic B");
        var set = await Create(client, Set(members: [(a, 1), (b, 2)]));
        using (await client.SendAsync(Guarded(HttpMethod.Put, $"/api/sets/{set.Id}", "\"1\"", Json(Set("Edited", set.Id, (b, 9)))))) { }

        var history = (await client.GetFromJsonAsync<DrawingSetPage>($"/api/sets/{set.Id}/revisions?limit=1&offset=1", Options))!;
        Assert.Equal((2, 1, 2), (history.Total, history.Items.Single().Revision, history.Items.Single().MemberCount));
        Assert.Empty(history.Items.Single().Members);

        var first = (await client.GetFromJsonAsync<DrawingSetRecord>($"/api/sets/{set.Id}/revisions/1", Options))!;
        Assert.Equal("Synthetic kit", first.Name);
        Assert.Equal(new[] { (a, 1), (b, 2) }, MembersOf(first));
        Assert.Equal("Synthetic B", first.Members[1].Drawing!.Name);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/sets/{set.Id}/revisions/3")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/sets/{set.Id}/revisions/0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/sets/{set.Id}/revisions?limit=201")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/sets/{set.Id}/revisions?page=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/sets/{Guid.NewGuid()}/revisions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/sets/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task UsedIn_ListsSetsWhoseCurrentRevisionContainsTheDrawing()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var a = await AddDrawing(client, "Synthetic A");
        var b = await AddDrawing(client, "Synthetic B");
        await Create(client, Set("Kit one", members: (a, 1)));
        var two = await Create(client, Set("Kit two", members: [(a, 1), (b, 1)]));
        using (await client.SendAsync(Guarded(HttpMethod.Put, $"/api/sets/{two.Id}", "\"1\"", Json(Set("Kit two", two.Id, (b, 1)))))) { }

        var usedA = (await client.GetFromJsonAsync<DrawingSetPage>($"/api/drawings/{a}/sets", Options))!;
        var usedB = (await client.GetFromJsonAsync<DrawingSetPage>($"/api/drawings/{b}/sets", Options))!;

        Assert.Equal(new[] { "Kit one" }, usedA.Items.Select(s => s.Name));
        Assert.Equal(new[] { "Kit two" }, usedB.Items.Select(s => s.Name));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/drawings/{Guid.NewGuid()}/sets")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/drawings/{a}/sets?limit=0")).StatusCode);
    }

    [Fact]
    public async Task ArchivedDrawing_StaysListedWithItsFlagAndLatestRevision()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var drawing = await AddDrawing(client);
        var set = await Create(client, Set(members: (drawing, 2)));
        using (await client.SendAsync(Guarded(HttpMethod.Post, $"/api/drawings/{drawing}/archive", "\"1\""))) { }

        var read = (await client.GetFromJsonAsync<DrawingSetRecord>($"/api/sets/{set.Id}", Options))!;

        var member = read.Members.Single();
        Assert.Equal((drawing, 2, true, 2), (member.DrawingId, member.Quantity, member.Drawing!.Archived, member.Drawing.Revision));
        // An archived drawing may still be added to a set.
        Assert.Equal(1, (await Create(client, Set("Another", members: (drawing, 1)))).MemberCount);
    }

    private static async Task AssertNoSets(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<DrawingSetPage>("/api/sets/query?includeArchived=true", Options);
        Assert.Equal(0, page!.Total);
    }
}
