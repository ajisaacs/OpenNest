namespace OpenNest
{
    public enum CutDirection
    {
        TowardOrigin,
        AwayFromOrigin,
    }

    public class CutOffSettings
    {
        public double PartClearance { get; set; } = 0.02;
        public double Overtravel { get; set; }
        public double MinSegmentLength { get; set; } = 0.05;
        public CutDirection CutDirection { get; set; } = CutDirection.AwayFromOrigin;

        /// <summary>
        /// Whether new cut-offs lock the head height and overcut past the nominal far sheet edge
        /// by <see cref="HeadLockOvercut"/> (see <see cref="CutOff.HeadLockOvercut"/>). Off until
        /// the machine's post has head-height lock codes.
        /// </summary>
        public bool LockHeadHeight { get; set; }

        /// <summary>How far past the nominal far edge a head-locked cut-off cuts, in model units.</summary>
        public double HeadLockOvercut { get; set; } = 1.5;

        /// <summary>The head-locked overcut a new cut-off gets from these settings; zero when off.</summary>
        public double NewCutOffHeadLockOvercut => LockHeadHeight ? HeadLockOvercut : 0;
    }
}
