using System;
using System.Collections.Generic;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs;

/// <summary>Shared stock snapshots for whole-job front ends. Plate repeat count is not inventory.</summary>
public static class NestStockBuilder
{
    public static IReadOnlyList<NestPlateStock> FromTemplate(
        Plate template,
        IReadOnlyList<PlateOption> options,
        int? quantityPerOption = null
    )
    {
        ArgumentNullException.ThrowIfNull(template);
        if (options == null || options.Count == 0)
            return new[] { DrawingJobMapper.FromPlate("plate", template, quantityPerOption) };

        var stock = new List<NestPlateStock>(options.Count);
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            ArgumentNullException.ThrowIfNull(option);
            stock.Add(new NestPlateStock(
                $"option-{i}",
                new Size(option.Width, option.Length),
                quantityPerOption,
                template.PartSpacing,
                template.EdgeSpacing,
                template.Quadrant,
                option.Cost == 0 ? null : option.Cost
            ));
        }
        NestJobValidator.ValidateStockCosts(stock);
        return stock.AsReadOnly();
    }

    /// <summary>Single-sheet callers may not silently create additional sheets.</summary>
    public static IReadOnlyList<NestPlateStock> SinglePlate(Plate plate) =>
        FromTemplate(plate, null, quantityPerOption: 1);
}
