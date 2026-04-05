using System;
using System.IO;
using System.Threading.Tasks;
using OpenNest.Api;
using OpenNest.Geometry;

namespace OpenNest.Tests.Api;

public class NestResponsePersistenceTests
{
    [Fact]
    public async Task SaveAsync_LoadAsync_RoundTrips()
    {
        var nest = new Nest("test-nest");
        var plate = new Plate(new Size(60, 120));
        var drawing = new Drawing("test-part");
        nest.Drawings.Add(drawing);
        plate.Parts.Add(new Part(drawing));
        nest.Plates.Add(plate);

        var request = new NestRequest
        {
            Parts = [new NestRequestPart { DxfPath = "test.dxf", Quantity = 5 }],
            SheetSize = new Size(60, 120),
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
            Nest = nest,
            Request = request
        };

        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.nestquote");

        try
        {
            await original.SaveAsync(path);
            var loaded = await NestResponse.LoadAsync(path);

            Assert.Equal(original.SheetCount, loaded.SheetCount);
            Assert.Equal(original.Utilization, loaded.Utilization, precision: 4);
            Assert.Equal(original.CutTime, loaded.CutTime);
            Assert.Equal(original.Elapsed, loaded.Elapsed);

            Assert.Equal(original.Request.Material, loaded.Request.Material);
            Assert.Equal(original.Request.Thickness, loaded.Request.Thickness);
            Assert.Equal(original.Request.Parts.Count, loaded.Request.Parts.Count);
            Assert.Equal(original.Request.Parts[0].DxfPath, loaded.Request.Parts[0].DxfPath);
            Assert.Equal(original.Request.Parts[0].Quantity, loaded.Request.Parts[0].Quantity);

            Assert.NotNull(loaded.Nest);
            Assert.Single(loaded.Nest.Plates);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
