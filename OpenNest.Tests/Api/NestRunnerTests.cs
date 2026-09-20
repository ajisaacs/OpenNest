using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenNest.Api;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Api;

public class NestRunnerTests
{
    [Fact]
    public async Task RunAsync_LegacySheetSize_UsesUnlimitedLegacyStockAndDerivedPartId()
    {
        var dxfPath = CreateTempSquareDxf(2, 2);

        try
        {
            var request = new NestRequest
            {
                Parts = [new NestRequestPart { DxfPath = dxfPath, Quantity = 4 }],
                SheetSize = new Size(10, 10),
                Spacing = 0.1,
            };

            var response = await NestRunner.RunAsync(request);

            Assert.Equal(NestJobStatus.Complete, response.Status);
            Assert.Equal(NestJobStopReason.Completed, response.StopReason);
            Assert.Equal("part-0", Assert.Single(response.Fulfillment).PartId);
            Assert.Equal(4, response.Fulfillment[0].Placed);
            var stock = Assert.Single(response.StockUsage);
            Assert.Equal("legacy-sheet", stock.StockId);
            Assert.Null(stock.Remaining);
            Assert.All(
                response.PlateStockMappings,
                mapping => Assert.Equal("legacy-sheet", mapping.StockId)
            );
            Assert.Equal(response.SheetCount, response.PlateStockMappings.Count);
            Assert.NotNull(response.Nest);
            Assert.Contains(response.Nest.Drawings, drawing => drawing.Name == "part-0");
            Assert.True(response.Utilization > 0);
            Assert.Equal(request, response.Request);
        }
        finally
        {
            File.Delete(dxfPath);
        }
    }

    [Fact]
    public async Task RunAsync_ExplicitMixedFinitePlates_UsesPhysicalStockEntries()
    {
        var dxfPath = CreateTempSquareDxf(4, 4);

        try
        {
            var response = await NestRunner.RunAsync(
                new NestRequest
                {
                    Parts =
                    [
                        new NestRequestPart
                        {
                            Id = "square",
                            DxfPath = dxfPath,
                            Quantity = 5,
                        },
                    ],
                    Plates =
                    [
                        new NestRequestPlate
                        {
                            Id = "small",
                            Size = new Size(5, 5),
                            Quantity = 1,
                        },
                        new NestRequestPlate
                        {
                            Id = "large",
                            Size = new Size(9, 9),
                            Quantity = 1,
                        },
                    ],
                }
            );

            Assert.Equal(NestJobStatus.Complete, response.Status);
            Assert.Equal(2, response.SheetCount);
            Assert.Equal(5, Assert.Single(response.Fulfillment).Placed);
            Assert.Equal(0, response.Fulfillment[0].Unplaced);
            Assert.Equal(
                new[] { "large", "small" },
                response.PlateStockMappings.Select(mapping => mapping.StockId).Order()
            );
            Assert.Equal(1, response.StockUsage.Single(usage => usage.StockId == "small").Used);
            Assert.Equal(1, response.StockUsage.Single(usage => usage.StockId == "large").Used);
            Assert.All(response.StockUsage, usage => Assert.Equal(0, usage.Remaining));
        }
        finally
        {
            File.Delete(dxfPath);
        }
    }

    [Fact]
    public async Task RunAsync_ExplicitEmptyPlates_ReportsStockExhausted()
    {
        var dxfPath = CreateTempSquareDxf(2, 2);

        try
        {
            var response = await NestRunner.RunAsync(
                new NestRequest
                {
                    Parts =
                    [
                        new NestRequestPart
                        {
                            Id = "square",
                            DxfPath = dxfPath,
                            Quantity = 1,
                        },
                    ],
                    Plates = [],
                }
            );

            Assert.Equal(NestJobStatus.Incomplete, response.Status);
            Assert.Equal(NestJobStopReason.StockExhausted, response.StopReason);
            Assert.Equal(0, response.SheetCount);
            Assert.Empty(response.StockUsage);
            var fulfillment = Assert.Single(response.Fulfillment);
            Assert.Equal(0, fulfillment.Placed);
            Assert.Equal(1, fulfillment.Unplaced);
            Assert.Empty(response.Nest.Plates);
            Assert.Contains(response.Nest.Drawings, drawing => drawing.Name == "square");
        }
        finally
        {
            File.Delete(dxfPath);
        }
    }

