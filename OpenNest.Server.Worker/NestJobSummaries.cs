using OpenNest.Data;
using OpenNest.Engine.Jobs;

namespace OpenNest.Server.Worker;

/// <summary>
/// A finished job's result in numbers for job lists, computed on the worker because the server
/// never opens results. The desktop's replay reaches the same plates, so these are the numbers
/// its review shows.
/// </summary>
public static class NestJobSummaries
{
    public static NestJobSummary From(NestPipelineResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var sheetArea = 0.0;
        var partArea = 0.0;
        var placed = 0;
        var sheets = 0;
        foreach (var plate in result.Plates)
        {
            sheets += plate.Quantity;
            placed += plate.Quantity * plate.Parts.Count;
            sheetArea += plate.Quantity * plate.Stock.Size.Length * plate.Stock.Size.Width;
            partArea += plate.Quantity * plate.Parts.Sum(part => part.BaseDrawing.Area);
        }
        return new NestJobSummary
        {
            PartsPlaced = placed,
            PartsUnplaced = result.Unplaced.Sum(u => u.Count),
            Sheets = sheets,
            Utilization = sheetArea > 0 ? System.Math.Clamp(partArea / sheetArea, 0, 1) : 0,
        };
    }
}
