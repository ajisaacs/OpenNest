using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Tests.Jobs;

// Frozen pre-PR2 computation: keep independent of NestJobCost to detect scoring drift.
internal static class LegacyNestJobCost
{
    public static double EstimateNetArea(NestJob job, NestJobPlateResult sheet)
    {
        var area = sheet.Stock.Size.Width * sheet.Stock.Size.Length;
        var minimum = job.Options.MinimumSalvageDimension;
        if (job.Options.SalvageRate == 0 || minimum <= 0 || sheet.Placements.Count == 0)
            return area;
        var work = DrawingJobMapper.CreatePlate(sheet.Stock).WorkArea();
        var parts = job.Parts.ToDictionary(p => p.Id);
        var boxes = sheet
            .Placements.Select(p =>
            {
                var part = new Part(DrawingJobMapper.CreateDrawing(parts[p.PartId]));
                part.Rotate(p.Rotation);
                part.Location = new OpenNest.Geometry.Vector(p.X, p.Y);
                part.UpdateBounds();
                return part.BoundingBox;
            })
            .ToList();
        var gap = sheet.Stock.PartSpacing;
        var candidates = new[]
        {
            (work.Length, boxes.Min(b => b.Bottom) - work.Bottom - gap),
            (work.Length, work.Top - boxes.Max(b => b.Top) - gap),
            (boxes.Min(b => b.Left) - work.Left - gap, work.Width),
            (work.Right - boxes.Max(b => b.Right) - gap, work.Width),
        };
        var salvage = candidates
            .Where(c => c.Item1 >= minimum && c.Item2 >= minimum)
            .Select(c => c.Item1 * c.Item2)
            .DefaultIfEmpty(0)
            .Max();
        return area - job.Options.SalvageRate * salvage;
    }
}
