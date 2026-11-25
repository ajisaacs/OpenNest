namespace OpenNest.CNC.CuttingStrategy
{
    /// <summary>What Plan Cutting etches in each part's label box (see docs/label-box.md).</summary>
    public enum PartLabelMode
    {
        /// <summary>Nothing is etched.</summary>
        Off,

        /// <summary>The drawing name only, for example <c>1234</c>.</summary>
        PartNumber,

        /// <summary>The drawing name in its label box, and each bend's group letter in a circle beside its bend line.</summary>
        PartNumberAndBendGroups,
    }
}
