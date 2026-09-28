using System;
using System.Collections.Generic;
using System.ComponentModel;
using OpenNest.PostSettings;

namespace OpenNest.Posts.CincinnatiCIFiber
{
    /// <summary>
    /// Configuration for the Cincinnati CI Fiber (TF5200 / nLight CLX) post-processor.
    /// All layer macro names, header variable names and the material code map are
    /// editable so the post can serve other CI Fiber tables, not only the 4020.
    /// </summary>
    [PostSettingsSection(
        MachineSection,
        0,
        Description = "Which CI Fiber table this configuration drives and the largest sheet it accepts."
    )]
    [PostSettingsSection(
        SheetsSection,
        1,
        Description = "How a nest with more than one sheet is written, and the code that swaps pallets."
    )]
    [PostSettingsSection(
        MaterialSection,
        2,
        Description = "Maps OpenNest material names to the controller's V.E.MATERIAL code."
    )]
    [PostSettingsSection(
        OutputSection,
        3,
        Description = "Number format, units, and what the posted program includes."
    )]
    [PostSettingsSection(
        MacrosSection,
        4,
        Description = "Controller subroutines called around each contour and at program start and end. They must exist on the machine."
    )]
    public class CIFiberPostConfig
    {
        private const string MachineSection = "Machine";
        private const string SheetsSection = "Sheets";
        private const string MaterialSection = "Material";
        private const string OutputSection = "Program output";
        private const string MacrosSection = "Macros";

        /// <summary>Emitted in the "( CONFIGURATION - ... )" header comment.</summary>
        [DisplayName("Configuration name")]
        [Description("Shown in the program header comment ( CONFIGURATION - ... ).")]
        [PostSetting(MachineSection, 0)]
        public string ConfigurationName { get; set; } = "CI FIBER 8K";

        /// <summary>
        /// Decimal places for posted coordinates and header values. The sample
        /// machine program posts 3 decimals for inch units.
        /// </summary>
        [DisplayName("Posted accuracy")]
        [Description("Decimal places for coordinates and header values. The machine sample uses 3 for inches.")]
        [PostSetting(OutputSection, 0, Minimum = 0, Maximum = 6)]
        public int PostedAccuracy { get; set; } = 3;

        /// <summary>Value for V.E.UNIT when the nest is in inches.</summary>
        [DisplayName("Unit code (inches)")]
        [Description("V.E.UNIT value written for inch nests.")]
        [PostSetting(OutputSection, 1, Minimum = 0, Maximum = 99)]
        public int InchUnitCode { get; set; } = 1;

        /// <summary>
        /// Value for V.E.UNIT when the nest is in millimeters. The TF5200
        /// metric unit code is unconfirmed; adjust if the controller rejects 0.
        /// </summary>
        [DisplayName("Unit code (millimeters)")]
        [Description("V.E.UNIT value written for millimeter nests. Unconfirmed on the controller.")]
        [PostSetting(OutputSection, 2, Minimum = 0, Maximum = 99)]
        public int MetricUnitCode { get; set; } = 0;

        /// <summary>Emit the V.E.SHEET_WEIGHT header variable (value 0).</summary>
        [DisplayName("Emit sheet weight")]
        [Description("Write the V.E.SHEET_WEIGHT header variable (value 0).")]
        [PostSetting(OutputSection, 3)]
        public bool EmitSheetWeight { get; set; } = false;

        /// <summary>Skip contours whose moves are all LayerType.Scribe (marks).</summary>
        [DisplayName("Skip scribe layer")]
        [Description("Leave out contours that are entirely scribe/etch marks.")]
        [PostSetting(OutputSection, 4)]
        public bool SkipScribe { get; set; } = true;

        /// <summary>Maximum plate length (X) this configuration drives; 0 disables the check.</summary>
        [DisplayName("Maximum table X")]
        [Description("Largest plate length (X) this table accepts. Posting a larger plate fails. 0 disables the check.")]
        [PostSetting(MachineSection, 1, Minimum = 0, Maximum = 10000, DecimalPlaces = 3)]
        public double MaxTableX { get; set; } = 160.25;

        /// <summary>Maximum plate width (Y) this configuration drives; 0 disables the check.</summary>
        [DisplayName("Maximum table Y")]
        [Description("Largest plate width (Y) this table accepts. Posting a larger plate fails. 0 disables the check.")]
        [PostSetting(MachineSection, 2, Minimum = 0, Maximum = 10000, DecimalPlaces = 3)]
        public double MaxTableY { get; set; } = 81.25;

        /// <summary>
        /// Write each sheet as its own program (NAME-1.cnc, NAME-2.cnc, ...), the
        /// Cincinnati convention for batch runs. When false, every sheet goes into
        /// one program with a pallet change between sheets. A nest with a single
        /// sheet always posts to the chosen file name.
        /// </summary>
        [DisplayName("One program per sheet")]
        [Description(
            "Save each sheet as its own program: JOB.cnc becomes JOB-1.cnc, JOB-2.cnc, ... "
                + "Clear to put every sheet in one program, with a pallet change between sheets. "
                + "A single-sheet nest always saves to the chosen name."
        )]
        [PostSetting(SheetsSection, 0)]
        public bool OneProgramPerSheet { get; set; } = true;

