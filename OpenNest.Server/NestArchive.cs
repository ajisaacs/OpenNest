using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Reads the facts a nest record states about its archive straight from the uploaded
/// <c>.nest</c>, so the stored record always describes the stored bytes. Only
/// <c>nest.json</c> is parsed: programs and geometry are never rebuilt, which keeps the
/// check cheap and keeps the server off <c>OpenNest.IO</c>. The fallbacks mirror
/// <c>NestReader</c>; <c>NestArchiveTests</c> pins them against real <c>NestWriter</c> output.
/// </summary>
internal static class NestArchive
{
    /// <summary>Largest <c>nest.json</c> the server will decompress.</summary>
    public const int MaxNestJsonBytes = 64 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Overwrites the record's plate count, part count, material and thickness with the
    /// archive's own. Returns an error when the bytes are not a readable nest archive.
    /// </summary>
    public static string? Apply(NestRecord record, byte[] file)
    {
        if (Read(file, out var nest) is { } error)
            return error;

        var defaults = nest.PlateDefaults ?? new PlateDefaultsJson();
        // NestReader: an absent nest-level material is an empty one; only an explicit null
        // falls back to the plate defaults. Thickness falls back when not positive.
        record.Material = (nest.Material ?? defaults.Material)?.Name ?? "";
        record.Thickness = nest.Thickness > 0 ? nest.Thickness : defaults.Thickness;

        // NestReader drops parts whose drawing is missing. Cut-off parts are never written as
        // parts, so every remaining part counts.
        var drawingIds = (nest.Drawings ?? new()).Select(d => d.Id).ToHashSet();
        var plates = NestPlates(nest, drawingIds);
        record.PartCount = plates.Sum(p => (p.Parts ?? new()).Count(part => drawingIds.Contains(part.DrawingId)));
        record.PlateCount = plates.Count;
        return null;
    }

    /// <summary>
    /// Reads the sheet sizes and part counts the archive's plates need, by the same plate
    /// rules as <see cref="Apply"/>. Returns an error when the bytes are not a readable nest archive.
    /// </summary>
    public static string? ReadSheets(NestRecord record, byte[] file, out NestSheets? sheets)
    {
        sheets = null;
        if (Read(file, out var nest) is { } error)
            return error;

        var drawings = nest.Drawings ?? new();
        var drawingIds = drawings.Select(d => d.Id).ToHashSet();
        var plates = NestPlates(nest, drawingIds);

        // Width is the shorter side, so a plate stored rotated groups with its unrotated twin.
        var sizes = plates
            .Select(p => (Size: p.Size ?? new SizeJson(), p.Quantity))
            .GroupBy(p => (Width: System.Math.Min(p.Size.Width, p.Size.Length), Length: System.Math.Max(p.Size.Width, p.Size.Length)))
            .OrderBy(g => g.Key.Width)
            .ThenBy(g => g.Key.Length)
            .Select(g => new NestSheetSize { Width = g.Key.Width, Length = g.Key.Length, Quantity = g.Sum(p => p.Quantity) })
            .ToList();

        var nested = new Dictionary<int, int>();
        foreach (var plate in plates)
        {
            foreach (var part in plate.Parts ?? new())
            {
                if (drawingIds.Contains(part.DrawingId))
                    nested[part.DrawingId] = nested.GetValueOrDefault(part.DrawingId) + plate.Quantity;
            }
        }

        var parts = drawings
            .Select(d => new NestPartCount
            {
                Name = d.Name ?? "",
                Required = d.Quantity?.Required ?? 0,
                Nested = nested.GetValueOrDefault(d.Id),
            })
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        // NestReader: codes end at the first blank entry and at the plate's quantity.
        var platesOut = plates
            .Select((p, index) =>
            {
                var size = p.Size ?? new SizeJson();
                return new NestPlateSheet
                {
                    Index = index,
                    Width = System.Math.Min(size.Width, size.Length),
                    Length = System.Math.Max(size.Width, size.Length),
                    Quantity = p.Quantity,
                    StockItemCodes = (p.StockItemCodes ?? new())
                        .TakeWhile(code => !string.IsNullOrWhiteSpace(code))
                        .Take(System.Math.Max(p.Quantity, 0))
                        .ToList(),
                    ExpectedRemnant = ReadRemnant(p.ExpectedRemnant),
                };
            })
            .ToList();

        // NestReader: absent or unknown units keep the nest default, inches.
        var units = Enum.TryParse<Units>(nest.Units, ignoreCase: true, out var parsed) ? parsed : Units.Inches;

        sheets = new NestSheets
        {
            NestId = record.Id,
            Revision = record.Revision,
            Units = units,
            Sheets = sizes,
            Parts = parts,
            Plates = platesOut,
        };
        return null;
    }

