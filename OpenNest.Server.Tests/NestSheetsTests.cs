using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Server.Tests;

/// <summary>
/// <c>GET /api/nests/{id}/sheets</c> reads sheet sizes and part counts from the stored archive,
/// skipping the same trailing new-plate slot the record's plate count skips.
/// </summary>
public sealed class NestSheetsTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task SinglePlate_ReturnsItsSizeAndParts()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var id = await Create(client, NestApiTests.SyntheticNest());

        using var response = await client.GetAsync($"/api/nests/{id}/sheets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sheets = (await response.Content.ReadFromJsonAsync<NestSheets>(Options))!;
        Assert.Equal((id, 1), (sheets.NestId, sheets.Revision));
        Assert.Equal([(20.0, 30.0, 1)], Sizes(sheets));
        Assert.Equal([("Synthetic rectangle", 1, 1)], Parts(sheets));
    }

    [Fact]
    public async Task PlateQuantity_MultipliesSheetsAndNestedParts()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var nest = NestApiTests.SyntheticNest();
        var plate = nest.Plates.Single();
        plate.Quantity = 3;
        plate.Parts.Add(new Part(nest.Drawings.Single(), new Vector(10, 3)));

        var sheets = await Sheets(client, nest);

        Assert.Equal([(20.0, 30.0, 3)], Sizes(sheets));
        Assert.Equal([("Synthetic rectangle", 1, 6)], Parts(sheets));
    }

    [Fact]
    public async Task TrailingEmptyPlate_IsNotASheet()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var nest = NestApiTests.SyntheticNest();
        nest.Plates.Add(new Plate(new Size(48, 96)) { Quantity = 1 });

        var sheets = await Sheets(client, nest);

        Assert.Equal([(20.0, 30.0, 1)], Sizes(sheets));
    }

    [Fact]
    public async Task SameSizePlates_GroupEvenWhenStoredRotated()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var nest = NestApiTests.SyntheticNest();
        var drawing = nest.Drawings.Single();
        var rotated = new Plate(new Size(30, 20)) { Quantity = 2 };
        rotated.Parts.Add(new Part(drawing, new Vector(2, 3)));
        nest.Plates.Add(rotated);
        var larger = new Plate(new Size(60, 10.125)) { Quantity = 1 };
        larger.Parts.Add(new Part(drawing, new Vector(2, 3)));
        nest.Plates.Add(larger);

        var sheets = await Sheets(client, nest);

        Assert.Equal([(10.125, 60.0, 1), (20.0, 30.0, 3)], Sizes(sheets));
        Assert.Equal([("Synthetic rectangle", 1, 4)], Parts(sheets));
    }

    [Fact]
    public async Task DrawingWithNothingNested_IsListedWithZero()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var nest = NestApiTests.SyntheticNest();
        var unplaced = new Drawing("Another rectangle", nest.Drawings.Single().Program);
        unplaced.Quantity.Required = 5;
        nest.Drawings.Add(unplaced);

        var sheets = await Sheets(client, nest);

        Assert.Equal([("Another rectangle", 5, 0), ("Synthetic rectangle", 1, 1)], Parts(sheets));
    }

    // NestWriter never writes a part without its drawing; NestReader drops one if it finds it.
    [Fact]
    public async Task PartWithMissingDrawing_IsSkipped()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var archive = TestNests.Zip("nest.json", """
            {"drawings":[{"id":1,"name":"Kept","quantity":{"required":2}}],
             "plates":[{"id":1,"size":{"width":48,"length":96},"quantity":2,
                        "parts":[{"drawingId":1},{"drawingId":7}]}]}
            """);
        var id = await Create(client, archive);

        var sheets = (await client.GetFromJsonAsync<NestSheets>($"/api/nests/{id}/sheets", Options))!;

        Assert.Equal([(48.0, 96.0, 2)], Sizes(sheets));
        Assert.Equal([("Kept", 2, 2)], Parts(sheets));
    }

    [Fact]
    public async Task Plates_ListEachPlateWithItsStockItemCodes()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var nest = NestApiTests.SyntheticNest();
        var drawing = nest.Drawings.Single();
        nest.Plates.Single().StockItemCodes = ["DROP-1182"];
        // Stored rotated: width is still the short side. Two of three sheets are coded.
        var bundle = new Plate(new Size(120, 60)) { Quantity = 3 };
        bundle.Parts.Add(new Part(drawing, new Vector(2, 3)));
        bundle.StockItemCodes = ["SHEET-456", "SHEET-456"];
        nest.Plates.Add(bundle);
        var uncoded = new Plate(new Size(48, 96)) { Quantity = 1 };
        uncoded.Parts.Add(new Part(drawing, new Vector(2, 3)));
        nest.Plates.Add(uncoded);
        nest.Plates.Add(new Plate(new Size(48, 96)) { Quantity = 1 });

        var sheets = await Sheets(client, nest);

        Assert.Equal(
            [
                (0, 20.0, 30.0, 1, "DROP-1182"),
                (1, 60.0, 120.0, 3, "SHEET-456,SHEET-456"),
                (2, 48.0, 96.0, 1, ""),
            ],
            Plates(sheets));
        Assert.Equal([(20.0, 30.0, 1), (48.0, 96.0, 1), (60.0, 120.0, 3)], Sizes(sheets));
    }

    [Fact]
    public async Task Plates_ReadStockItemCodesAsNestReaderDoes()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var archive = TestNests.Zip("nest.json", """
            {"drawings":[{"id":1,"name":"Kept","quantity":{"required":2}}],
             "plates":[{"id":1,"size":{"width":48,"length":96},"quantity":2,"parts":[{"drawingId":1}],
                        "stockItemCodes":["A",7,"B","C"]},
                       {"id":2,"size":{"width":30,"length":40},"quantity":2,"parts":[{"drawingId":1}],
                        "stockItemCodes":["D","","E"]},
                       {"id":3,"size":{"width":30,"length":40},"quantity":1,"parts":[{"drawingId":1}],
                        "stockItemCodes":{"bad":true}}]}
            """);
        var id = await Create(client, archive);

        var sheets = (await client.GetFromJsonAsync<NestSheets>($"/api/nests/{id}/sheets", Options))!;

        Assert.Equal([(0, 48.0, 96.0, 2, "A,B"), (1, 30.0, 40.0, 2, "D"), (2, 30.0, 40.0, 1, "")], Plates(sheets));
    }

    [Fact]
    public async Task Plates_SerializeAsTheDocumentedShape()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var nest = NestApiTests.SyntheticNest();
        nest.Plates.Single().StockItemCodes = ["DROP-1182"];
        var id = await Create(client, nest);

        using var document = JsonDocument.Parse(await client.GetStringAsync($"/api/nests/{id}/sheets"));

        var plate = Assert.Single(document.RootElement.GetProperty("plates").EnumerateArray());
        Assert.Equal(
            ["index", "width", "length", "quantity", "stockItemCodes"],
            plate.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("DROP-1182", Assert.Single(plate.GetProperty("stockItemCodes").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task UnknownNest_Returns404()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/nests/{Guid.NewGuid()}/sheets");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static (double, double, int)[] Sizes(NestSheets sheets) =>
        sheets.Sheets.Select(s => (s.Width, s.Length, s.Quantity)).ToArray();

    private static (int, double, double, int, string)[] Plates(NestSheets sheets) =>
        sheets.Plates.Select(p => (p.Index, p.Width, p.Length, p.Quantity, string.Join(",", p.StockItemCodes))).ToArray();

    private static (string, int, int)[] Parts(NestSheets sheets) =>
        sheets.Parts.Select(p => (p.Name, p.Required, p.Nested)).ToArray();

    private static async Task<NestSheets> Sheets(HttpClient client, Nest nest)
    {
        var id = await Create(client, nest);
        return (await client.GetFromJsonAsync<NestSheets>($"/api/nests/{id}/sheets", Options))!;
    }

    private static Task<Guid> Create(HttpClient client, Nest nest) => Create(client, NestApiTests.Archive(nest));

    private static async Task<Guid> Create(HttpClient client, byte[] archive)
    {
        using var created = await client.PostAsync("/api/nests",
            NestRevisionTests.Body(new NestRecord { Name = "Sheets" }, archive));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<NestRecord>(Options))!.Id;
    }
}
