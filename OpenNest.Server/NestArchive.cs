using System.IO.Compression;
using System.Text.Json;
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
        NestJson? nest;
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
            nest = JsonSerializer.Deserialize<NestJson>(json, Options);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            return "The file is not a readable nest archive.";
        }

        if (nest is null)
            return "The nest archive's nest.json is empty.";

        var defaults = nest.PlateDefaults ?? new PlateDefaultsJson();
        // NestReader: an absent nest-level material is an empty one; only an explicit null
        // falls back to the plate defaults. Thickness falls back when not positive.
        record.Material = (nest.Material ?? defaults.Material)?.Name ?? "";
        record.Thickness = nest.Thickness > 0 ? nest.Thickness : defaults.Thickness;

        // NestReader drops parts whose drawing is missing. Cut-off parts are never written as
        // parts, so every remaining part counts.
        var drawingIds = (nest.Drawings ?? new()).Select(d => d.Id).ToHashSet();
        var plates = (nest.Plates ?? new()).OrderBy(p => p.Id).ToList();
        record.PartCount = plates.Sum(p => (p.Parts ?? new()).Count(part => drawingIds.Contains(part.DrawingId)));
        // PlateDisplayNumbering.DisplayedPlateCount: a trailing plate with nothing on it is the
        // editor's new-plate slot, not a plate of the nest.
        var last = plates.LastOrDefault();
        var trailingEmpty = last is not null
            && !(last.Parts ?? new()).Any(part => drawingIds.Contains(part.DrawingId))
            && (last.CutOffs ?? new()).Count == 0;
        record.PlateCount = plates.Count - (trailingEmpty ? 1 : 0);
        return null;
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
    }

    private sealed class PlateJson
    {
        public int Id { get; set; }
        public List<PartJson>? Parts { get; set; }
        public List<CutOffJson>? CutOffs { get; set; }
    }

    private sealed class PartJson
    {
        public int DrawingId { get; set; }
    }

    private sealed class CutOffJson
    {
    }
}
