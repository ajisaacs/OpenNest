namespace OpenNest.Posts.GravographIS
{
    public sealed class GravographISWriterOptions
    {
        public double DepthInches { get; set; } = 0.25;

        public int FeedMmPerSec { get; set; } = 35;

        // IS8000 work envelope in millimeters, from the operator-set upper-left
        // work origin. Defaults to the catalog 0.610 m x 1.220 m bed. With an
        // OpenNest quadrant-4 plate, motion is allowed right (+X) and down (-Y).
        public double WorkEnvelopeXMm { get; set; } = 610.0;
        public double WorkEnvelopeYMm { get; set; } = 1220.0;

        // When true, the writer throws an InvalidOperationException naming the
        // offending polyline and segment before any out-of-envelope record is
        // emitted. Disable only for off-machine encoding tests.
        public bool EnvelopeGuardEnabled { get; set; } = true;

        // When true, lift at the end of the last cut and return to the
        // operator-set origin before shutting the job down.
        public bool ReturnToOriginAtEnd { get; set; } = true;
    }
}
