using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public class BestFitFilter
    {
        public double MaxPlateWidth { get; set; }
        public double MaxPlateHeight { get; set; }
        public double MaxAspectRatio { get; set; } = 5.0;

        /// <summary>
        /// A high-aspect pair is kept anyway when this much of its rotated bounding box is
        /// actual part area. Utilization is only ever an exception here, never a rejection:
        /// thin-framed, hollow, or concave (e.g. S-shaped) parts have inherently low
        /// part-to-bbox utilization, yet can nest tightly — their quality is judged by the
        /// pair bounding-box area the results are sorted on, not by utilization.
        /// </summary>
        public double UtilizationOverride { get; set; } = 0.75;

        public void Apply(List<BestFitResult> results)
        {
            foreach (var result in results)
            {
                if (!result.Keep)
                    continue;

                if (
                    result.ShortestSide > System.Math.Min(MaxPlateWidth, MaxPlateHeight)
                    || result.LongestSide > System.Math.Max(MaxPlateWidth, MaxPlateHeight)
                )
                {
                    result.Keep = false;
                    result.Reason = "Exceeds plate dimensions";
                    continue;
                }

                var aspect = result.LongestSide / result.ShortestSide;

                if (aspect > MaxAspectRatio && result.Utilization < UtilizationOverride)
                {
                    result.Keep = false;
                    result.Reason = string.Format(
                        "Aspect ratio {0:F1} exceeds max {1}",
                        aspect,
                        MaxAspectRatio
                    );
                    continue;
                }

                result.Reason = "Valid";
            }
        }
    }
}
