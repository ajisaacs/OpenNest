namespace OpenNest
{
    public class TimingInfo
    {
        public int PierceCount;

        public int IntersectionCount;

        public double TravelDistance;

        public double CutDistance;

        /// <summary>
        /// Minutes added by last-resort clamp removal (D9): once per physical sheet, for
        /// every clamp <see cref="Plate.RemovedClamps"/> lists. Zero for a plate with no
        /// removal, so existing timing is unaffected.
        /// </summary>
        public double ClampRemovalMinutes;

        public static TimingInfo operator +(TimingInfo info1, TimingInfo info2)
        {
            return new TimingInfo
            {
                CutDistance = info1.CutDistance + info2.CutDistance,
                IntersectionCount = info1.IntersectionCount + info2.IntersectionCount,
                TravelDistance = info1.TravelDistance + info2.TravelDistance,
                PierceCount = info1.PierceCount + info2.PierceCount,
                ClampRemovalMinutes = info1.ClampRemovalMinutes + info2.ClampRemovalMinutes,
            };
        }

        public static TimingInfo operator -(TimingInfo info1, TimingInfo info2)
        {
            return new TimingInfo
            {
                CutDistance = info1.CutDistance - info2.CutDistance,
                IntersectionCount = info1.IntersectionCount - info2.IntersectionCount,
                TravelDistance = info1.TravelDistance - info2.TravelDistance,
                PierceCount = info1.PierceCount - info2.PierceCount,
                ClampRemovalMinutes = info1.ClampRemovalMinutes - info2.ClampRemovalMinutes,
            };
        }

        public static TimingInfo operator *(TimingInfo info1, TimingInfo info2)
        {
            return new TimingInfo
            {
                CutDistance = info1.CutDistance * info2.CutDistance,
                IntersectionCount = info1.IntersectionCount * info2.IntersectionCount,
                TravelDistance = info1.TravelDistance * info2.TravelDistance,
                PierceCount = info1.PierceCount * info2.PierceCount,
                ClampRemovalMinutes = info1.ClampRemovalMinutes * info2.ClampRemovalMinutes,
            };
        }

        public static TimingInfo operator *(TimingInfo info1, int factor)
        {
            return new TimingInfo
            {
                CutDistance = info1.CutDistance * factor,
                IntersectionCount = info1.IntersectionCount * factor,
                TravelDistance = info1.TravelDistance * factor,
                PierceCount = info1.PierceCount * factor,
                ClampRemovalMinutes = info1.ClampRemovalMinutes * factor,
            };
        }
    }
}
