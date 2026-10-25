using System.Net;
using System.Net.Http;
using System.Text;
using OpenNest.Data.MaterialLedger;

namespace OpenNest.Tests.Data;

public class MaterialLedgerClientTests
{
    private const string MappedBody = """
        {
          "mapped": true,
          "specs": [{ "materialSpecId": 7, "displayName": "A36 1/4", "thicknessIn": 0.25, "gaugeLabel": null }],
          "items": [{
            "itemCode": "D-101", "materialSpecId": 7, "kind": "Drop", "widthIn": 24.5, "lengthIn": 36,
            "quantity": 1, "shape": "Rectangular", "locationCode": "R1-A", "locationName": "Rack 1 A",
            "parentLocationCode": "R1", "costPerUnit": null, "lastObservedAtUtc": "2026-10-20T14:00:00Z"
          }]
        }
        """;

    private static readonly MaterialLedgerSettings Configured = new() { BaseUrl = "http://ledger:5000/", ApiKey = "secret-key" };

    [Fact]
    public async Task NotConfiguredNeverSendsARequest()
    {
        var sent = 0;
        var client = Client(_ => { sent++; return Json(MappedBody); }, new MaterialLedgerSettings());
        Assert.IsType<LedgerStockResult.NotConfigured>(await client.GetStockAsync("Steel", "A36", 0.25, default));
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task MappedStockIsOkAndSendsTheKeyAndQuery()
    {
        HttpRequestMessage? seen = null;
        var client = Client(request => { seen = request; return Json(MappedBody); });

        var ok = Assert.IsType<LedgerStockResult.Ok>(await client.GetStockAsync("Stainless Steel", "304 #4", 0.18750, default));

        Assert.Equal("secret-key", Assert.Single(seen!.Headers.GetValues(MaterialLedgerClient.ApiKeyHeader)));
        Assert.Equal("/api/stock/opennest?material=Stainless%20Steel&grade=304%20%234&thicknessIn=0.1875",
            seen.RequestUri!.PathAndQuery);
        Assert.Equal("ledger", seen.RequestUri.Host);
        var spec = Assert.Single(ok.Specs);
        Assert.Equal(0.25m, spec.ThicknessIn);
        var item = Assert.Single(ok.Items);
        Assert.Equal("D-101", item.ItemCode);
        Assert.Equal(LedgerItemKinds.Drop, item.Kind);
        Assert.Equal(24.5m, item.WidthIn);
        Assert.Null(item.CostPerUnit);
    }

    [Fact]
    public async Task RankedStockReadsRanksAndCatalogNames()
    {
        var client = Client(_ => Json("""
            {
              "mapped": true,
              "specs": [
                { "materialSpecId": 12, "displayName": "A1011 HR 16 ga", "thicknessIn": 0.0598, "gaugeLabel": "16 ga",
                  "rank": 1, "materialFamily": "Steel", "grade": "A1011", "finish": "HR" },
                { "materialSpecId": 13, "displayName": "A1011 P&O 16 ga", "thicknessIn": 0.0598, "gaugeLabel": "16 ga",
                  "rank": 2, "materialFamily": "Steel", "grade": "A1011", "finish": null }
              ],
              "items": [{
                "itemCode": "FS-4896", "materialSpecId": 13, "rank": 2, "kind": "FullSheet", "widthIn": 48, "lengthIn": 96,
                "quantity": 1, "shape": "Rectangular", "locationCode": "R1-A", "locationName": "Rack 1 A",
                "parentLocationCode": "R1", "costPerUnit": null, "lastObservedAtUtc": "2026-10-20T14:00:00"
              }]
            }
            """));

        var ok = Assert.IsType<LedgerStockResult.Ok>(await client.GetStockAsync("Steel", "HR", 0.0598, default));

        Assert.Equal(new int?[] { 1, 2 }, ok.Specs.Select(s => s.Rank));
        Assert.Equal(("Steel", "A1011", "HR"), (ok.Specs[0].MaterialFamily, ok.Specs[0].Grade, ok.Specs[0].Finish));
        Assert.Null(ok.Specs[1].Finish);
        Assert.Equal(2, Assert.Single(ok.Items).Rank);
    }

    [Fact]
    public async Task AnOlderLedgerWithoutRanksStillDeserializes()
    {
        var ok = Assert.IsType<LedgerStockResult.Ok>(await Client(_ => Json(MappedBody))
            .GetStockAsync("Steel", "A36", 0.25, default));

        var spec = Assert.Single(ok.Specs);
        Assert.Null(spec.Rank);
        Assert.Null(spec.MaterialFamily);
        Assert.Null(spec.Grade);
        Assert.Null(spec.Finish);
        Assert.Null(Assert.Single(ok.Items).Rank);
    }

    [Fact]
    public async Task UnmappedMaterialIsDistinctFromEmptyStock()
    {
        var unmapped = Client(_ => Json("""{ "mapped": false, "specs": [], "items": [] }"""));
        Assert.IsType<LedgerStockResult.Unmapped>(await unmapped.GetStockAsync("Unobtainium", "", 1, default));

        var empty = Client(_ => Json("""{ "mapped": true, "specs": [], "items": [] }"""));
        var ok = Assert.IsType<LedgerStockResult.Ok>(await empty.GetStockAsync("Steel", "A36", 0.25, default));
        Assert.Empty(ok.Items);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedKeyIsUnauthorized(HttpStatusCode status)
    {
        var client = Client(_ => new HttpResponseMessage(status));
        Assert.IsType<LedgerStockResult.Unauthorized>(await client.GetStockAsync("Steel", "A36", 0.25, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "no OpenNest stock route")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    public async Task OtherStatusesAreUnreachable(HttpStatusCode status, string reason)
    {
        var client = Client(_ => new HttpResponseMessage(status));
        var result = Assert.IsType<LedgerStockResult.Unreachable>(await client.GetStockAsync("Steel", "A36", 0.25, default));
        Assert.Contains(reason, result.Reason);
    }

    [Fact]
    public async Task NetworkFailureAndGarbageAreUnreachable()
    {
        var down = Client(_ => throw new HttpRequestException("No such host is known."));
        Assert.Contains("No such host", Assert.IsType<LedgerStockResult.Unreachable>(
            await down.GetStockAsync("Steel", "A36", 0.25, default)).Reason);

        var garbage = Client(_ => Json("<html>login</html>"));
        Assert.IsType<LedgerStockResult.Unreachable>(await garbage.GetStockAsync("Steel", "A36", 0.25, default));
    }

    [Fact]
    public async Task InvalidUrlIsUnreachableWithoutARequest()
    {
        var client = Client(_ => throw new InvalidOperationException("sent"),
            new MaterialLedgerSettings { BaseUrl = "ftp://ledger" });
        Assert.IsType<LedgerStockResult.Unreachable>(await client.GetStockAsync("Steel", "A36", 0.25, default));
    }

    [Fact]
    public async Task SlowLedgerTimesOutAsUnreachable()
    {
        using var http = new HttpClient(new HangingHandler());
        var client = new MaterialLedgerClient(http, Configured, TimeSpan.FromMilliseconds(50));
        var result = Assert.IsType<LedgerStockResult.Unreachable>(await client.GetStockAsync("Steel", "A36", 0.25, default));
        Assert.Contains("No answer", result.Reason);
        Assert.Equal(TimeSpan.FromSeconds(5), MaterialLedgerClient.RequestTimeout);
    }

    [Fact]
    public async Task CallerCancellationThrows()
    {
        using var http = new HttpClient(new HangingHandler());
        var client = new MaterialLedgerClient(http, Configured);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetStockAsync("Steel", "A36", 0.25, cancel.Token));
    }

    [Fact]
    public async Task UnknownKindAndShapeStillDeserialize()
    {
        var client = Client(_ => Json("""
            {
              "mapped": true, "specs": [],
              "items": [{
                "itemCode": "X-1", "materialSpecId": 7, "kind": "Coil", "widthIn": 48, "lengthIn": 96,
                "quantity": 2, "shape": "Circular", "locationCode": "", "locationName": "",
                "parentLocationCode": null, "costPerUnit": 12.5, "lastObservedAtUtc": "2026-10-20T14:00:00Z",
                "someNewField": 1
              }]
            }
            """));
        var ok = Assert.IsType<LedgerStockResult.Ok>(await client.GetStockAsync("Steel", "A36", 0.25, default));
        var item = Assert.Single(ok.Items);
        Assert.Equal("Coil", item.Kind);
        Assert.Equal("Circular", item.Shape);
    }

    private static readonly Guid NestId = Guid.Parse("4f7c2a1e-0b9d-4e55-8a6f-1c2d3e4f5a6b");

    [Fact]
    public async Task HoldsAreListedWithTheKeyAndNestId()
    {
        HttpRequestMessage? seen = null;
        var client = Client(request =>
        {
            seen = request;
            return Json("""
                {
                  "openNestNestId": "4f7c2a1e-0b9d-4e55-8a6f-1c2d3e4f5a6b",
                  "holds": [
                    { "materialReservationId": 91, "itemCode": "DROP-1182", "widthIn": 30, "lengthIn": 52.5,
                      "status": "Approved", "locationCode": "B3", "createdAtUtc": "2026-10-20T14:00:00Z" },
                    { "materialReservationId": 92, "itemCode": "FS-4896", "widthIn": 48, "lengthIn": 96,
                      "status": "ClosedConsumed", "locationCode": null, "createdAtUtc": "2026-10-20T14:00:00Z" }
                  ]
                }
                """);
        });

        var ok = Assert.IsType<LedgerHoldsResult.Ok>(await client.GetHoldsAsync(NestId, default));

        Assert.Equal("/api/stock/holds?openNestNestId=4f7c2a1e-0b9d-4e55-8a6f-1c2d3e4f5a6b", seen!.RequestUri!.PathAndQuery);
        Assert.Equal("secret-key", Assert.Single(seen.Headers.GetValues(MaterialLedgerClient.ApiKeyHeader)));
        Assert.Equal(2, ok.Holds.Count);
        var first = ok.Holds[0];
        Assert.Equal((91L, "DROP-1182", 30m, 52.5m, LedgerHoldStatuses.Approved, "B3"),
            (first.MaterialReservationId, first.ItemCode, first.WidthIn, first.LengthIn, first.Status, first.LocationCode));
        Assert.Null(ok.Holds[1].LocationCode);
    }

    [Fact]
    public async Task NoHoldsIsAnEmptyList()
    {
        var client = Client(_ => Json("""{ "openNestNestId": "4f7c2a1e-0b9d-4e55-8a6f-1c2d3e4f5a6b", "holds": [] }"""));
        Assert.Empty(Assert.IsType<LedgerHoldsResult.Ok>(await client.GetHoldsAsync(NestId, default)).Holds);

        var missing = Client(_ => Json("""{ "openNestNestId": "4f7c2a1e-0b9d-4e55-8a6f-1c2d3e4f5a6b" }"""));
        Assert.Empty(Assert.IsType<LedgerHoldsResult.Ok>(await missing.GetHoldsAsync(NestId, default)).Holds);
    }

    [Fact]
    public async Task UnknownHoldStatusIsKept()
    {
        var client = Client(_ => Json("""
            {
              "openNestNestId": "4f7c2a1e-0b9d-4e55-8a6f-1c2d3e4f5a6b",
              "holds": [{ "materialReservationId": 5, "itemCode": "D-1", "widthIn": 10, "lengthIn": 20,
                "status": "OnTruck", "locationCode": "Y", "createdAtUtc": "2026-10-20T14:00:00Z", "someNewField": 1 }]
            }
            """));
        var hold = Assert.Single(Assert.IsType<LedgerHoldsResult.Ok>(await client.GetHoldsAsync(NestId, default)).Holds);
        Assert.Equal("OnTruck", hold.Status);
        Assert.False(LedgerHoldStatuses.IsOpen(hold.Status));
    }

    [Fact]
    public async Task HoldFailuresAreResults()
    {
        var sent = 0;
        var notConfigured = Client(_ => { sent++; return Json("{}"); }, new MaterialLedgerSettings());
        Assert.IsType<LedgerHoldsResult.NotConfigured>(await notConfigured.GetHoldsAsync(NestId, default));
        Assert.Equal(0, sent);

        var down = Client(_ => throw new HttpRequestException("No such host is known."));
        Assert.Contains("No such host", Assert.IsType<LedgerHoldsResult.Unreachable>(
            await down.GetHoldsAsync(NestId, default)).Reason);

        var oldLedger = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Contains("no OpenNest holds route", Assert.IsType<LedgerHoldsResult.Unreachable>(
            await oldLedger.GetHoldsAsync(NestId, default)).Reason);

        var rejected = Client(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        Assert.IsType<LedgerHoldsResult.Unauthorized>(await rejected.GetHoldsAsync(NestId, default));

        var garbage = Client(_ => Json("<html>login</html>"));
        Assert.IsType<LedgerHoldsResult.Unreachable>(await garbage.GetHoldsAsync(NestId, default));

        using var http = new HttpClient(new HangingHandler());
        var slow = new MaterialLedgerClient(http, Configured, TimeSpan.FromMilliseconds(50));
        Assert.Contains("No answer", Assert.IsType<LedgerHoldsResult.Unreachable>(
            await slow.GetHoldsAsync(NestId, default)).Reason);
    }

    private static MaterialLedgerClient Client(Func<HttpRequestMessage, HttpResponseMessage> reply,
        MaterialLedgerSettings? settings = null) =>
        new(new HttpClient(new Handler(reply)), settings ?? Configured);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(reply(request));
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
