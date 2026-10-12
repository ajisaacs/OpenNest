using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNest.IO
{
    public static class NestFormat
    {
        public const string FileExtension = ".nest";
        public const string FileFilter = "Nest Files (*.nest)|*.nest";

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        public record NestDto
        {
            public int Version { get; init; } = 2;
            public string Name { get; init; } = "";
            public string Units { get; init; } = "Inches";
            public string Customer { get; init; } = "";
            public string DateCreated { get; init; } = "";
            public string DateLastModified { get; init; } = "";
            public string Notes { get; init; } = "";
            public string AssistGas { get; init; } = "";
            public string Status { get; init; } = "Quote";
            public string MadeBy { get; init; } = "";
            public double Thickness { get; init; }
            public MaterialDto Material { get; init; } = new();
            public PlateDefaultsDto PlateDefaults { get; init; } = new();
            public List<DrawingDto> Drawings { get; init; } = new();
            public List<PlateDto> Plates { get; init; } = new();
            public List<PlateOptionDto> PlateOptions { get; init; } = new();
            public double SalvageRate { get; init; } = 0.5;
        }

        public record PlateDefaultsDto
        {
            public SizeDto Size { get; init; } = new();
            public double Thickness { get; init; }
            public int Quadrant { get; init; } = 1;
            public double PartSpacing { get; init; }
            public MaterialDto Material { get; init; } = new();
            public SpacingDto EdgeSpacing { get; init; } = new();

            /// <summary>
            /// The nest's machine clamp rail, which new plates follow. Omitted when the nest
            /// has none; older files and older readers simply have no rail.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            [JsonConverter(typeof(LenientClampRailConverter))]
            public ClampRailDto ClampRail { get; init; }
        }

        /// <summary>
        /// Reads a malformed or wrongly typed clamp rail as no rail instead of failing the
        /// whole nest; the reader still validates the values (sign, size) it receives.
        /// </summary>
        public sealed class LenientClampRailConverter : JsonConverter<ClampRailDto>
        {
            public override ClampRailDto Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                using var document = JsonDocument.ParseValue(ref reader);
                var rail = document.RootElement;
                if (
                    rail.ValueKind != JsonValueKind.Object
                    || !TryNumber(rail, "width", out var width)
                    || !TryNumber(rail, "depth", out var depth)
                    || !rail.TryGetProperty("centers", out var centers)
                    || centers.ValueKind != JsonValueKind.Array
                )
                    return null;

                var list = new List<double>();
                foreach (var center in centers.EnumerateArray())
                {
                    if (center.ValueKind != JsonValueKind.Number || !center.TryGetDouble(out var x))
                        return null;
                    list.Add(x);
                }
                // Missing or unreadable -> null; the reader then falls back to the default.
                double? removalMinutes = TryNumber(rail, "removalMinutes", out var minutes)
                    ? minutes
                    : null;
                return new ClampRailDto { Width = width, Depth = depth, Centers = list, RemovalMinutes = removalMinutes };
            }

            public override void Write(
                Utf8JsonWriter writer,
                ClampRailDto value,
                JsonSerializerOptions options
            )
            {
                writer.WriteStartObject();
                writer.WriteNumber("width", value.Width);
                writer.WriteNumber("depth", value.Depth);
                writer.WriteStartArray("centers");
                foreach (var center in value.Centers ?? new List<double>())
                    writer.WriteNumberValue(center);
                writer.WriteEndArray();
                if (value.RemovalMinutes is double removalMinutes)
                    writer.WriteNumber("removalMinutes", removalMinutes);
                writer.WriteEndObject();
            }

            private static bool TryNumber(JsonElement rail, string name, out double value)
            {
                value = 0;
                return rail.TryGetProperty(name, out var element)
                    && element.ValueKind == JsonValueKind.Number
                    && element.TryGetDouble(out value);
            }
        }

        public record ClampRailDto
        {
            /// <summary>Clamp extent along the origin-side edge (X).</summary>
            public double Width { get; init; }

            /// <summary>Clamp intrusion into the sheet from that edge (Y).</summary>
            public double Depth { get; init; }

            /// <summary>Clamp centers along X, measured from the plate origin.</summary>
            public List<double> Centers { get; init; } = new();

            /// <summary>
            /// Minutes to remove one clamp and put it back after cutting (D9). Null in older
            /// files and readers fall back to <see cref="OpenNest.ClampRail.DefaultRemovalMinutes"/>.
            /// </summary>
            public double? RemovalMinutes { get; init; }
        }

        public record DrawingDto
        {
            public int Id { get; init; }
            public string Name { get; init; } = "";
            public string Customer { get; init; } = "";
            public ColorDto Color { get; init; } = new();
            public QuantityDto Quantity { get; init; } = new();
            public int Priority { get; init; }
            public ConstraintsDto Constraints { get; init; } = new();
            public MaterialDto Material { get; init; } = new();
            public SourceDto Source { get; init; } = new();
            public List<BendDto> Bends { get; init; } = new();
        }

        public record PlateDto
        {
            public int Id { get; init; }
            public SizeDto Size { get; init; } = new();
            public int Quadrant { get; init; } = 1;
            public int Quantity { get; init; } = 1;
            public double PartSpacing { get; init; }
            public SpacingDto EdgeSpacing { get; init; } = new();
            public double GrainAngle { get; init; }
            public CuttingParametersDto CuttingParameters { get; init; }
            public List<PartDto> Parts { get; init; } = new();
            public List<CutOffDto> CutOffs { get; init; } = new();

            /// <summary>
            /// Machine clamp keep-out rectangles in plate coordinates. Missing in older
            /// files, which load with no zones.
            /// </summary>
            public List<ClampZoneDto> Clamps { get; init; } = new();

            /// <summary>
            /// Rail clamp ids removed from this sheet as a last resort (D8). Missing or empty
            /// means none were removed; <see cref="Clamps"/> already excludes them.
            /// </summary>
            public List<string> RemovedClamps { get; init; } = new();
        }

        public record ClampZoneDto
        {
            public string Id { get; init; } = "";
            public double X { get; init; }
            public double Y { get; init; }

            /// <summary>Extent along X.</summary>
            public double Length { get; init; }

            /// <summary>Extent along Y (depth from the y = 0 operator-side edge).</summary>
            public double Width { get; init; }
        }

        public record PartDto
        {
            public int DrawingId { get; init; }
            public double X { get; init; }
            public double Y { get; init; }
            public double Rotation { get; init; }
            public bool HasManualLeadIns { get; init; }
            public bool LeadInsLocked { get; init; }
            public string Program { get; init; }
            public string DrawingHash { get; init; }
        }

        public record CutOffDto
        {
            public double X { get; init; }
            public double Y { get; init; }
            public string Axis { get; init; } = "vertical";
            public double? StartLimit { get; init; }
            public double? EndLimit { get; init; }

            /// <summary>
            /// Zero-based place in the plate's cut sequence (<c>Plate.Parts</c> order,
            /// cut-offs included). Null in older files, which load it at the end.
            /// </summary>
            public int? Sequence { get; init; }
        }

        public record SizeDto
        {
            public double Width { get; init; }
            public double Length { get; init; }
        }

        public record MaterialDto
        {
            public string Name { get; init; } = "";
            public string Grade { get; init; } = "";
            public double Density { get; init; }
        }

        public record SpacingDto
        {
            public double Left { get; init; }
            public double Top { get; init; }
            public double Right { get; init; }
            public double Bottom { get; init; }
        }

        public record ColorDto
        {
            public int A { get; init; } = 255;
            public int R { get; init; }
            public int G { get; init; }
            public int B { get; init; }
        }

        public record QuantityDto
        {
            public int Required { get; init; }
        }

        public record ConstraintsDto
        {
            public double StepAngle { get; init; }
            public double StartAngle { get; init; }
            public double EndAngle { get; init; }
            public bool Allow180Equivalent { get; init; }
        }

        public record SourceDto
        {
            public string Path { get; init; } = "";
            public OffsetDto Offset { get; init; } = new();
        }

        public record OffsetDto
        {
            public double X { get; init; }
            public double Y { get; init; }
        }

        public record BendDto
        {
            public double StartX { get; init; }
            public double StartY { get; init; }
            public double EndX { get; init; }
            public double EndY { get; init; }
            public string Direction { get; init; } = "Unknown";
            public double? Angle { get; init; }
            public double? Radius { get; init; }
            public string NoteText { get; init; } = "";
        }

        public record PlateOptionDto
        {
            public double Width { get; init; }
            public double Length { get; init; }
            public double Cost { get; init; }
        }

        public record EntitySetDto
        {
            public List<EntityDto> Entities { get; init; } = new();
            public List<string> Suppressed { get; init; } = new();
        }

        public record EntityDto
        {
            public string Id { get; init; } = "";
            public string Type { get; init; } = "";
            public string Layer { get; init; } = "";
            public string LineType { get; init; } = "";

            // Line
            public double X1 { get; init; }
            public double Y1 { get; init; }
            public double X2 { get; init; }
            public double Y2 { get; init; }

            // Arc / Circle
            public double CX { get; init; }
            public double CY { get; init; }
            public double R { get; init; }
            public double StartAngle { get; init; }
            public double EndAngle { get; init; }
            public bool Reversed { get; init; }
            public string Rotation { get; init; } = "";
        }

        public record BestFitSetDto
        {
            public double PlateWidth { get; init; }
            public double PlateHeight { get; init; }
            public double Spacing { get; init; }
            public List<BestFitResultDto> Results { get; init; } = new();
        }

        public record BestFitResultDto
        {
            public double Part1Rotation { get; init; }
            public double Part2Rotation { get; init; }
            public double Part2OffsetX { get; init; }
            public double Part2OffsetY { get; init; }
            public int StrategyType { get; init; }
            public int TestNumber { get; init; }
            public double CandidateSpacing { get; init; }
            public double RotatedArea { get; init; }
            public double BoundingWidth { get; init; }
            public double BoundingHeight { get; init; }
            public double OptimalRotation { get; init; }
            public bool Keep { get; init; }
            public string Reason { get; init; } = "";
            public double TrueArea { get; init; }
            public List<double> HullAngles { get; init; } = new();
        }
    }
}
