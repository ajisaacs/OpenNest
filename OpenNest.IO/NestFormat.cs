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

            /// <summary>
            /// The machine support bed every sheet of the nest rests on (docs/support-tabs.md).
            /// Omitted when the nest has none; older files and older readers simply have none.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            [JsonConverter(typeof(LenientSupportBedConverter))]
            public SupportBedDto SupportBed { get; init; }
        }

        /// <summary>
        /// A support bed layout. <see cref="Type"/> names the layout: only <c>grid</c>, a regular
        /// grid of contact points, so far. Lengths are in the nest's units.
        /// </summary>
        public record SupportBedDto
        {
            public string Type { get; init; } = "grid";
            public double PitchX { get; init; }
            public double PitchY { get; init; }
            public double OffsetX { get; init; }
            public double OffsetY { get; init; }
            public double RegistrationTolerance { get; init; }
            public double StabilityMargin { get; init; }
        }

        /// <summary>
        /// Reads a malformed, wrongly typed or unknown support bed as none instead of failing the
        /// whole nest; the reader still validates the values it receives. Optional lengths
        /// default to zero.
        /// </summary>
        public sealed class LenientSupportBedConverter : JsonConverter<SupportBedDto>
        {
            private static readonly string[] Optional = { "offsetX", "offsetY", "registrationTolerance", "stabilityMargin" };

            public override SupportBedDto Read(ref Utf8JsonReader reader, Type typeToConvert,
                JsonSerializerOptions options)
            {
                using var document = JsonDocument.ParseValue(ref reader);
                var bed = document.RootElement;
                if (bed.ValueKind != JsonValueKind.Object
                    || !bed.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                    || type.GetString() != "grid"
                    || !TryNumber(bed, "pitchX", out var pitchX) || !TryNumber(bed, "pitchY", out var pitchY))
                    return null;
                var values = new double[Optional.Length];
                for (var i = 0; i < Optional.Length; i++)
                    if (bed.TryGetProperty(Optional[i], out _) && !TryNumber(bed, Optional[i], out values[i]))
                        return null;
                return new SupportBedDto
                {
                    PitchX = pitchX,
                    PitchY = pitchY,
                    OffsetX = values[0],
                    OffsetY = values[1],
                    RegistrationTolerance = values[2],
                    StabilityMargin = values[3],
                };
            }

            public override void Write(Utf8JsonWriter writer, SupportBedDto value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString("type", value.Type);
                writer.WriteNumber("pitchX", value.PitchX);
                writer.WriteNumber("pitchY", value.PitchY);
                writer.WriteNumber("offsetX", value.OffsetX);
                writer.WriteNumber("offsetY", value.OffsetY);
                writer.WriteNumber("registrationTolerance", value.RegistrationTolerance);
                writer.WriteNumber("stabilityMargin", value.StabilityMargin);
                writer.WriteEndObject();
            }

            private static bool TryNumber(JsonElement bed, string name, out double value)
            {
                value = 0;
                return bed.TryGetProperty(name, out var element)
                    && element.ValueKind == JsonValueKind.Number
                    && element.TryGetDouble(out value);
            }
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

                var list = ReadCenters(centers);
                if (list == null)
                    return null;
                // Clamps on the other edges arrived later: a missing key means none there.
                var others = new List<double>[OtherEdgeKeys.Length];
                for (var i = 0; i < others.Length; i++)
                {
                    others[i] = new List<double>();
                    if (!rail.TryGetProperty(OtherEdgeKeys[i], out var edge))
                        continue;
                    others[i] = edge.ValueKind == JsonValueKind.Array ? ReadCenters(edge) : null;
                    if (others[i] == null)
                        return null;
                }
                // Missing or unreadable -> null; the reader then falls back to the default.
                double? removalMinutes = TryNumber(rail, "removalMinutes", out var minutes)
                    ? minutes
                    : null;
                return new ClampRailDto
                {
                    Width = width,
                    Depth = depth,
                    Centers = list,
                    FarSideCenters = others[0],
                    OriginEndCenters = others[1],
                    FarEndCenters = others[2],
                    RemovalMinutes = removalMinutes,
                };
            }

            private static readonly string[] OtherEdgeKeys = { "farSideCenters", "originEndCenters", "farEndCenters" };

            private static void WriteCenters(Utf8JsonWriter writer, string key, List<double> centers)
            {
                if (centers == null || centers.Count == 0)
                    return;
                writer.WriteStartArray(key);
                foreach (var center in centers)
                    writer.WriteNumberValue(center);
                writer.WriteEndArray();
            }

            private static List<double> ReadCenters(JsonElement centers)
            {
                var list = new List<double>();
                foreach (var center in centers.EnumerateArray())
                {
                    if (center.ValueKind != JsonValueKind.Number || !center.TryGetDouble(out var x))
                        return null;
                    list.Add(x);
                }
                return list;
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
                // Omitted when empty so origin-side-only rails write exactly as before.
                WriteCenters(writer, OtherEdgeKeys[0], value.FarSideCenters);
                WriteCenters(writer, OtherEdgeKeys[1], value.OriginEndCenters);
                WriteCenters(writer, OtherEdgeKeys[2], value.FarEndCenters);
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

        /// <summary>
        /// Reads an optional list of strings without failing the nest: a non-array value
        /// reads as an empty list and non-string entries are skipped.
        /// </summary>
        public sealed class LenientStringListConverter : JsonConverter<List<string>>
        {
            public override List<string> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                using var document = JsonDocument.ParseValue(ref reader);
                var list = new List<string>();
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return list;
                foreach (var element in document.RootElement.EnumerateArray())
                    if (element.ValueKind == JsonValueKind.String)
                        list.Add(element.GetString());
                return list;
            }

            public override void Write(
                Utf8JsonWriter writer,
                List<string> value,
                JsonSerializerOptions options
            )
            {
                writer.WriteStartArray();
                foreach (var item in value)
                    writer.WriteStringValue(item);
                writer.WriteEndArray();
            }
        }

        public record ClampRailDto
        {
            /// <summary>Clamp extent along its edge.</summary>
            public double Width { get; init; }

            /// <summary>Clamp intrusion into the sheet from its edge.</summary>
            public double Depth { get; init; }

            /// <summary>Origin-side clamp centers along X, measured from the plate origin.</summary>
            public List<double> Centers { get; init; } = new();

            /// <summary>Far-side clamp centers along X from the origin; omitted when none.</summary>
            public List<double> FarSideCenters { get; init; } = new();

            /// <summary>Origin-end clamp centers along Y from the origin; omitted when none.</summary>
            public List<double> OriginEndCenters { get; init; } = new();

            /// <summary>Far-end clamp centers along Y from the origin; omitted when none.</summary>
            public List<double> FarEndCenters { get; init; } = new();

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

            /// <summary>
            /// The color was chosen on purpose and survives scheme changes. Omitted when false;
            /// readers recolor such drawings from the active palette.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool CustomColor { get; init; }

            public QuantityDto Quantity { get; init; } = new();
            public int Priority { get; init; }

            /// <summary>Sequence this drawing's parts first. Omitted when false.</summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool CutFirst { get; init; }

            public ConstraintsDto Constraints { get; init; } = new();
            public MaterialDto Material { get; init; } = new();
            public SourceDto Source { get; init; } = new();
            public List<BendDto> Bends { get; init; } = new();

            /// <summary>
            /// The drawing library revision this drawing was copied from. Omitted when the
            /// drawing did not come from a library. Read leniently: a malformed value reads
            /// as <see cref="DrawingLibraryLinkDto.Malformed"/> and the reader drops it with a warning.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            [JsonConverter(typeof(LenientDrawingLibraryLinkConverter))]
            public DrawingLibraryLinkDto Library { get; init; }

            /// <summary>
            /// Drawing cut pins in the source frame. Omitted when there are none. Read leniently:
            /// an entry that cannot be read becomes <see cref="CutPinDto.Malformed"/> and the
            /// reader skips it with a warning; the other pins load.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            [JsonConverter(typeof(LenientCutPinListConverter))]
            public List<CutPinDto> CutPins { get; init; }

            /// <summary>
            /// Breakoffs in the source frame, already applied to the stored program. Omitted when
            /// there are none.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<BreakoffDto> Breakoffs { get; init; }

            /// <summary>
            /// Tabbed cutouts in the source frame, already applied to the stored program. Omitted
            /// when there are none.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<TabbedCutoutDto> TabbedCutouts { get; init; }
        }

        public record TabbedCutoutDto
        {
            public List<Guid> EdgeIds { get; init; } = new();
            public double TabWidth { get; init; }
            public List<double> TabCenters { get; init; } = new();
        }

        public record BreakoffDto
        {
            public List<Guid> EdgeIds { get; init; } = new();

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool Flip { get; init; }

            public double TabWidth { get; init; }
            public List<double> TabCenters { get; init; } = new();
        }

        public record CutPinDto
        {
            public double X { get; init; }
            public double Y { get; init; }
            public CuttingParametersDto.LeadInDto LeadIn { get; init; }
            public CuttingParametersDto.LeadOutDto LeadOut { get; init; }

            /// <summary>Omitted for a closed contour.</summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public CuttingParametersDto.TabDto Tab { get; init; }

            /// <summary>Set only by the lenient reader for an entry it could not read.</summary>
            [JsonIgnore]
            public bool Malformed { get; init; }
        }

        /// <summary>
        /// Reads each cut pin on its own, so one wrongly shaped entry does not lose the rest or
        /// fail the nest. A value that is not an array reads as a single malformed entry.
        /// </summary>
        public sealed class LenientCutPinListConverter : JsonConverter<List<CutPinDto>>
        {
            public override List<CutPinDto> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                using var document = JsonDocument.ParseValue(ref reader);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return new List<CutPinDto> { new() { Malformed = true } };

                var list = new List<CutPinDto>();
                foreach (var element in document.RootElement.EnumerateArray())
                    list.Add(ReadPin(element, options));
                return list;
            }

            private static CutPinDto ReadPin(JsonElement element, JsonSerializerOptions options)
            {
                if (element.ValueKind != JsonValueKind.Object)
                    return new CutPinDto { Malformed = true };

                try
                {
                    return element.Deserialize<CutPinDto>(options) ?? new CutPinDto { Malformed = true };
                }
                catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is InvalidOperationException)
                {
                    return new CutPinDto { Malformed = true };
                }
            }

            public override void Write(
                Utf8JsonWriter writer,
                List<CutPinDto> value,
                JsonSerializerOptions options
            )
            {
                writer.WriteStartArray();
                foreach (var pin in value)
                    JsonSerializer.Serialize(writer, pin, options);
                writer.WriteEndArray();
            }
        }

        /// <summary>
        /// Reads only a JSON boolean. Anything else, null included, reads as null so the reader
        /// can warn; the property's initializer covers a missing value.
        /// </summary>
        public sealed class LenientFlagConverter : JsonConverter<bool?>
        {
            public override bool HandleNull => true;

            public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.True)
                    return true;
                if (reader.TokenType == JsonTokenType.False)
                    return false;
                reader.Skip();
                return null;
            }

            public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
            {
                if (value is bool flag)
                    writer.WriteBooleanValue(flag);
                else
                    writer.WriteNullValue();
            }
        }

        public record DrawingLibraryLinkDto
        {
            public string ServerKey { get; init; } = "";
            public Guid Id { get; init; }
            public int Revision { get; init; }
            public string CutType { get; init; } = "";
            public double Thickness { get; init; }
            public string MaterialName { get; init; } = "";
            public string MaterialGrade { get; init; } = "";
            public string Units { get; init; } = "";
            public string Notes { get; init; } = "";
            public string PackageSha256 { get; init; } = "";

            /// <summary>Set only by the lenient reader for a value it could not read.</summary>
            [JsonIgnore]
            public bool Malformed { get; init; }
        }

        /// <summary>
        /// Reads a wrongly shaped or typed library link as a malformed marker instead of
        /// failing the whole nest; the nest reader validates the values it receives.
        /// </summary>
        public sealed class LenientDrawingLibraryLinkConverter : JsonConverter<DrawingLibraryLinkDto>
        {
            public override DrawingLibraryLinkDto Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                using var document = JsonDocument.ParseValue(ref reader);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return new DrawingLibraryLinkDto { Malformed = true };

                try
                {
                    return document.RootElement.Deserialize<DrawingLibraryLinkDto>(options)
                        ?? new DrawingLibraryLinkDto { Malformed = true };
                }
                catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is InvalidOperationException)
                {
                    return new DrawingLibraryLinkDto { Malformed = true };
                }
            }

            // The converter sits on the property, not the type, so this does not recurse.
            public override void Write(
                Utf8JsonWriter writer,
                DrawingLibraryLinkDto value,
                JsonSerializerOptions options
            ) => JsonSerializer.Serialize(writer, value, options);
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
            /// Rail clamp ids removed from this sheet as a last resort (D8). Omitted when none
            /// were removed; missing or empty means none; <see cref="Clamps"/> already excludes them. Read
            /// leniently: a wrongly shaped value reads as none removed, non-string entries
            /// are skipped, and the saved zones load either way.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            [JsonConverter(typeof(LenientStringListConverter))]
            public List<string> RemovedClamps { get; init; } = new();

            /// <summary>
            /// The machine clamp rail this sheet follows, written only when it is not the
            /// nest's default rail (otherwise the sheet re-attaches to that one on load).
            /// Read leniently like <see cref="PlateDefaultsDto.ClampRail"/>.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            [JsonConverter(typeof(LenientClampRailConverter))]
            public ClampRailDto ClampRail { get; init; }
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

            /// <summary>
            /// True when this placement ignores its drawing's cut pins. Written only when true;
            /// missing means false. Any value other than a boolean reads as null, which the
            /// reader reports and treats as false.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            [JsonConverter(typeof(LenientFlagConverter))]
            public bool? DrawingPinsOff { get; init; } = false;
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
            /// cut-offs included) of the cut-off's first segment. Null in older files, which
            /// load it at the end.
            /// </summary>
            public int? Sequence { get; init; }

            /// <summary>
            /// The place of each of the cut-off's trimmed segments in the cut sequence, in
            /// ascending order along its line, when they are not consecutive. Omitted for one
            /// segment or consecutive segments; readers without it place the segments
            /// consecutively from <see cref="Sequence"/>.
            /// </summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public int[] Segments { get; init; }
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

            /// <summary>Role overrides by entity ID ("cut", "etch" or "display"); omitted when none.</summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public Dictionary<string, string> Roles { get; init; }
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
            public double? CanonicalAngle { get; init; }
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
