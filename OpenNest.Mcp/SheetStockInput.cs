namespace OpenNest.Mcp;

/// <summary>Request-local physical inventory for one whole-job solve.</summary>
public sealed class SheetStockInput
{
    /// <summary>Positive per-sheet cost in common units; omit on every row for area scoring.</summary>
    public double? Cost { get; set; }

    public double Width { get; set; }
    public double Length { get; set; }
    public int Quantity { get; set; }
}
