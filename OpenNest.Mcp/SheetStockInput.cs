namespace OpenNest.Mcp;

/// <summary>Request-local physical inventory for one whole-job solve.</summary>
public sealed class SheetStockInput
{
    public double Width { get; set; }
    public double Length { get; set; }
    public int Quantity { get; set; }
}