        /// <summary>
        /// Line written to swap pallets: after the last sheet, and between sheets
        /// in a single program. M50 comes from the machine sample and the CL-series
        /// manual (EM-423 §3.50); it is unconfirmed for multi-sheet CI Fiber runs.
        /// Blank writes no pallet change.
        /// </summary>
        [DisplayName("Pallet change code")]
        [Description(
            "Written after each sheet to swap pallets. M50 matches the machine sample; "
                + "confirm it for multi-sheet runs. Blank writes no pallet change."
        )]
        [PostSetting(SheetsSection, 1)]
        public string PalletChangeCode { get; set; } = "M50";

        /// <summary>Skippable global subroutine used to cancel comp / park between features.</summary>
        [DisplayName("Layer: cancel (L0)")]
        [Description("Called before each contour and at the end to cancel compensation and park.")]
        [PostSetting(MacrosSection, 0)]
        public string LayerCancel { get; set; } = "L0";

        /// <summary>Skippable global subroutine for interior lead-in plus G41.</summary>
        [DisplayName("Layer: interior leadin (L2)")]
        [Description("Called before an interior (hole) lead-in, with G41.")]
        [PostSetting(MacrosSection, 1)]
        public string LayerInteriorLeadin { get; set; } = "L2";

        /// <summary>Skippable global subroutine for exterior lead-in plus G42.</summary>
        [DisplayName("Layer: exterior leadin (L4)")]
        [Description("Called before an exterior (perimeter) lead-in, with G42.")]
        [PostSetting(MacrosSection, 2)]
        public string LayerExteriorLeadin { get; set; } = "L4";

        /// <summary>Skippable global subroutine that switches the cut layer on.</summary>
        [DisplayName("Layer: cut on (L6)")]
        [Description("Switches the cut layer on after the lead-in.")]
        [PostSetting(MacrosSection, 3)]
        public string LayerCut { get; set; } = "L6";

        /// <summary>Skippable global subroutine at the end of each contour (head separation / comp off).</summary>
        [DisplayName("Layer: cut end (ZHSOFF)")]
        [Description("Called at the end of each contour (head separation).")]
        [PostSetting(MacrosSection, 4)]
        public string LayerCutEnd { get; set; } = "ZHSOFF";

        /// <summary>External program called once at program start.</summary>
        [DisplayName("Start macro")]
        [Description("Program called once at the start (L name).")]
        [PostSetting(MacrosSection, 5)]
        public string ProgramStartMacro { get; set; } = "PROGRAMSTART.NC";

        /// <summary>External program called after the last sheet.</summary>
        [DisplayName("End macro")]
        [Description("Program called after the last sheet (L name).")]
        [PostSetting(MacrosSection, 6)]
        public string ProgramEndMacro { get; set; } = "PROGRAMEND.NC";

        /// <summary>
        /// Material name (case-insensitive) to machine material code map for
        /// V.E.MATERIAL. The setter re-keys any assigned map (including one
        /// deserialized from JSON, which is case-sensitive) case-insensitively;
        /// if names collide by case, the later entry wins.
        /// </summary>
        [DisplayName("Material codes")]
        [Description("Material name (not case-sensitive) and the code written to V.E.MATERIAL.")]
        [PostSetting(MaterialSection, 0, KeyHeader = "Material name", ValueHeader = "Machine code")]
        public Dictionary<string, string> MaterialCodes
        {
            get => _materialCodes;
            set => _materialCodes = IgnoreCase(value);
        }

        private Dictionary<string, string> _materialCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Mild Steel"] = "MSN",
        };

        private static Dictionary<string, string> IgnoreCase(Dictionary<string, string> map)
        {
            if (map == null || map.Comparer == StringComparer.OrdinalIgnoreCase)
                return map;

            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in map)
                copy[entry.Key] = entry.Value;
            return copy;
        }

        /// <summary>Fallback V.E.MATERIAL code when the material name has no mapping.</summary>
        [DisplayName("Default material code")]
        [Description("Code used when the nest material has no entry in the table above.")]
        [PostSetting(MaterialSection, 1)]
        public string DefaultMaterialCode { get; set; } = "MSN";

        public string ResolveMaterialCode(string materialName)
        {
            if (!string.IsNullOrWhiteSpace(materialName)
                && MaterialCodes != null
                && MaterialCodes.TryGetValue(materialName.Trim(), out var code)
                && !string.IsNullOrWhiteSpace(code))
                return code;

            return DefaultMaterialCode ?? "MSN";
        }

        /// <summary>
        /// Throws if the plate exceeds the configured table envelope.
        /// Zero limits disable the check.
        /// </summary>
        public void ValidateTableSize(double lengthX, double widthY)
        {
            if (MaxTableX > 0 && lengthX > MaxTableX + ToleranceEpsilon)
                throw new InvalidOperationException(
                    $"Plate length {lengthX:0.###} exceeds maximum table X {MaxTableX:0.###}."
                );

            if (MaxTableY > 0 && widthY > MaxTableY + ToleranceEpsilon)
                throw new InvalidOperationException(
                    $"Plate width {widthY:0.###} exceeds maximum table Y {MaxTableY:0.###}."
                );
        }

        private const double ToleranceEpsilon = 1e-6;
    }
}
