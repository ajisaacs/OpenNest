namespace OpenNest.Posts.Cincinnati
{
    /// <summary>
    /// Specifies how coordinate positioning is handled between parts.
    /// </summary>
    public enum CoordinateMode
    {
        /// <summary>Set absolute position.</summary>
        G92,

        /// <summary>Use relative/incremental positioning.</summary>
        G91,

        /// <summary>Use machine coordinate system.</summary>
        G53
    }

    /// <summary>
    /// Specifies how G89 (hole drilling/tapping parameters) are provided.
    /// </summary>
    public enum G89Mode
    {
        /// <summary>Use external library file for G89 parameters.</summary>
        LibraryFile,

        /// <summary>Explicitly define G89 parameters in the program.</summary>
        Explicit
    }

    /// <summary>
    /// Specifies where kerf compensation is applied.
    /// </summary>
    public enum KerfMode
    {
        /// <summary>Controller side (using cutter compensation codes).</summary>
        ControllerSide,

        /// <summary>Pre-applied to part geometry during post-processing.</summary>
        PreApplied
    }

    /// <summary>
    /// Specifies which side of the cut line kerf compensation is applied to.
    /// </summary>
    public enum KerfSide
    {
        /// <summary>Kerf applied to the left side of the cut.</summary>
        Left,

        /// <summary>Kerf applied to the right side of the cut.</summary>
        Right
    }

    /// <summary>
    /// Specifies how M47 (optional stop) commands are used.
    /// </summary>
    public enum M47Mode
    {
        /// <summary>Always include M47.</summary>
        Always,

        /// <summary>Include M47 with block delete functionality.</summary>
        BlockDelete,

        /// <summary>Automatically determine M47 placement.</summary>
        Auto,

        /// <summary>Do not use M47.</summary>
        None
    }

    /// <summary>
    /// Specifies when pallet exchange occurs.
    /// </summary>
    public enum PalletMode
    {
        /// <summary>No pallet exchange.</summary>
        None,

        /// <summary>Pallet exchange at end of sheet.</summary>
        EndOfSheet,

        /// <summary>Pallet exchange at start and end of sheet.</summary>
        StartAndEnd
    }

    /// <summary>
    /// Configuration for Cincinnati post processor.
    /// Defines machine-specific parameters, output format, and cutting strategies.
    /// </summary>
    public sealed class CincinnatiPostConfig
    {
        /// <summary>
        /// Gets or sets the configuration name/identifier.
        /// Default: "CL940"
        /// </summary>
        public string ConfigurationName { get; set; } = "CL940";

        /// <summary>
        /// Gets or sets the units for posted output.
        /// Default: Units.Inches
        /// </summary>
        public Units PostedUnits { get; set; } = Units.Inches;

        /// <summary>
        /// Gets or sets the decimal accuracy for numeric output.
        /// Default: 4
        /// </summary>
        public int PostedAccuracy { get; set; } = 4;

        /// <summary>
        /// Gets or sets how coordinate positioning is handled between parts.
        /// Default: CoordinateMode.G92
        /// </summary>
        public CoordinateMode CoordModeBetweenParts { get; set; } = CoordinateMode.G92;

        /// <summary>
        /// Gets or sets whether to use subprograms for sheet operations.
        /// Default: true
        /// </summary>
        public bool UseSheetSubprograms { get; set; } = true;

        /// <summary>
        /// Gets or sets the starting subprogram number for sheet operations.
        /// Default: 101
        /// </summary>
        public int SheetSubprogramStart { get; set; } = 101;

        /// <summary>
        /// Gets or sets whether to use M98 sub-programs for part geometry.
        /// When enabled, each unique part geometry is written as a reusable sub-program
        /// called via M98, reducing output size for nests with repeated parts.
        /// Default: false
        /// </summary>
        public bool UsePartSubprograms { get; set; } = false;

        /// <summary>
        /// Gets or sets the starting sub-program number for part geometry sub-programs.
        /// Default: 200
        /// </summary>
        public int PartSubprogramStart { get; set; } = 200;

        /// <summary>
        /// Gets or sets the subprogram number for variable declarations.
        /// Default: 100
        /// </summary>
        public int VariableDeclarationSubprogram { get; set; } = 100;

        /// <summary>
        /// Gets or sets how G89 parameters are provided.
        /// Default: G89Mode.LibraryFile
        /// </summary>
        public G89Mode ProcessParameterMode { get; set; } = G89Mode.LibraryFile;

        /// <summary>
        /// Gets or sets the default G89 library file path.
        /// Default: empty string
        /// </summary>
        public string DefaultLibraryFile { get; set; } = "";

        /// <summary>
        /// Gets or sets whether to repeat G89 before each feature.
        /// Default: true
        /// </summary>
        public bool RepeatG89BeforeEachFeature { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to use exact stop mode (G61).
        /// Default: false
        /// </summary>
        public bool UseExactStopMode { get; set; } = false;

        /// <summary>
        /// Gets or sets where kerf compensation is applied.
        /// Default: KerfMode.ControllerSide
        /// </summary>
        public KerfMode KerfCompensation { get; set; } = KerfMode.ControllerSide;

        /// <summary>
        /// Gets or sets the default side for kerf compensation.
        /// Default: KerfSide.Left
        /// </summary>
        public KerfSide DefaultKerfSide { get; set; } = KerfSide.Left;

        /// <summary>
        /// Gets or sets how M47 is used in interior cuts.
        /// Default: M47Mode.Always
        /// </summary>
        public M47Mode InteriorM47 { get; set; } = M47Mode.Always;

        /// <summary>
        /// Gets or sets how M47 is used in exterior cuts.
        /// Default: M47Mode.Always
        /// </summary>
        public M47Mode ExteriorM47 { get; set; } = M47Mode.Always;

        /// <summary>
        /// Gets or sets the safety head raise distance (in machine units).
        /// Default: 2000
        /// </summary>
        public int? SafetyHeadraiseDistance { get; set; } = 2000;

        /// <summary>
        /// Gets or sets the distance threshold for M47 override.
        /// Default: null
        /// </summary>
        public double? M47OverrideDistanceThreshold { get; set; } = null;

        /// <summary>
        /// Gets or sets whether to use anti-dive functionality.
        /// Default: true
        /// </summary>
        public bool UseAntiDive { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to use smart rapids optimization.
        /// Default: false
        /// </summary>
        public bool UseSmartRapids { get; set; } = false;

        /// <summary>
        /// Gets or sets when pallet exchange occurs.
        /// Default: PalletMode.EndOfSheet
        /// </summary>
        public PalletMode PalletExchange { get; set; } = PalletMode.EndOfSheet;

        /// <summary>
        /// Gets or sets whether to use line numbers in output.
        /// Default: true
        /// </summary>
        public bool UseLineNumbers { get; set; } = true;

        /// <summary>
        /// Gets or sets the starting line number for features.
        /// Default: 1
        /// </summary>
        public int FeatureLineNumberStart { get; set; } = 1;

        /// <summary>
        /// Gets or sets whether to use speed/gas commands.
        /// Default: false
        /// </summary>
        public bool UseSpeedGas { get; set; } = false;

        /// <summary>
        /// Gets or sets the feedrate percentage for lead-in moves.
        /// Default: 0.5 (50%)
        /// </summary>
        public double LeadInFeedratePercent { get; set; } = 0.5;

        /// <summary>
        /// Gets or sets the feedrate percentage for lead-in arc-to-line moves.
        /// Default: 0.5 (50%)
        /// </summary>
        public double LeadInArcLine2FeedratePercent { get; set; } = 0.5;

        /// <summary>
        /// Gets or sets the feedrate multiplier for circular cuts.
        /// Default: 0.8 (80%)
        /// </summary>
        public double CircleFeedrateMultiplier { get; set; } = 0.8;

        /// <summary>
        /// Gets or sets the variable number for sheet width.
        /// Default: 110
        /// </summary>
        public int SheetWidthVariable { get; set; } = 110;

        /// <summary>
        /// Gets or sets the variable number for sheet length.
        /// Default: 111
        /// </summary>
        public int SheetLengthVariable { get; set; } = 111;
    }
}
