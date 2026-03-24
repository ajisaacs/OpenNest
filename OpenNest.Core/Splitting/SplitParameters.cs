namespace OpenNest;

public enum SplitType
{
    Straight,
    WeldGapTabs,
    SpikeGroove
}

public class SplitParameters
{
    public SplitType Type { get; set; } = SplitType.Straight;

    // Tab parameters
    public double TabWidth { get; set; } = 1.0;
    public double TabHeight { get; set; } = 0.125;
    public int TabCount { get; set; } = 3;

    // Spike/Groove parameters
    public double SpikeDepth { get; set; } = 0.5;
    public double SpikeAngle { get; set; } = 60.0; // degrees
    public int SpikePairCount { get; set; } = 2;

    /// <summary>
    /// Max protrusion from the split edge (for auto-fit plate size calculation).
    /// </summary>
    public double FeatureOverhang => Type switch
    {
        SplitType.WeldGapTabs => TabHeight,
        SplitType.SpikeGroove => SpikeDepth,
        _ => 0
    };
}
