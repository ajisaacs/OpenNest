using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Api;

public class NestRequest
{
    public IReadOnlyList<NestRequestPart> Parts { get; init; } = [];

    /// <summary>
    /// Explicit available physical stock. Null keeps the legacy unlimited SheetSize fallback;
    /// an empty list deliberately means no stock is available.
    /// </summary>
    public IReadOnlyList<NestRequestPlate> Plates { get; init; }
    public Size SheetSize { get; init; } = new(60, 120);

    /// <summary>Built-in whole-job placement strategy. Explicit values take precedence over legacy Strategy.</summary>
    public string PlacementStrategy { get; init; } = "Default";
    public string Material { get; init; } = "Steel, A1011 HR";
    public double Thickness { get; init; } = 0.06;
    public double Spacing { get; init; } = 0.1;

    /// <summary>Legacy compatibility setting; Auto maps to the Default whole-job strategy.</summary>
    public NestStrategy Strategy { get; init; } = NestStrategy.Auto;
    public CutParameters Cutting { get; init; } = CutParameters.Default;
}
