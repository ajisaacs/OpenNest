using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace OpenNest.Posts.Cincinnati
{
    /// <summary>
    /// Specifies where kerf compensation is applied.
    /// </summary>
    public enum KerfMode
    {
        /// <summary>Controller side (using cutter compensation codes).</summary>
        ControllerSide,

        /// <summary>Pre-applied to part geometry during post-processing.</summary>
        PreApplied,
    }

    /// <summary>
    /// Specifies which side of the cut line kerf compensation is applied to.
    /// </summary>
    public enum KerfSide
    {
        /// <summary>Kerf applied to the left side of the cut.</summary>
        Left,

        /// <summary>Kerf applied to the right side of the cut.</summary>
        Right,
    }

    /// <summary>
    /// Specifies how the M47 head raise before each rapid is posted.
    /// </summary>
    public enum M47Mode
    {
        /// <summary>Always include M47.</summary>
        Always,

        /// <summary>Post /M47, which the operator can skip with block delete.</summary>
        BlockDelete,

        /// <summary>
        /// M47 before rapids longer than the M47 override distance threshold, /M47 before
        /// shorter ones. Without a threshold every raise is M47.
        /// </summary>
        Auto,

        /// <summary>Do not use M47.</summary>
        None,
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
        StartAndEnd,
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
        [DisplayName("Sheet Subprogram Start")]
        [Description("Starting subprogram number for sheet operations.")]
        public int SheetSubprogramStart { get; set; } = 101;

        [Category("2. Subprograms")]
        [DisplayName("Use Part Subprograms")]
        [Description(
            "Use M98 sub-programs for part geometry. Reduces output size for repeated parts."
        )]
        public bool UsePartSubprograms { get; set; } = false;

        [Category("2. Subprograms")]
        [DisplayName("Part Subprogram Start")]
        [Description("Starting sub-program number for part geometry sub-programs.")]
        public int PartSubprogramStart { get; set; } = 200;

        [Category("2. Subprograms")]
        [DisplayName("Variable Declaration Subprogram")]
        [Description("Subprogram number for variable declarations.")]
        public int VariableDeclarationSubprogram { get; set; } = 100;

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
        [Description("How M47 is posted after features other than a part's exterior, such as cutouts and etch.")]
        public M47Mode InteriorM47 { get; set; } = M47Mode.Always;

        [Category("6. M47 (Optional Stop)")]
        [DisplayName("Exterior M47")]
        [Description(
            "How M47 is posted after a part's exterior: the cut contour spanning the part's cut extents, or a part sub-program call."
        )]
        public M47Mode ExteriorM47 { get; set; } = M47Mode.Always;

        [Category("6. M47 (Optional Stop)")]
        [DisplayName("M47 Override Distance Threshold")]
        [Description(
            "Auto M47 mode: rapids to the next pierce longer than this get M47, shorter ones /M47. Program units. Null = Auto always posts M47."
        )]
        public double? M47OverrideDistanceThreshold { get; set; } = null;

        [Category("7. Safety")]
        [DisplayName("Safety Headraise Distance")]
        [Description(
            "Safety head raise distance in machine units, posted as M47 P<distance> before rapids longer than the safety headraise rapid distance. Null = disabled."
        )]
        public int? SafetyHeadraiseDistance { get; set; } = 2000;

        [Category("7. Safety")]
        [DisplayName("Safety Headraise Rapid Distance")]
        [Description(
            "Rapids to the next pierce longer than this get the safety head raise instead of the M47 mode's raise, so the head clears warped plate. Program units. Null = disabled."
        )]
        public double? SafetyHeadraiseRapidDistance { get; set; } = 12.0;

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
        [Description(
            "Radius-based arc feedrate ranges. Matched from smallest to largest MaxRadius."
        )]
        public List<ArcFeedrateRange> ArcFeedrateRanges { get; set; } =
            new()
            {
                new()
                {
                    MaxRadius = 0.125,
                    FeedratePercent = 0.25,
                    VariableNumber = 123,
                },
                new()
                {
                    MaxRadius = 0.750,
                    FeedratePercent = 0.50,
                    VariableNumber = 124,
                },
                new()
                {
                    MaxRadius = 4.500,
                    FeedratePercent = 0.80,
                    VariableNumber = 125,
                },
            };

        [Category("A. Variables")]
        [DisplayName("User Variable Start")]
        [Description("Starting variable number for user-defined variables (#200, #201, etc.).")]
        public int UserVariableStart { get; set; } = 200;

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
        [Description(
            "Material-to-library mapping for cut operations. Maps (material, thickness, gas) to a G89 library file."
        )]
        public List<MaterialLibraryEntry> MaterialLibraries { get; set; } = new();

        [Category("B. Libraries")]
        [DisplayName("Etch Libraries")]
        [Description("Gas-to-library mapping for etch operations.")]
        public List<EtchLibraryEntry> EtchLibraries { get; set; } = new();

        [Category("B. Libraries")]
        [DisplayName("Selected Library")]
        [Description(
            "Overrides Material/Thickness/Gas auto-resolution. Pick an existing entry from Material Libraries, or leave blank to auto-resolve."
        )]
        [TypeConverter(typeof(MaterialLibraryNameConverter))]
        public string SelectedLibrary { get; set; } = "";
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
        Variables,
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