    [Fact]
    public async Task RunAsync_FiniteStockExhaustion_PreservesUnplacedRequirementAndLockedRotation()
    {
        var dxfPath = CreateTempSquareDxf(4, 4);

        try
        {
            var response = await NestRunner.RunAsync(
                new NestRequest
                {
                    Parts =
                    [
                        new NestRequestPart
                        {
                            Id = "locked-square",
                            DxfPath = dxfPath,
                            Quantity = 2,
                            AllowRotation = false,
                        },
                    ],
                    Plates =
                    [
                        new NestRequestPlate
                        {
                            Id = "only-sheet",
                            Size = new Size(5, 5),
                            Quantity = 1,
                        },
                    ],
                }
            );

            Assert.Equal(NestJobStatus.Incomplete, response.Status);
            Assert.Equal(NestJobStopReason.StockExhausted, response.StopReason);
            var fulfillment = Assert.Single(response.Fulfillment);
            Assert.Equal(2, fulfillment.Requested);
            Assert.Equal(1, fulfillment.Placed);
            Assert.Equal(1, fulfillment.Unplaced);
            var stock = Assert.Single(response.StockUsage);
            Assert.Equal(1, stock.Used);
            Assert.Equal(0, stock.Remaining);
            var drawing = Assert.Single(response.Nest.Drawings);
            Assert.Equal("locked-square", drawing.Name);
            Assert.Equal(OpenNest.Math.Angle.TwoPI, drawing.Constraints.StepAngle);
        }
        finally
        {
            File.Delete(dxfPath);
        }
    }

    [Fact]
    public async Task RunAsync_MixedPhysicalSheets_CalculatesWeightedUtilization()
    {
        var dxfPath = CreateTempSquareDxf(4, 4);

        try
        {
            var response = await NestRunner.RunAsync(
                new NestRequest
                {
                    Parts =
                    [
                        new NestRequestPart
                        {
                            Id = "square",
                            DxfPath = dxfPath,
                            Quantity = 5,
                        },
                    ],
                    Plates =
                    [
                        new NestRequestPlate
                        {
                            Id = "small",
                            Size = new Size(5, 5),
                            Quantity = 1,
                        },
                        new NestRequestPlate
                        {
                            Id = "large",
                            Size = new Size(9, 9),
                            Quantity = 1,
                        },
                    ],
                }
            );

            Assert.Equal(2, response.SheetCount);
            Assert.Equal(80d / 106d, response.Utilization, precision: 6);
        }
        finally
        {
            File.Delete(dxfPath);
        }
    }

    [Fact]
    public async Task RunAsync_DuplicatePartIds_ThrowsBeforeNesting()
    {
        var dxfPath = CreateTempSquareDxf(2, 2);

        try
        {
            var request = new NestRequest
            {
                Parts =
                [
                    new NestRequestPart { Id = "duplicate", DxfPath = dxfPath },
                    new NestRequestPart { Id = "duplicate", DxfPath = dxfPath },
                ],
            };

            await Assert.ThrowsAsync<ArgumentException>(() => NestRunner.RunAsync(request));
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
            Parts = [new NestRequestPart { DxfPath = "nonexistent.dxf", Quantity = 1 }],
        };

        await Assert.ThrowsAsync<FileNotFoundException>(() => NestRunner.RunAsync(request));
    }

    [Fact]
    public async Task RunAsync_EmptyParts_Throws()
    {
        var request = new NestRequest { Parts = [] };

        await Assert.ThrowsAsync<ArgumentException>(() => NestRunner.RunAsync(request));
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
