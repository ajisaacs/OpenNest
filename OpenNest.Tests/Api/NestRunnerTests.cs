using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenNest.Api;
using OpenNest.Converters;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Api;

public class NestRunnerTests
{
    [Fact]
    public async Task ExplicitPricesSelectCheaperStockAndSurviveArchive()
    {
        var path = CreateTempSquareDxf(4, 4);
        var archive = Path.ChangeExtension(path, ".nestquote");
        try
        {
            var response = await NestRunner.RunAsync(new NestRequest
            {
                Engine = "Rectangles",
                Parts = [new NestRequestPart { DxfPath = path, Quantity = 1 }],
                Plates = [new NestRequestPlate { Id = "small", Size = new Size(10, 10), Quantity = 1, Cost = 50 },
                    new NestRequestPlate { Id = "large", Size = new Size(12, 12), Quantity = 1, Cost = 7 }],
            });
            Assert.Equal(NestValidationStatus.Valid, response.ValidationStatus);
            Assert.Equal("large", Assert.Single(response.PlateStockMappings).StockId);
            Assert.Equal("supplied-cost", response.Costs.Basis);
            Assert.Equal(7, response.Costs.GrossTotal);
            Assert.Equal(7, response.Costs.NetScore);
            Assert.Equal(0, response.Costs.SalvageCredit);
            await response.SaveAsync(archive);
            var reopened = await NestResponse.LoadAsync(archive);
            Assert.Equal(7, reopened.Costs.GrossTotal);
            Assert.Equal(new[] { 50d, 7d }, reopened.Nest.PlateOptions.Select(p => p.Cost));
            Assert.Equal(7, reopened.Request.Plates[1].Cost);
        }
        finally
        {
            File.Delete(path);
            File.Delete(archive);
        }
    }

    [Fact]
    public async Task MixedCostsFailBeforeImport()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => NestRunner.RunAsync(new NestRequest
        {
            Parts = [new NestRequestPart { DxfPath = "missing.dxf", Quantity = 1 }],
            Plates = [new NestRequestPlate { Id = "priced", Size = new Size(10, 10), Cost = 2 },
                new NestRequestPlate { Id = "missing", Size = new Size(12, 12) }],
        }));
        Assert.Contains("Missing: missing", error.Message);
    }

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
            // Quantity-weighted: 5 placed 4x4 parts over 25 + 81 physical sheet area.
            Assert.Equal(80d / 106d, response.Utilization, precision: 6);
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
    public async Task RunAsync_FiniteStockExhaustion_PreservesUnplacedRequirement()
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
                            Quantity = 2,
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
            Assert.Equal("square", drawing.Name);
        }
        finally
        {
            File.Delete(dxfPath);
        }
    }

    [Fact]
    public async Task RunAsync_AllowRotationFalse_KeepsOnlyRotatedFitsUnplaced()
    {
        // A 5x2 bar on a sheet 4 long in X and 6 wide in Y fits only rotated 90 degrees,
        // so a run that ignored AllowRotation would place it instead of reporting it unplaced.
        // Size is (width = Y, length = X).
        var dxfPath = CreateTempSquareDxf(5, 2);

        try
        {
            NestRequest Request(bool allowRotation) =>
                new()
                {
                    Parts =
                    [
                        new NestRequestPart
                        {
                            Id = "bar",
                            DxfPath = dxfPath,
                            Quantity = 1,
                            AllowRotation = allowRotation,
                        },
                    ],
                    Plates =
                    [
                        new NestRequestPlate
                        {
                            Id = "tall-sheet",
                            Size = new Size(6, 4),
                            Quantity = 1,
                        },
                    ],
                };

            var control = await NestRunner.RunAsync(Request(true));
            Assert.Equal(1, Assert.Single(control.Fulfillment).Placed);

            var locked = await NestRunner.RunAsync(Request(false));
            Assert.Equal(NestJobStatus.Incomplete, locked.Status);
            Assert.Equal(NestJobStopReason.NoPlacementFound, locked.StopReason);
            var fulfillment = Assert.Single(locked.Fulfillment);
            Assert.Equal(1, fulfillment.Requested);
            Assert.Equal(0, fulfillment.Placed);
            Assert.Equal(1, fulfillment.Unplaced);
            Assert.Equal(0, locked.SheetCount);
            var drawing = Assert.Single(locked.Nest.Drawings);
            Assert.Equal(OpenNest.Math.Angle.TwoPI, drawing.Constraints.StepAngle);
            Assert.Equal(0, drawing.Constraints.StartAngle);
            Assert.Equal(0, drawing.Constraints.EndAngle);
        }
        finally
        {
            File.Delete(dxfPath);
        }
    }

    [Fact]
    public async Task RunAsync_ExplicitPlates_FlowStockSettingsToPlacedPlates()
    {
        var dxfPath = CreateTempSquareDxf(2, 2);

        try
        {
            var response = await NestRunner.RunAsync(
                new NestRequest
                {
                    Parts = [new NestRequestPart { Id = "square", DxfPath = dxfPath, Quantity = 1 }],
                    Plates =
                    [
                        new NestRequestPlate
                        {
                            Id = "remnant",
                            Size = new Size(10, 10),
                            Quantity = 1,
                            PartSpacing = 0.2,
                            EdgeSpacing = new Spacing(1, 2, 3, 4),
                            Quadrant = 3,
                        },
                    ],
                }
            );

            Assert.Equal(1, Assert.Single(response.Fulfillment).Placed);
            var plate = Assert.Single(response.Nest.Plates);
            Assert.Equal(0.2, plate.PartSpacing);
            Assert.Equal(new Spacing(1, 2, 3, 4), plate.EdgeSpacing);
            Assert.Equal(3, plate.Quadrant);
        }
        finally
        {
            File.Delete(dxfPath);
        }
    }

    [Fact]
    public async Task RunAsync_DuplicatePartIds_ThrowsBeforeNesting()
    {
        // Missing DXFs: if the duplicate check ever runs after import, these surface
        // FileNotFoundException instead, turning this red.
        var request = new NestRequest
        {
            Parts =
            [
                new NestRequestPart { Id = "duplicate", DxfPath = "missing-a.dxf" },
                new NestRequestPart { Id = "duplicate", DxfPath = "missing-b.dxf" },
            ],
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => NestRunner.RunAsync(request));
        Assert.Contains("unique", error.Message);
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
