namespace OpenNest.Forms;

/// <summary>Operator-facing names for <see cref="ClampEdge"/>, relative to the plate origin.</summary>
internal static class ClampEdgeText
{
    public static readonly ClampEdge[] All =
    {
        ClampEdge.OriginSide,
        ClampEdge.FarSide,
        ClampEdge.OriginEnd,
        ClampEdge.FarEnd,
    };

    public static string Name(ClampEdge edge) =>
        edge switch
        {
            ClampEdge.FarSide => "Far side (X)",
            ClampEdge.OriginEnd => "Origin end (Y)",
            ClampEdge.FarEnd => "Far end (Y)",
            _ => "Origin side (X)",
        };

    /// <summary>Lowercase phrase for status text, e.g. "the far side".</summary>
    public static string Phrase(ClampEdge edge) =>
        edge switch
        {
            ClampEdge.FarSide => "the far side",
            ClampEdge.OriginEnd => "the origin end",
            ClampEdge.FarEnd => "the far end",
            _ => "the origin side",
        };
}
