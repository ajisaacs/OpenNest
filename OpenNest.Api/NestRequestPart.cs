namespace OpenNest.Api;

public class NestRequestPart
{
    /// <summary>Optional stable requirement identity. NestRunner derives part-{requestIndex} when omitted.</summary>
    public string Id { get; init; }
    public string DxfPath { get; init; }
    public int Quantity { get; init; } = 1;
    public bool AllowRotation { get; init; } = true;
    /// <summary>Nesting order: higher numbers nest first.</summary>
    public int Priority { get; init; } = 0;
}
