using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace OpenNest.Posts.CincinnatiCIFiber
{
    /// <summary>
    /// Configuration for the Cincinnati CI Fiber (TF5200 / nLight CLX) post-processor.
    /// All layer macro names, header variable names and the material code map are
    /// editable so the post can serve other CI Fiber tables, not only the 4020.
    /// </summary>
    public class CIFiberPostConfig
    {
        /// <summary>Emitted in the "( CONFIGURATION - ... )" header comment.</summary>
        [DisplayName("Configuration name")]
        public string ConfigurationName { get; set; } = "CI FIBER 8K";

        /// <summary>
        /// Decimal places for posted coordinates and header values. The sample
        /// machine program posts 3 decimals for inch units.
        /// </summary>
        [DisplayName("Posted accuracy")]
        public int PostedAccuracy { get; set; } = 3;

        /// <summary>Value for V.E.UNIT when the nest is in inches.</summary>
        [DisplayName("Unit code (inches)")]
        public int InchUnitCode { get; set; } = 1;

        /// <summary>
        /// Value for V.E.UNIT when the nest is in millimeters. The TF5200
        /// metric unit code is unconfirmed; adjust if the controller rejects 0.
        /// </summary>
        [DisplayName("Unit code (millimeters)")]
        public int MetricUnitCode { get; set; } = 0;

        /// <summary>Emit the V.E.SHEET_WEIGHT header variable (value 0).</summary>
        [DisplayName("Emit sheet weight")]
        public bool EmitSheetWeight { get; set; } = false;

        /// <summary>Skip contours whose moves are all LayerType.Scribe (marks).</summary>
        [DisplayName("Skip scribe layer")]
        public bool SkipScribe { get; set; } = true;

        /// <summary>Maximum plate length (X) this configuration drives; 0 disables the check.</summary>
        [DisplayName("Maximum table X")]
        public double MaxTableX { get; set; } = 160.25;

        /// <summary>Maximum plate width (Y) this configuration drives; 0 disables the check.</summary>
        [DisplayName("Maximum table Y")]
        public double MaxTableY { get; set; } = 81.25;

        /// <summary>Comment text after the "( PART:" prefix for each part.</summary>
        [DisplayName("Part comment")]
        public string PartComment { get; set; } = "";

        /// <summary>Skippable global subroutine used to cancel comp / park between features.</summary>
        [DisplayName("Layer: cancel (L0)")]
        public string LayerCancel { get; set; } = "L0";

        /// <summary>Skippable global subroutine for interior lead-in plus G41.</summary>
        [DisplayName("Layer: interior leadin (L2)")]
        public string LayerInteriorLeadin { get; set; } = "L2";

        /// <summary>Skippable global subroutine for exterior lead-in plus G42.</summary>
        [DisplayName("Layer: exterior leadin (L4)")]
        public string LayerExteriorLeadin { get; set; } = "L4";

        /// <summary>Skippable global subroutine that switches the cut layer on.</summary>
        [DisplayName("Layer: cut on (L6)")]
        public string LayerCut { get; set; } = "L6";

        /// <summary>Skippable global subroutine at the end of each contour (head separation / comp off).</summary>
        [DisplayName("Layer: cut end (ZHSOFF)")]
        public string LayerCutEnd { get; set; } = "ZHSOFF";

        /// <summary>External program called once at program start.</summary>
        [DisplayName("Start macro")]
        public string ProgramStartMacro { get; set; } = "PROGRAMSTART.NC";

        /// <summary>External program called after the last sheet.</summary>
        [DisplayName("End macro")]
        public string ProgramEndMacro { get; set; } = "PROGRAMEND.NC";

        /// <summary>Material name (case-insensitive) to machine material code map for V.E.MATERIAL.</summary>
        [DisplayName("Material codes")]
        public Dictionary<string, string> MaterialCodes { get; set; } =
            new(StringComparer.OrdinalIgnoreCase) { ["Mild Steel"] = "MSN" };

        /// <summary>Fallback V.E.MATERIAL code when the material name has no mapping.</summary>
        [DisplayName("Default material code")]
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
