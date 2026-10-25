using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using OpenNest.PostSettings;

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
    [PostSettingsSection(
        OutputSection,
        0,
        Description = "Which configuration the program names, its units and number format, and line numbering."
    )]
    [PostSettingsSection(
        SheetsSection,
        1,
        Description = "When the pallet exchange is written around each sheet."
    )]
    [PostSettingsSection(
        LibrariesSection,
        2,
        Description = "The G89 cut and etch libraries the program loads. They must exist on the machine."
    )]
    [PostSettingsSection(
        ProcessSection,
        3,
        Description = "Assist gas defaults and the controller modes the program switches on."
    )]
    [PostSettingsSection(
        KerfSection,
        4,
        Description = "Where kerf compensation is applied and on which side of the cut."
    )]
    [PostSettingsSection(
        HeadRaiseSection,
        5,
        Description = "How the head is raised (M47) before each rapid, and the safety raise for long rapids over warped plate."
    )]
    [PostSettingsSection(
        FeedratesSection,
        6,
        Description = "Feedrates for lead-ins, lead-outs, circles and small arcs, as fractions of the library feedrate (0.5 = 50%)."
    )]
    [PostSettingsSection(
        SubprogramsSection,
        7,
        Description = "Sub-program numbers for sheets, variable declarations and repeated parts."
    )]
    [PostSettingsSection(
        VariablesSection,
        8,
        Description = "Machine variable numbers the program declares and reads."
    )]
    public sealed class CincinnatiPostConfig
    {
        private const string OutputSection = "Program output";
        private const string SheetsSection = "Sheets";
        private const string LibrariesSection = "Libraries";
        private const string ProcessSection = "Process";
        private const string KerfSection = "Kerf";
        private const string HeadRaiseSection = "Head raise";
        private const string FeedratesSection = "Feedrates";
        private const string SubprogramsSection = "Sub-programs";
        private const string VariablesSection = "Variables";

        [DisplayName("Configuration name")]
        [Description("Configuration name/identifier written to the program (e.g. CL940).")]
        [PostSetting(OutputSection, 0)]
        public string ConfigurationName { get; set; } = "CL940";

        [DisplayName("Posted units")]
        [Description("Units for posted output (Inches or Millimeters).")]
        [PostSetting(OutputSection, 1)]
        public Units PostedUnits { get; set; } = Units.Inches;

        [DisplayName("Posted accuracy")]
        [Description("Number of decimal places for numeric output.")]
        [PostSetting(OutputSection, 2, Minimum = 0, Maximum = 6)]
        public int PostedAccuracy { get; set; } = 4;

        [DisplayName("Use line numbers")]
        [Description("Number the program's feature blocks (N numbers).")]
        [PostSetting(OutputSection, 3)]
        public bool UseLineNumbers { get; set; } = true;

        [DisplayName("Feature line number start")]
        [Description("Starting line number for features.")]
        [PostSetting(OutputSection, 4, Minimum = 0, Maximum = 99999)]
        public int FeatureLineNumberStart { get; set; } = 1;

        [DisplayName("Sheet sub-program start")]
        [Description("Starting sub-program number for sheet operations.")]
        [PostSetting(SubprogramsSection, 0, Minimum = 0, Maximum = 99999)]
        public int SheetSubprogramStart { get; set; } = 101;

        [DisplayName("Use part sub-programs")]
        [Description(
            "Use M98 sub-programs for part geometry. Reduces output size for repeated parts."
        )]
        [PostSetting(SubprogramsSection, 2)]
        public bool UsePartSubprograms { get; set; } = false;

        [DisplayName("Part sub-program start")]
        [Description("Starting sub-program number for part geometry sub-programs.")]
        [PostSetting(SubprogramsSection, 3, Minimum = 0, Maximum = 99999)]
        public int PartSubprogramStart { get; set; } = 200;

        [DisplayName("Variable declaration sub-program")]
        [Description("Sub-program number for variable declarations.")]
        [PostSetting(SubprogramsSection, 1, Minimum = 0, Maximum = 99999)]
        public int VariableDeclarationSubprogram { get; set; } = 100;

        [DisplayName("Default assist gas")]
        [Description("Assist gas used to pick the cut library when the nest names none.")]
        [PostSetting(ProcessSection, 0)]
        public string DefaultAssistGas { get; set; } = "O2";

        [DisplayName("Default etch gas")]
        [Description("Gas used for etch operations; picks the etch library.")]
        [PostSetting(ProcessSection, 1)]
        public string DefaultEtchGas { get; set; } = "N2";

        [DisplayName("Use exact stop mode")]
        [Description("Enable exact stop mode (G61).")]
        [PostSetting(ProcessSection, 2)]
        public bool UseExactStopMode { get; set; } = false;

        [DisplayName("Use speed/gas commands")]
        [Description("Enable speed/gas commands in output.")]
        [PostSetting(ProcessSection, 3)]
        public bool UseSpeedGas { get; set; } = false;

        [DisplayName("Use anti-dive")]
        [Description("Enable anti-dive functionality.")]
        [PostSetting(ProcessSection, 4)]
        public bool UseAntiDive { get; set; } = true;

        [DisplayName("Use smart rapids")]
        [Description("Enable smart rapids optimization.")]
        [PostSetting(ProcessSection, 5)]
        public bool UseSmartRapids { get; set; } = false;

        [DisplayName("Kerf compensation")]
        [Description("ControllerSide posts cutter compensation codes; PreApplied offsets the part geometry while posting.")]
        [PostSetting(KerfSection, 0)]
        public KerfMode KerfCompensation { get; set; } = KerfMode.ControllerSide;

        [DisplayName("Default kerf side")]
        [Description("Default side for kerf compensation (Left or Right).")]
        [PostSetting(KerfSection, 1)]
        public KerfSide DefaultKerfSide { get; set; } = KerfSide.Left;

        [DisplayName("Interior M47")]
        [Description("How M47 is posted after features other than a part's exterior, such as cutouts and etch.")]
        [PostSetting(HeadRaiseSection, 0)]
        public M47Mode InteriorM47 { get; set; } = M47Mode.Always;

        [DisplayName("Exterior M47")]
        [Description(
            "How M47 is posted after a part's exterior: the cut contour spanning the part's cut extents, or a part sub-program call."
        )]
        [PostSetting(HeadRaiseSection, 1)]
        public M47Mode ExteriorM47 { get; set; } = M47Mode.Always;

        [DisplayName("M47 override distance")]
        [Description(
            "Auto M47 mode: rapids to the next pierce longer than this get M47, shorter ones /M47. Program units. Clear the box and Auto always posts M47."
        )]
        [PostSetting(HeadRaiseSection, 2, Minimum = 0, Maximum = 10000, DecimalPlaces = 3)]
        public double? M47OverrideDistanceThreshold { get; set; } = null;

        [DisplayName("Safety head raise distance")]
        [Description(
            "Safety head raise distance in machine units, posted as M47 P<distance> before rapids longer than the safety head raise rapid distance. Clear the box to disable."
        )]
        [PostSetting(HeadRaiseSection, 3, Minimum = 0, Maximum = 100000)]
        public int? SafetyHeadraiseDistance { get; set; } = 2000;

        [DisplayName("Safety head raise rapid distance")]
        [Description(
            "Rapids to the next pierce longer than this get the safety head raise instead of the M47 mode's raise, so the head clears warped plate. Program units. Clear the box to disable."
        )]
        [PostSetting(HeadRaiseSection, 4, Minimum = 0, Maximum = 10000, DecimalPlaces = 3)]
        public double? SafetyHeadraiseRapidDistance { get; set; } = 12.0;

        [DisplayName("Head height lock")]
        [Description(
            "Written before a cut-off overcuts past the far sheet edge. Blank = not set: a nest with head-locked cut-offs is refused."
        )]
        [PostSetting(HeadRaiseSection, 5)]
        public string HeadHeightLockCode { get; set; } = "";

        [DisplayName("Head height unlock")]
        [Description(
            "Written after a cut-off's overcut, back at the nominal sheet edge. Blank = not set: a nest with head-locked cut-offs is refused."
        )]
        [PostSetting(HeadRaiseSection, 6)]
        public string HeadHeightUnlockCode { get; set; } = "";

        /// <summary>Whether both head-height lock blocks are set.</summary>
        public bool HasHeadHeightLockCodes() =>
            !string.IsNullOrWhiteSpace(HeadHeightLockCode) && !string.IsNullOrWhiteSpace(HeadHeightUnlockCode);

        [DisplayName("Pallet exchange")]
        [Description("When the pallet exchange is written: None, EndOfSheet, or StartAndEnd.")]
        [PostSetting(SheetsSection, 0)]
        public PalletMode PalletExchange { get; set; } = PalletMode.EndOfSheet;

        [DisplayName("Lead-in feedrate")]
        [Description("Feedrate for lead-in moves (0.5 = 50%).")]
        [PostSetting(FeedratesSection, 0, Minimum = 0, Maximum = 10, DecimalPlaces = 3)]
        public double LeadInFeedratePercent { get; set; } = 0.5;

        [DisplayName("Lead-in arc feedrate")]
        [Description("Feedrate for lead-in arc moves (0.5 = 50%).")]
        [PostSetting(FeedratesSection, 1, Minimum = 0, Maximum = 10, DecimalPlaces = 3)]
        public double LeadInArcLine2FeedratePercent { get; set; } = 0.5;

        [DisplayName("Lead-out feedrate")]
        [Description("Feedrate for lead-out moves (0.5 = 50%).")]
        [PostSetting(FeedratesSection, 2, Minimum = 0, Maximum = 10, DecimalPlaces = 3)]
        public double LeadOutFeedratePercent { get; set; } = 0.5;

        [DisplayName("Circle feedrate multiplier")]
        [Description("Feedrate multiplier for circular cuts (0.8 = 80%).")]
        [PostSetting(FeedratesSection, 3, Minimum = 0, Maximum = 10, DecimalPlaces = 3)]
        public double CircleFeedrateMultiplier { get; set; } = 0.8;

        [DisplayName("Arc feedrate mode")]
        [Description(
            "None slows only full circles. Percentages posts F [#148*fraction] and Variables posts F #variable for arcs, by the radius ranges below."
        )]
        [PostSetting(FeedratesSection, 4)]
        public ArcFeedrateMode ArcFeedrate { get; set; } = ArcFeedrateMode.None;

        [DisplayName("Arc feedrate ranges")]
        [Description(
            "An arc takes the first range, from smallest to largest maximum radius, that its radius fits. Feedrate is a fraction (0.25 = 25%); the variable is used in Variables mode."
        )]
        [PostSetting(FeedratesSection, 5)]
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

        [DisplayName("User variable start")]
        [Description("Starting variable number for user-defined variables (#200, #201, etc.).")]
        [PostSetting(VariablesSection, 0, Minimum = 0, Maximum = 99999)]
        public int UserVariableStart { get; set; } = 200;

        [DisplayName("Sheet width variable")]
        [Description("Variable number for sheet width.")]
        [PostSetting(VariablesSection, 1, Minimum = 0, Maximum = 99999)]
        public int SheetWidthVariable { get; set; } = 110;

        [DisplayName("Sheet length variable")]
        [Description("Variable number for sheet length.")]
        [PostSetting(VariablesSection, 2, Minimum = 0, Maximum = 99999)]
        public int SheetLengthVariable { get; set; } = 111;

        [DisplayName("Material libraries")]
        [Description(
            "Cut library (G89 file) for each material, thickness and assist gas. The first row matching the nest's material, thickness within 0.001 and gas is posted."
        )]
        [PostSetting(LibrariesSection, 1)]
        public List<MaterialLibraryEntry> MaterialLibraries { get; set; } = new();

        [DisplayName("Etch libraries")]
        [Description("Etch library (G89 file) for each etch gas.")]
        [PostSetting(LibrariesSection, 2)]
        public List<EtchLibraryEntry> EtchLibraries { get; set; } = new();

        [DisplayName("Selected library")]
        [Description(
            "The cut library this nest posts with: pre-selected from its material, thickness and gas. Pick another to override, or leave blank to resolve automatically."
        )]
        [PostSetting(
            LibrariesSection,
            0,
            SuggestionsTable = nameof(MaterialLibraries),
            SuggestionsColumn = nameof(MaterialLibraryEntry.Library)
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
        [DisplayName("Max radius")]
        public double MaxRadius { get; set; }

        /// <summary>Feedrate as a fraction of process feedrate (e.g. 0.25 = 25%).</summary>
        [DisplayName("Feedrate")]
        public double FeedratePercent { get; set; }

        /// <summary>Variable number for Variables mode (e.g. 123).</summary>
        [DisplayName("Variable")]
        public int VariableNumber { get; set; }
    }
}
