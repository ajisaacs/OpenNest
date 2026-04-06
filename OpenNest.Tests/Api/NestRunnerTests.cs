using System;
using System.IO;
using System.Threading.Tasks;
using OpenNest.Api;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Api;

public class NestRunnerTests
{
    [Fact]
    public async Task RunAsync_SinglePart_ProducesResponse()
    {
        var dxfPath = CreateTempSquareDxf(2, 2);

        try
        {
            var request = new NestRequest
            {
                Parts = [new NestRequestPart { DxfPath = dxfPath, Quantity = 4 }],
                SheetSize = new Size(10, 10),
                Spacing = 0.1
            };

            var response = await NestRunner.RunAsync(request);

            Assert.NotNull(response);
            Assert.NotNull(response.Nest);
            Assert.True(response.SheetCount >= 1);
            Assert.True(response.Utilization > 0);
            Assert.Equal(request, response.Request);
        }
        finally
        {
            File.Delete(dxfPath);
        }
    }

    [Fact]
    public async Task RunAsync_BadDxfPath_Throws()
    {
        var request = new NestRequest
        {
            Parts = [new NestRequestPart { DxfPath = "nonexistent.dxf", Quantity = 1 }]
        };

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => NestRunner.RunAsync(request));
    }

    [Fact]
    public async Task RunAsync_EmptyParts_Throws()
    {
        var request = new NestRequest { Parts = [] };

        await Assert.ThrowsAsync<ArgumentException>(
            () => NestRunner.RunAsync(request));
    }

    private static string CreateTempSquareDxf(double width, double height)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(width, 0)));
        shape.Entities.Add(new Line(new Vector(width, 0), new Vector(width, height)));
        shape.Entities.Add(new Line(new Vector(width, height), new Vector(0, height)));
        shape.Entities.Add(new Line(new Vector(0, height), new Vector(0, 0)));

        var pgm = ConvertGeometry.ToProgram(shape);
        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.dxf");

        Dxf.ExportProgram(pgm, path);

        return path;
    }
}
