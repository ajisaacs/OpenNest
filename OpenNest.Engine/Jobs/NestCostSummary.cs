using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs;

public sealed record NestStockCost(string StockId, int Used, double? Cost, double GrossSubtotal);

/// <summary>Costs of validated physical sheets, excluding unplaced-demand penalties.</summary>
public sealed record NestCostSummary(string Basis, IReadOnlyList<NestStockCost> Stock,
    double GrossTotal, double SalvageCredit, double NetScore)
{
    public static NestCostSummary FromAccepted(NestPipelineResult result)
    {
        if (!result.CanKeep || !result.IsValid)
            throw new ArgumentException("Cost reporting requires a validated proposal.", nameof(result));
        var stock = result.Job.Plates.Select(s =>
        {
            var used = result.Plates.Count(p => p.Stock.Id == s.Id);
            return new NestStockCost(s.Id, used, s.Cost,
                used == 0 ? 0 : NestJobCost.RequireFinite(used * NestJobCost.GrossSheetCost(s)));
        }).ToArray();
        var gross = NestJobCost.RequireFinite(stock.Sum(s => s.GrossSubtotal));
        var net = NestJobCost.RequireFinite(result.Plates.Sum(sheet =>
        {
            if (sheet.Parts.Count == 0 || result.Job.Options.SalvageRate == 0
                || result.Job.Options.MinimumSalvageDimension <= 0)
                return NestJobCost.GrossSheetCost(sheet.Stock);
            var bounds = sheet.Parts.Select(NestLayoutCheck.MaterialBounds).ToArray();
            var left = bounds.Min(b => b.Left);
            var bottom = bounds.Min(b => b.Bottom);
            return NestJobCost.NetSheetCost(result.Job.Options, sheet.Stock,
                new Box(left, bottom, bounds.Max(b => b.Right) - left, bounds.Max(b => b.Top) - bottom));
        }));
        return new NestCostSummary(NestJobCost.UsesExplicitCosts(result.Job)
            ? "supplied-cost" : "area", stock, gross, NestJobCost.RequireFinite(gross - net), net);
    }
}
