using System.Collections.Generic;
using System.ComponentModel;

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
        [Category("1. Output")]
        [DisplayName("Configuration Name")]
        [Description("Configuration name/identifier (e.g. CL940).")]
        public string ConfigurationName { get; set; } = "CL940";

        [Category("1. Output")]
        [DisplayName("Posted Units")]
        [Description("Units for posted output (Inches or Millimeters).")]
        public Units PostedUnits { get; set; } = Units.Inches;

        [Category("1. Output")]
        [DisplayName("Decimal Accuracy")]
        [Description("Number of decimal places for numeric output.")]
        public int PostedAccuracy { get; set; } = 4;

        [Category("1. Output")]
        [DisplayName("Use Line Numbers")]
        [Description("Include line numbers in output.")]
        public bool UseLineNumbers { get; set; } = true;

        [Category("1. Output")]
        [DisplayName("Feature Line Number Start")]
        [Description("Starting line number for features.")]
        public int FeatureLineNumberStart { get; set; } = 1;

        [Category("2. Subprograms")]
        [DisplayName("Use Sheet Subprograms")]
        [Description("Use subprograms for sheet operations.")]
        public bool UseSheetSubprograms { get; set; } = true;

        [Category("2. Subprograms")]
        [DisplayName("Sheet Subprogram Start")]
        [Description("Starting subprogram number for sheet operations.")]
        public int SheetSubprogramStart { get; set; } = 101;

        [Category("2. Subprograms")]
        [DisplayName("Use Part Subprograms")]
        [Description("Use M98 sub-programs for part geometry. Reduces output size for repeated parts.")]
        public bool UsePartSubprograms { get; set; } = false;

        [Category("2. Subprograms")]
        [DisplayName("Part Subprogram Start")]
        [Description("Starting sub-program number for part geometry sub-programs.")]
        public int PartSubprogramStart { get; set; } = 200;

        [Category("2. Subprograms")]
        [DisplayName("Variable Declaration Subprogram")]
        [Description("Subprogram number for variable declarations.")]
        public int VariableDeclarationSubprogram { get; set; } = 100;

        [Category("3. Positioning")]
        [DisplayName("Coordinate Mode Between Parts")]
        [Description("How coordinate positioning is handled between parts (G92, G91, or G53).")]
        public CoordinateMode CoordModeBetweenParts { get; set; } = CoordinateMode.G92;

        [Category("4. Process")]
        [DisplayName("Process Parameter Mode")]
        [Description("How G89 parameters are provided (LibraryFile or Explicit).")]
        public G89Mode ProcessParameterMode { get; set; } = G89Mode.LibraryFile;

        [Category("4. Process")]
        [DisplayName("Default Assist Gas")]
        [Description("Default assist gas when Nest.AssistGas is empty.")]
        public string DefaultAssistGas { get; set; } = "O2";

        [Category("4. Process")]
        [DisplayName("Default Etch Gas")]
        [Description("Gas used for etch operations.")]
        public string DefaultEtchGas { get; set; } = "N2";

        [Category("4. Process")]
        [DisplayName("Use Exact Stop Mode")]
        [Description("Enable exact stop mode (G61).")]
        public bool UseExactStopMode { get; set; } = false;

        [Category("4. Process")]
        [DisplayName("Use Speed/Gas Commands")]
        [Description("Enable speed/gas commands in output.")]
        public bool UseSpeedGas { get; set; } = false;

        [Category("4. Process")]
        [DisplayName("Use Anti-Dive")]
        [Description("Enable anti-dive functionality.")]
        public bool UseAntiDive { get; set; } = true;

        [Category("4. Process")]
        [DisplayName("Use Smart Rapids")]
        [Description("Enable smart rapids optimization.")]
        public bool UseSmartRapids { get; set; } = false;

        [Category("5. Kerf")]
        [DisplayName("Kerf Compensation")]
        [Description("Where kerf compensation is applied (ControllerSide or PreApplied).")]
        public KerfMode KerfCompensation { get; set; } = KerfMode.ControllerSide;

        [Category("5. Kerf")]
        [DisplayName("Default Kerf Side")]
        [Description("Default side for kerf compensation (Left or Right).")]
        public KerfSide DefaultKerfSide { get; set; } = KerfSide.Left;

        [Category("6. M47 (Optional Stop)")]
        [DisplayName("Interior M47")]
        [Description("How M47 is used in interior cuts.")]
        public M47Mode InteriorM47 { get; set; } = M47Mode.Always;

        [Category("6. M47 (Optional Stop)")]
        [DisplayName("Exterior M47")]
        [Description("How M47 is used in exterior cuts.")]
        public M47Mode ExteriorM47 { get; set; } = M47Mode.Always;

        [Category("6. M47 (Optional Stop)")]
        [DisplayName("M47 Override Distance Threshold")]
        [Description("Distance threshold for M47 override. Null = no override.")]
        public double? M47OverrideDistanceThreshold { get; set; } = null;

        [Category("7. Safety")]
        [DisplayName("Safety Headraise Distance")]
        [Description("Safety head raise distance in machine units. Null = disabled.")]
        public int? SafetyHeadraiseDistance { get; set; } = 2000;

        [Category("8. Pallet")]
        [DisplayName("Pallet Exchange")]
        [Description("When pallet exchange occurs (None, EndOfSheet, or StartAndEnd).")]
        public PalletMode PalletExchange { get; set; } = PalletMode.EndOfSheet;

        [Category("9. Feedrates")]
        [DisplayName("Lead-In Feedrate %")]
        [Description("Feedrate percentage for lead-in moves (e.g. 0.5 = 50%).")]
        public double LeadInFeedratePercent { get; set; } = 0.5;

        [Category("9. Feedrates")]
        [DisplayName("Lead-In Arc Line 2 Feedrate %")]
        [Description("Feedrate percentage for lead-in arc-to-line moves.")]
        public double LeadInArcLine2FeedratePercent { get; set; } = 0.5;

        [Category("9. Feedrates")]
        [DisplayName("Lead-Out Feedrate %")]
        [Description("Feedrate percentage for lead-out moves.")]
        public double LeadOutFeedratePercent { get; set; } = 0.5;

        [Category("9. Feedrates")]
        [DisplayName("Circle Feedrate Multiplier")]
        [Description("Feedrate multiplier for circular cuts (e.g. 0.8 = 80%).")]
        public double CircleFeedrateMultiplier { get; set; } = 0.8;

        [Category("9. Feedrates")]
        [DisplayName("Arc Feedrate Mode")]
        [Description("Arc feedrate calculation mode (None, Percentages, or Variables).")]
        public ArcFeedrateMode ArcFeedrate { get; set; } = ArcFeedrateMode.None;

        [Category("9. Feedrates")]
        [DisplayName("Arc Feedrate Ranges")]
        [Description("Radius-based arc feedrate ranges. Matched from smallest to largest MaxRadius.")]
        public List<ArcFeedrateRange> ArcFeedrateRanges { get; set; } = new()
        {
            new() { MaxRadius = 0.125, FeedratePercent = 0.25, VariableNumber = 123 },
            new() { MaxRadius = 0.750, FeedratePercent = 0.50, VariableNumber = 124 },
            new() { MaxRadius = 4.500, FeedratePercent = 0.80, VariableNumber = 125 }
        };

        [Category("A. Variables")]
        [DisplayName("Sheet Width Variable")]
        [Description("Variable number for sheet width.")]
        public int SheetWidthVariable { get; set; } = 110;

        [Category("A. Variables")]
        [DisplayName("Sheet Length Variable")]
        [Description("Variable number for sheet length.")]
        public int SheetLengthVariable { get; set; } = 111;

        [Category("B. Libraries")]
        [DisplayName("Material Libraries")]
        [Description("Material-to-library mapping for cut operations. Maps (material, thickness, gas) to a G89 library file.")]
        public List<MaterialLibraryEntry> MaterialLibraries { get; set; } = new();

        [Category("B. Libraries")]
        [DisplayName("Etch Libraries")]
        [Description("Gas-to-library mapping for etch operations.")]
        public List<EtchLibraryEntry> EtchLibraries { get; set; } = new();
    }

    public class MaterialLibraryEntry
    {
        public string Material { get; set; } = "";
        public double Thickness { get; set; }
        public string Gas { get; set; } = "";
        public string Library { get; set; } = "";
    }

    public class EtchLibraryEntry
    {
        public string Gas { get; set; } = "";
        public string Library { get; set; } = "";
    }

    /// <summary>
    /// Specifies how arc feedrates are calculated based on radius.
    /// </summary>
    public enum ArcFeedrateMode
    {
        /// <summary>No radius-based arc feedrate adjustment (only full circles use multiplier).</summary>
        None,

        /// <summary>Inline percentage expressions: F [#148*pct] based on radius range.</summary>
        Percentages,

        /// <summary>Radius-range-based variables: F #varNum based on radius range.</summary>
        Variables
    }

    /// <summary>
    /// Defines a radius range and its associated feedrate for arc moves.
    /// </summary>
    public class ArcFeedrateRange
    {
        /// <summary>Maximum radius for this range (inclusive).</summary>
        public double MaxRadius { get; set; }

        /// <summary>Feedrate as a fraction of process feedrate (e.g. 0.25 = 25%).</summary>
        public double FeedratePercent { get; set; }

        /// <summary>Variable number for Variables mode (e.g. 123).</summary>
        public int VariableNumber { get; set; }
    }
}
