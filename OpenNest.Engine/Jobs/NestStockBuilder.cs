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
            var size = new Size(option.Width, option.Length);
            stock.Add(new NestPlateStock(
                $"option-{i}",
                size,
                quantityPerOption,
                template.PartSpacing,
                template.EdgeSpacing,
                template.Quadrant,
                option.Cost == 0 ? null : option.Cost,
                ClampZonesFor(template, size)
            )
            { ClampRail = template.ClampRail });
        }
        NestJobValidator.ValidateStockCosts(stock);
        return stock.AsReadOnly();
    }

    /// <summary>
    /// A template following the machine clamp rail offers each sheet size with the rail
    /// resolved for that size (clamps are fixed to the machine, not the sheet); explicit
    /// template zones are offered unchanged.
    /// </summary>
    private static IReadOnlyList<ClampZone> ClampZonesFor(Plate template, Size size) =>
        template.ClampRail?.ResolveFor(size, template.Quadrant)
        ?? new List<ClampZone>(template.ClampZones);

    /// <summary>Single-sheet callers may not silently create additional sheets.</summary>
    public static IReadOnlyList<NestPlateStock> SinglePlate(Plate plate) =>
        FromTemplate(plate, null, quantityPerOption: 1);
}
