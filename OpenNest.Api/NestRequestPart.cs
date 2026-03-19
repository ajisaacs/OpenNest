namespace OpenNest.Api;

public class NestRequestPart
{
    public string DxfPath { get; init; }
    public int Quantity { get; init; } = 1;
    public bool AllowRotation { get; init; } = true;
    public int Priority { get; init; } = 0;
}
