namespace OpenNest
{
    public class TimingInfo
    {
        public int PierceCount;

        public int IntersectionCount;

        public double TravelDistance;

        public double CutDistance;

        public static TimingInfo operator +(TimingInfo info1, TimingInfo info2)
        {
            return new TimingInfo
            {
                CutDistance = info1.CutDistance + info2.CutDistance,
                IntersectionCount = info1.IntersectionCount + info2.IntersectionCount,
                TravelDistance = info1.TravelDistance + info2.TravelDistance,
                PierceCount = info1.PierceCount + info2.PierceCount
            };
        }

        public static TimingInfo operator -(TimingInfo info1, TimingInfo info2)
        {
            return new TimingInfo
            {
                CutDistance = info1.CutDistance - info2.CutDistance,
                IntersectionCount = info1.IntersectionCount - info2.IntersectionCount,
                TravelDistance = info1.TravelDistance - info2.TravelDistance,
                PierceCount = info1.PierceCount - info2.PierceCount
            };
        }

        public static TimingInfo operator *(TimingInfo info1, TimingInfo info2)
        {
            return new TimingInfo
            {
                CutDistance = info1.CutDistance * info2.CutDistance,
                IntersectionCount = info1.IntersectionCount * info2.IntersectionCount,
                TravelDistance = info1.TravelDistance * info2.TravelDistance,
                PierceCount = info1.PierceCount * info2.PierceCount
            };
        }

        public static TimingInfo operator *(TimingInfo info1, int factor)
        {
            return new TimingInfo
            {
                CutDistance = info1.CutDistance * factor,
                IntersectionCount = info1.IntersectionCount * factor,
                TravelDistance = info1.TravelDistance * factor,
                PierceCount = info1.PierceCount * factor
            };
        }
    }
}
