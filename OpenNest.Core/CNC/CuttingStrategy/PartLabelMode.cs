namespace OpenNest.CNC.CuttingStrategy
{
    /// <summary>What Plan Cutting etches in each part's label box (see docs/label-box.md).</summary>
    public enum PartLabelMode
    {
        /// <summary>Nothing is etched.</summary>
        Off,

        /// <summary>The drawing name only, for example <c>1234</c>.</summary>
        PartNumber,

        /// <summary>The drawing name and the bend groups of its bends, each letter etched in a circle, for example <c>1234 AB</c>.</summary>
        PartNumberAndBendGroups,
    }
}
