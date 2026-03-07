using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public class BestFitFilter
    {
        public double MaxPlateWidth { get; set; }
        public double MaxPlateHeight { get; set; }
        public double MaxAspectRatio { get; set; } = 5.0;
        public double MinUtilization { get; set; } = 0.3;

        public void Apply(List<BestFitResult> results)
        {
            foreach (var result in results)
            {
                if (!result.Keep)
                    continue;

                if (result.ShortestSide > System.Math.Min(MaxPlateWidth, MaxPlateHeight))
                {
                    result.Keep = false;
                    result.Reason = "Exceeds plate dimensions";
                    continue;
                }

                var aspect = result.LongestSide / result.ShortestSide;

                if (aspect > MaxAspectRatio)
                {
                    result.Keep = false;
                    result.Reason = string.Format("Aspect ratio {0:F1} exceeds max {1}", aspect, MaxAspectRatio);
                    continue;
                }

                if (result.Utilization < MinUtilization)
                {
                    result.Keep = false;
                    result.Reason = string.Format("Utilization {0:P0} below minimum", result.Utilization);
                    continue;
                }

                result.Reason = "Valid";
            }
        }
    }
}
