using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using OpenNest.Api;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Api;

public class RepeatedPlateResponseTests
{
    private static Drawing Square()
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(2, 0)));
        shape.Entities.Add(new Line(new Vector(2, 0), new Vector(2, 2)));
        shape.Entities.Add(new Line(new Vector(2, 2), new Vector(0, 2)));
        shape.Entities.Add(new Line(new Vector(0, 2), new Vector(0, 0)));
        return new Drawing("neutral-square", OpenNest.Converters.ConvertGeometry.ToProgram(shape));
    }

    [Fact]
    public async Task RealEngineArchiveCountsPhysicalSheetsAndWeightedPartsAreaTimeAndCost()
    {
        var path = Path.Combine(Path.GetTempPath(), $"repeated-{Guid.NewGuid()}.dxf");
        var archive = Path.ChangeExtension(path, ".nestquote");
        Dxf.ExportProgram(Square().Program, path);
        try
        {
            var request = new NestRequest
            {
                Engine = "Rectangles",
                Parts = [new NestRequestPart { Id = "external-square", DxfPath = path, Quantity = 9, AllowRotation = false }],
                Plates = [new NestRequestPlate { Id = "finite-stock", Size = new Size(4.5, 4.5), Quantity = 4, PartSpacing = 0.25, Cost = 7 }],
            };
            var response = await NestRunner.RunAsync(request);
            AssertResponse(response);
            await response.SaveAsync(archive);
            File.Delete(path); // Stored layouts and metadata must be sufficient without the DXF.
            var loaded = await NestResponse.LoadAsync(archive);
            AssertResponse(loaded);
            Assert.Equal(response.CutTime, loaded.CutTime);
            Assert.Equal(response.Elapsed, loaded.Elapsed);
            Assert.Equal(response.Nest.Plates.SelectMany(p => p.Parts).Select(p => (p.Location, p.Rotation)),
                loaded.Nest.Plates.SelectMany(p => p.Parts).Select(p => (p.Location, p.Rotation)));
            Assert.All(loaded.Nest.Plates, p => Assert.Equal(0.25, p.PartSpacing));
        }
        finally { File.Delete(path); File.Delete(archive); }
    }

    private static void AssertResponse(NestResponse response)
    {
        Assert.Equal(4, response.SchemaVersion);
        Assert.Equal(NestValidationStatus.Valid, response.ValidationStatus);
        Assert.Equal(NestJobStatus.Complete, response.Status);
        Assert.Empty(response.Violations);
        Assert.Equal(3, response.SheetCount);
        Assert.Equal(new[] { 2, 1 }, response.Nest.Plates.Select(p => p.Quantity));
        Assert.Equal(new[] { 4, 1 }, response.Nest.Plates.Select(p => p.Parts.Count));
        Assert.Equal(new NestPartFulfillment("external-square", 9, 9, 0), Assert.Single(response.Fulfillment));
        Assert.Equal(9, Assert.Single(response.Nest.Drawings).Quantity.Nested);
        Assert.Equal(new NestStockUsage("finite-stock", 3, 1), Assert.Single(response.StockUsage));
        Assert.Equal(new[] { new NestPlateStockMapping(0, "finite-stock"), new NestPlateStockMapping(1, "finite-stock") }, response.PlateStockMappings);
        Assert.Equal(36.0 / (3 * 4.5 * 4.5), response.Utilization, 10);
        var timing = Timing.GetTimingInfo(response.Nest);
        Assert.Equal(72, timing.CutDistance, 8);
        Assert.Equal(9, timing.PierceCount);
        Assert.Equal(Timing.CalculateTime(timing, response.Request.Cutting), response.CutTime);
        Assert.True(response.CutTime > TimeSpan.Zero);
        Assert.Equal("supplied-cost", response.Costs.Basis);
        Assert.Equal(21, response.Costs.GrossTotal);
        Assert.Equal(21, response.Costs.NetScore);
        Assert.Equal(3, Assert.Single(response.Costs.Stock).Used);
    }

    [Fact]
    public async Task SchemaThreePhysicalEntriesLoadWithoutGroupingOrMetadataReinterpretation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"schema3-{Guid.NewGuid()}.nestquote");
        var nest = new Nest();
        var drawing = Square();
        nest.Drawings.Add(drawing);
        for (var i = 0; i < 2; i++)
        {
            var plate = new Plate(4.5, 4.5) { Quantity = 1 };
            plate.Parts.Add(new Part(drawing));
            nest.Plates.Add(plate);
        }
        try
        {
            using (var file = File.Create(path))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("request.json").Open()))
                    await writer.WriteAsync("{\"parts\":[]}");
                using (var writer = new StreamWriter(zip.CreateEntry("response.json").Open()))
                    await writer.WriteAsync("""
                        {"schemaVersion":3,"sheetCount":2,"utilization":0.25,"status":"Complete","stopReason":"Completed","validationStatus":"Valid","fulfillment":[{"partId":"old-id","requested":2,"placed":2,"unplaced":0}],"stockUsage":[{"stockId":"old-stock","used":2,"remaining":1}],"plateStockMappings":[{"plateIndex":0,"stockId":"old-stock"},{"plateIndex":1,"stockId":"old-stock"}]}
                        """);
                using var stream = zip.CreateEntry("nest.nest").Open();
                Assert.True(new NestWriter(nest).Write(stream));
            }
            var response = await NestResponse.LoadAsync(path);
            Assert.Equal(3, response.SchemaVersion);
            Assert.Equal(2, response.SheetCount);
            Assert.Equal(2, response.Nest.Plates.Count);
            Assert.All(response.Nest.Plates, p => Assert.Equal(1, p.Quantity));
            Assert.Equal(0.25, response.Utilization);
            Assert.Equal(new NestPartFulfillment("old-id", 2, 2, 0), Assert.Single(response.Fulfillment));
            Assert.Equal(new NestStockUsage("old-stock", 2, 1), Assert.Single(response.StockUsage));
            Assert.Equal(new[] { 0, 1 }, response.PlateStockMappings.Select(p => p.PlateIndex));
            Assert.Equal(2, Assert.Single(response.Nest.Drawings).Quantity.Nested);
        }
        finally { File.Delete(path); }
    }
}
