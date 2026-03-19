using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Api;

public class NestRequest
{
    public IReadOnlyList<NestRequestPart> Parts { get; init; } = [];
    public Size SheetSize { get; init; } = new(60, 120);
    public string Material { get; init; } = "Steel, A1011 HR";
    public double Thickness { get; init; } = 0.06;
    public double Spacing { get; init; } = 0.1;
    public NestStrategy Strategy { get; init; } = NestStrategy.Auto;
    public CutParameters Cutting { get; init; } = CutParameters.Default;
}
