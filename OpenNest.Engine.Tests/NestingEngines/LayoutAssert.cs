using OpenNest.Converters;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using Xunit;

namespace OpenNest.Engine.Tests.NestingEngines;

public static class LayoutAssert
{
    public static void Valid(NestJob job, NestJobResult result)
    {
        var violations = NestLayoutCheck.Violations(job, result);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.Equal(Enumerable.Range(0, result.Plates.Count), result.Plates.Select(p => p.PlateIndex));
        foreach (var f in result.Fulfillment)
            Assert.Equal(f.Requested, f.Placed + f.Unplaced);
        foreach (var sheet in result.Plates)
        {
            var s = sheet.Stock;
            var work = s.WorkArea;
            foreach (var pose in sheet.Placements)
            {
                var part = job.Parts.Single(p => p.Id == pose.PartId);
                Assert.True(part.Rotation.Allows(pose.Rotation));
                var geometry = ConvertProgram.ToGeometry(DrawingJobMapper.ToProgram(part.Geometry))
                    .Where(e => SpecialLayers.IsMaterial(e.Layer)).ToArray();
                foreach (var entity in geometry) { entity.Rotate(pose.Rotation); entity.Offset(pose.X, pose.Y); }
                var b = (L: geometry.Min(e => e.Left), B: geometry.Min(e => e.Bottom),
                    R: geometry.Max(e => e.Right), T: geometry.Max(e => e.Top));
                Assert.True(b.L >= work.Left - 1e-7 && b.B >= work.Bottom - 1e-7
                    && b.R <= work.Right + 1e-7 && b.T <= work.Top + 1e-7);
            }
        }
        foreach (var part in job.Parts)
        {
            var placed = result.Plates.SelectMany(s => s.Placements).Where(p => p.PartId == part.Id).ToArray();
            Assert.Equal(Enumerable.Range(0, placed.Length), placed.Select(p => p.InstanceIndex).Order());
            var fulfillment = result.Fulfillment.Single(f => f.PartId == part.Id);
            Assert.Equal(placed.Length, fulfillment.Placed);
            Assert.Equal(part.Quantity, fulfillment.Placed + fulfillment.Unplaced);
        }
        foreach (var usage in result.StockUsage)
        {
            var stock = job.Plates.Single(s => s.Id == usage.StockId);
            Assert.Equal(result.Plates.Count(s => s.StockId == stock.Id), usage.Used);
            Assert.Equal(stock.Quantity - usage.Used, usage.Remaining);
            Assert.True(usage.Remaining is null or >= 0);
        }
    }
}