    private static string? Read(byte[] file, out NestJson nest)
    {
        nest = null!;
        NestJson? read;
        try
        {
            using var archive = new ZipArchive(new MemoryStream(file, writable: false), ZipArchiveMode.Read);
            var entry = archive.GetEntry("nest.json");
            if (entry is null)
                return "The file is not a nest archive: it has no nest.json.";
            if (entry.Length > MaxNestJsonBytes)
                return "The nest archive's nest.json is too large.";
            using var json = new MemoryStream();
            using (var stream = entry.Open())
                CopyBounded(stream, json);
            json.Position = 0;
            read = JsonSerializer.Deserialize<NestJson>(json, Options);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            return "The file is not a readable nest archive.";
        }

        if (read is null)
            return "The nest archive's nest.json is empty.";
        nest = read;
        return null;
    }

    /// <summary>The nest's plates in id order, as NestReader orders them, without the new-plate slot.</summary>
    private static List<PlateJson> NestPlates(NestJson nest, HashSet<int> drawingIds)
    {
        var plates = (nest.Plates ?? new()).OrderBy(p => p.Id).ToList();
        // PlateDisplayNumbering.DisplayedPlateCount: a trailing plate with nothing on it is the
        // editor's new-plate slot, not a plate of the nest.
        var last = plates.LastOrDefault();
        var trailingEmpty = last is not null
            && !(last.Parts ?? new()).Any(part => drawingIds.Contains(part.DrawingId))
            && (last.CutOffs ?? new()).Count == 0;
        if (trailingEmpty)
            plates.RemoveAt(plates.Count - 1);
        return plates;
    }

    // The declared entry size is untrusted; stop at the cap however much the data inflates.
    private static void CopyBounded(Stream source, Stream destination)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxNestJsonBytes)
                throw new InvalidDataException("nest.json exceeds the size limit.");
            destination.Write(buffer, 0, read);
        }
    }

    private sealed class NestJson
    {
        public string? Units { get; set; }
        public double Thickness { get; set; }
        public MaterialJson? Material { get; set; } = new();
        public PlateDefaultsJson? PlateDefaults { get; set; }
        public List<DrawingJson>? Drawings { get; set; }
        public List<PlateJson>? Plates { get; set; }
    }

    private sealed class PlateDefaultsJson
    {
        public double Thickness { get; set; }
        public MaterialJson? Material { get; set; } = new();
    }

    private sealed class MaterialJson
    {
        public string? Name { get; set; } = "";
    }

    private sealed class DrawingJson
    {
        public int Id { get; set; }
        public string? Name { get; set; } = "";
        public QuantityJson? Quantity { get; set; } = new();
    }

    private sealed class QuantityJson
    {
        public int Required { get; set; }
    }

    private sealed class PlateJson
    {
        public int Id { get; set; }
        public SizeJson? Size { get; set; } = new();
        public int Quantity { get; set; } = 1;
        public List<PartJson>? Parts { get; set; }
        public List<CutOffJson>? CutOffs { get; set; }

        [JsonConverter(typeof(LenientStringListJson))]
        public List<string>? StockItemCodes { get; set; }

        [JsonConverter(typeof(LenientRemnantJson))]
        public RemnantJson? ExpectedRemnant { get; set; }
    }

    private sealed class RemnantJson
    {
        public double Width { get; set; }
        public double Length { get; set; }
        public string? Source { get; set; }
    }

    // Derived data: a wrongly shaped value reads as none rather than failing the whole nest.
    private sealed class LenientRemnantJson : JsonConverter<RemnantJson?>
    {
        public override RemnantJson? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryNumber(root, "width", out var width)
                || !TryNumber(root, "length", out var length))
                return null;
            var source = root.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;
            return new RemnantJson { Width = width, Length = length, Source = source };
        }

        public override void Write(Utf8JsonWriter writer, RemnantJson? value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value);

        private static bool TryNumber(JsonElement element, string name, out double value)
        {
            value = 0;
            return element.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetDouble(out value);
        }
    }

    private static NestExpectedRemnant? ReadRemnant(RemnantJson? remnant) =>
        remnant is { Source: "cutoff" or "offcut" }
            && double.IsFinite(remnant.Width) && double.IsFinite(remnant.Length)
            && remnant.Width > 0 && remnant.Length > 0
                ? new NestExpectedRemnant
                {
                    Width = System.Math.Min(remnant.Width, remnant.Length),
                    Length = System.Math.Max(remnant.Width, remnant.Length),
                    Source = remnant.Source,
                }
                : null;

    // NestReader's lenient list: a wrongly shaped value reads as empty, non-strings are skipped.
    private sealed class LenientStringListJson : JsonConverter<List<string>>
    {
        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var list = new List<string>();
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                    list.Add(element.GetString()!);
            }
            return list;
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value);
    }

    private sealed class SizeJson
    {
        public double Width { get; set; }
        public double Length { get; set; }
    }

    private sealed class PartJson
    {
        public int DrawingId { get; set; }
    }

    private sealed class CutOffJson
    {
    }
}
