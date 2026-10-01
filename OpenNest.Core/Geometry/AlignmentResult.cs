using System;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Tuning for <see cref="DrawingAligner"/>. Every default is a design choice
    /// under measurement, not a calibrated safe envelope: until calibration
    /// establishes one, every alignment pair still requires operator confirmation.
    /// Values are expressed in the declared units of the drawings being aligned.
    /// </summary>
    public sealed class AlignmentOptions
    {
        /// <summary>Chord-error tolerance used when flattening arcs for measurement.</summary>
        public double FlattenTolerance { get; set; } = 0.01;

        /// <summary>
        /// Target arclength between resampled contour samples. Must resolve the
        /// intended residual gate: a residual below roughly half this spacing is
        /// not meaningful.
        /// </summary>
        public double SamplingSpacing { get; set; } = 0.5;

        /// <summary>Upper bound on samples per contour ring.</summary>
        public int MaxSamplesPerRing { get; set; } = 4000;

        /// <summary>Upper bound on total samples across all rings of one drawing.</summary>
        public int MaxTotalSamples { get; set; } = 20000;

        /// <summary>Trim fraction: worst-distance correspondences excluded from each ICP fit.</summary>
        public double TrimFraction { get; set; } = 0.10;

        /// <summary>Fit iterations per candidate start.</summary>
        public int MaxIterations { get; set; } = 40;

        /// <summary>Pose change (radians) treated as converged.</summary>
        public double RotationEpsilon { get; set; } = 1e-7;

        /// <summary>Translation change in units treated as converged.</summary>
        public double TranslationEpsilon { get; set; } = 1e-7;

        /// <summary>
        /// Correspondences farther than this count as unmatched (changed geometry),
        /// not as fit error. Defaults to a generous envelope; the review gate
        /// reports coverage instead of silently clamping it.
        /// </summary>
        public double OutlierDistance { get; set; } = 10.0;

        /// <summary>Minimum distinct samples required to attempt a fit at all.</summary>
        public int MinSamples { get; set; } = 8;
    }

    /// <summary>Why an alignment is not trustworthy. Absence of all flags is not a
    /// calibrated safe envelope; it only means no review reason fired.</summary>
    [Flags]
    public enum AlignmentReasons
    {
        None = 0,

        /// <summary>The fit stopped on an iteration/work limit or stagnated without converging.</summary>
        FailedConvergence = 1,

        /// <summary>The revised geometry has too few usable samples, too little outer
        /// support, or too little of the target matched to it.</summary>
        InsufficientSupport = 2,

        /// <summary>Significant changed/unmatched boundary spans in either direction.</summary>
        SignificantBoundaryChange = 4,

        /// <summary>Two or more genuinely distinct poses fit near-equally (symmetry,
        /// repeated features). The reported transform is still valid geometry.</summary>
        UnresolvedAlternatives = 8,

        /// <summary>Input geometry is invalid: nonfinite coordinates, degenerate or
        /// unclosed rings, unsupported topology, zero usable perimeter.</summary>
        InvalidGeometry = 16,

        /// <summary>A reflected candidate fits as well as the best rigid one, or the
        /// input cannot exclude reflection. Reflection is never applied silently.</summary>
        ReflectionUncertain = 32,
    }

    /// <summary>
    /// Structured outcome of aligning one revised drawing to one old drawing.
    /// The transform maps NEW points into the OLD drawing-local frame:
    /// reflect about the declared local axis (only when <see cref="Reflection"/> is
    /// true, which the automatic aligner never sets), then rotate by
    /// <see cref="Rotation"/> (radians), then translate by <see cref="Translation"/>.
    /// Diagnostics are in the drawings' declared units. A diagnostic IoU is
    /// bounded to [0, 1] and is not a calibrated probability.
    /// </summary>
    public sealed class AlignmentResult
    {
        public bool Converged { get; init; }
        public double Rotation { get; init; }
        public Vector Translation { get; init; }
        public bool Reflection { get; init; }

        /// <summary>Review reasons; combined across candidate selection. Empty does
        /// not license skipping the operator overlay — no calibrated envelope
        /// exists yet.</summary>
        public AlignmentReasons Reasons { get; init; }

        /// <summary>Trimmed RMS of matched sample-to-segment residuals.</summary>
        public double ResidualRms { get; init; }
        public double ResidualP50 { get; init; }
        public double ResidualP90 { get; init; }

        /// <summary>Fraction of NEW samples matching OLD within the outlier bound, and vice versa.</summary>
        public double NewToOldCoverage { get; init; }
        public double OldToNewCoverage { get; init; }

        /// <summary>Number of distinct converged candidate poses clustered as equivalent.</summary>
        public int EquivalentCandidateCount { get; init; }

        public int Iterations { get; init; }
        public int NewSampleCount { get; init; }
        public int OldSampleCount { get; init; }

        /// <summary>Bounded diagnostic intersection-over-union of the material regions,
        /// or null when regions are unavailable or degenerate. Never a gate input.</summary>
        public double? DiagnosticIoU { get; init; }

        public string FailureMessage { get; init; }
    }
}
