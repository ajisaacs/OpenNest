using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using OpenNest.Api;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Api;

public class NestResponsePersistenceTests
{
    [Fact]
    public async Task SaveAsync_LoadAsync_RoundTripsCompleteResponseMetadata()
    {
        var nest = CreateNest("test-part", new Size(60, 120));
        var request = new NestRequest
        {
            Parts = [new NestRequestPart { Id = "test-part", DxfPath = "test.dxf", Quantity = 5 }],
            Plates = [new NestRequestPlate { Id = "sheet", Size = new Size(60, 120), Quantity = 1, PartSpacing = 0.1 }],
            Material = "Steel",
            Thickness = 0.125,
            Spacing = 0.1
        };
        var original = new NestResponse
        {
            SheetCount = 1,
            Utilization = 0.75,
            CutTime = TimeSpan.FromMinutes(12.5),
            Elapsed = TimeSpan.FromSeconds(3.2),
            Status = NestJobStatus.Complete,
            StopReason = NestJobStopReason.Completed,
            Fulfillment = [new NestPartFulfillment("test-part", 5, 5, 0)],
            StockUsage = [new NestStockUsage("sheet", 1, 0)],
            PlateStockMappings = [new NestPlateStockMapping(0, "sheet")],
            Nest = nest,
            Request = request
        };
        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.nestquote");

        try
        {
            await original.SaveAsync(path);
            var loaded = await NestResponse.LoadAsync(path);

            Assert.Equal(NestResponse.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.Equal(original.SheetCount, loaded.SheetCount);
            Assert.Equal(original.Utilization, loaded.Utilization, precision: 4);
            Assert.Equal(original.CutTime, loaded.CutTime);
            Assert.Equal(original.Elapsed, loaded.Elapsed);
            Assert.Equal(NestJobStatus.Complete, loaded.Status);
            Assert.Equal(NestJobStopReason.Completed, loaded.StopReason);
            Assert.Equal(original.Fulfillment, loaded.Fulfillment);
            Assert.Equal(original.StockUsage, loaded.StockUsage);
            Assert.Equal(original.PlateStockMappings, loaded.PlateStockMappings);

            Assert.Equal(original.Request.Material, loaded.Request.Material);
            Assert.Equal(original.Request.Thickness, loaded.Request.Thickness);
            Assert.Equal(original.Request.Parts.Count, loaded.Request.Parts.Count);
            Assert.Equal("test-part", loaded.Request.Parts[0].Id);
            Assert.Equal(original.Request.Parts[0].DxfPath, loaded.Request.Parts[0].DxfPath);
            Assert.Equal(original.Request.Parts[0].Quantity, loaded.Request.Parts[0].Quantity);
            Assert.Equal("sheet", Assert.Single(loaded.Request.Plates!).Id);

            Assert.NotNull(loaded.Nest);
            Assert.Single(loaded.Nest.Plates);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_LegacyArchiveWithoutFulfillment_LeavesStatusUnspecified()
    {
        var path = Path.Combine(Path.GetTempPath(), $"legacy-{Guid.NewGuid()}.nestquote");

        try
        {
            await WriteLegacyArchiveAsync(path, CreateNest("legacy-part", new Size(60, 120)));

            var loaded = await NestResponse.LoadAsync(path);

            Assert.Equal(0, loaded.SchemaVersion);
            Assert.Null(loaded.Status);
            Assert.Null(loaded.StopReason);
            Assert.Empty(loaded.Fulfillment);
            Assert.Empty(loaded.StockUsage);
            Assert.Empty(loaded.PlateStockMappings);
            Assert.Equal(1, loaded.SheetCount);
            Assert.Equal(0.75, loaded.Utilization, precision: 4);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveAsync_LoadAsync_IncompleteResponsePreservesIdsAndUnplacedWithoutDxf()
    {
        var dxfPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid()}.dxf");
        var path = Path.Combine(Path.GetTempPath(), $"incomplete-{Guid.NewGuid()}.nestquote");
        Assert.False(File.Exists(dxfPath));
        var original = new NestResponse
        {
            SheetCount = 1,
            Utilization = 0.4,
            CutTime = TimeSpan.FromMinutes(2),
            Elapsed = TimeSpan.FromMilliseconds(500),
            Status = NestJobStatus.Incomplete,
            StopReason = NestJobStopReason.StockExhausted,
            Fulfillment = [new NestPartFulfillment("custom-id", 3, 1, 2)],
            StockUsage = [new NestStockUsage("finite-stock", 1, 0)],
            PlateStockMappings = [new NestPlateStockMapping(0, "finite-stock")],
            Nest = CreateNest("custom-id", new Size(10, 10)),
            Request = new NestRequest
            {
                Parts = [new NestRequestPart { Id = "custom-id", DxfPath = dxfPath, Quantity = 3 }],
                Plates = [new NestRequestPlate { Id = "finite-stock", Size = new Size(10, 10), Quantity = 1 }]
            }
        };

        try
        {
            await original.SaveAsync(path);
            var loaded = await NestResponse.LoadAsync(path);

            Assert.False(File.Exists(dxfPath));
            Assert.Equal(NestJobStatus.Incomplete, loaded.Status);
            Assert.Equal(NestJobStopReason.StockExhausted, loaded.StopReason);
            Assert.Equal(new NestPartFulfillment("custom-id", 3, 1, 2), Assert.Single(loaded.Fulfillment));
            Assert.Equal(new NestStockUsage("finite-stock", 1, 0), Assert.Single(loaded.StockUsage));
            Assert.Equal(new NestPlateStockMapping(0, "finite-stock"), Assert.Single(loaded.PlateStockMappings));
            Assert.Equal("custom-id", Assert.Single(loaded.Request.Parts).Id);
            Assert.Equal("finite-stock", Assert.Single(loaded.Request.Plates!).Id);
            Assert.Single(loaded.Nest.Drawings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Nest CreateNest(string drawingName, Size size)
    {
        var nest = new Nest("test-nest");
        var plate = new Plate(size);
        var drawing = new Drawing(drawingName);
        nest.Drawings.Add(drawing);
        plate.Parts.Add(new Part(drawing));
        nest.Plates.Add(plate);
        return nest;
    }

    private static async Task WriteLegacyArchiveAsync(string path, Nest nest)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        await WriteEntryAsync(zip, "request.json", """
            {"parts":[{"dxfPath":"legacy-missing.dxf","quantity":2}],"sheetSize":{"width":60,"length":120},"material":"Steel","thickness":0.06,"spacing":0.1,"strategy":0}
            """);
        await WriteEntryAsync(zip, "response.json", """
            {"sheetCount":1,"utilization":0.75,"cutTimeTicks":120000,"elapsedTicks":340000}
            """);

        var nestEntry = zip.CreateEntry("nest.nest");
        await using var stream = nestEntry.Open();
        new NestWriter(nest).Write(stream);
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string contents)
    {
        var entry = zip.CreateEntry(name);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(contents);
    }
}
