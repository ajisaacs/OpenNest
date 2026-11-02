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
            var partAspects = new Dictionary<Drawing, double>(ReferenceEqualityComparer.Instance);

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
                var maxAspect = System.Math.Max(
                    MaxAspectRatio,
                    PartAspect(result.Candidate?.Drawing, partAspects)
                );

                if (aspect > maxAspect && result.Utilization < UtilizationOverride)
                {
                    result.Keep = false;
                    result.Reason = string.Format(
                        "Aspect ratio {0:F1} exceeds max {1:0.#}",
                        aspect,
                        maxAspect
                    );
                    continue;
                }

                result.Reason = "Valid";
            }
        }

        /// <summary>
        /// A pair is never held to a tighter aspect limit than one part alone: two long, thin
        /// parts laid side by side are less elongated than either, so a fixed limit would reject
        /// every pair of a part longer than it. Measured on the drawing's own frame (canonical
        /// for cached pairs); any other frame only understates the aspect.
        /// </summary>
        private static double PartAspect(Drawing drawing, Dictionary<Drawing, double> cache)
        {
            if (drawing?.Program == null)
                return 0;

            if (!cache.TryGetValue(drawing, out var aspect))
            {
                var box = drawing.Program.BoundingBox();
                var shortest = System.Math.Min(box.Width, box.Length);
                aspect =
                    shortest > 0 ? System.Math.Max(box.Width, box.Length) / shortest : 0;
                cache[drawing] = aspect;
            }

            return aspect;
        }
    }
}
