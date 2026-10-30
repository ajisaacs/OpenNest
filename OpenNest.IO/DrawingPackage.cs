using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenNest.IO
{
    /// <summary>
    /// A drawing library package: a .nest archive holding exactly one drawing and no
    /// plates, written and read by the ordinary <see cref="NestWriter"/>/<see cref="NestReader"/>.
    /// Packages are canonical, so the same drawing content always has the same bytes and
    /// SHA-256: archive timestamps and nest dates are fixed, the nest-specific required
    /// quantity is zero, and derived best-fit caches and library links are left out.
    /// <para>
    /// A package holds geometry and geometry-bound data only (program, source entities,
    /// suppressed IDs, bends, cut pins, breakoffs, material density). Fields the library
    /// record owns are written as their defaults: the nest and drawing names, customer,
    /// material name and grade, color, priority, cut first and rotation constraints.
    /// Renaming a library drawing or changing its nesting settings therefore never changes
    /// its package hash; adding it to a nest applies the record's values instead.
    /// </para>
    /// </summary>
    public static class DrawingPackage
    {
        // The earliest time a ZIP entry can record.
        private static readonly DateTimeOffset EntryTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private const string FixedDate = "2000-01-01T00:00:00.0000000";

        // Removed so readers fall back to their defaults; the library record owns these values.
        private static readonly string[] RecordOwnedDrawingFields = { "customer", "color", "customColor", "priority", "cutFirst", "constraints" };

        /// <summary>Packages <paramref name="drawing"/> without changing it.</summary>
        public static byte[] Write(Drawing drawing, Units units)
        {
            ArgumentNullException.ThrowIfNull(drawing);
            if (!Enum.IsDefined(units))
                throw new ArgumentOutOfRangeException(nameof(units), units, "Unsupported units.");

            var nest = new Nest(drawing.Name ?? "") { Units = units };
            nest.Drawings.Add(drawing);
            using var written = new MemoryStream();
            new NestWriter(nest).Write(written);
            return Canonicalize(written.ToArray());
        }

        /// <summary>
        /// Reads a package. Throws <see cref="InvalidDataException"/> unless it is a .nest
        /// archive with exactly one drawing, no plates and supported units.
        /// </summary>
        public static DrawingPackageContents Read(byte[] package)
        {
            ArgumentNullException.ThrowIfNull(package);
            try
            {
                var units = CheckShape(package);
                var reader = new NestReader(new MemoryStream(package, writable: false));
                var nest = reader.Read();
                if (nest.Plates.Count != 0 || nest.Drawings.Count != 1)
                    throw new InvalidDataException("A drawing package must hold exactly one drawing and no plates.");
                return new DrawingPackageContents(nest.Drawings.Single(), units, reader.Warnings.ToList());
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is ArgumentException
                || (ex is IOException && ex is not InvalidDataException))
            {
                throw new InvalidDataException("The drawing package is not a readable .nest archive.", ex);
            }
        }

        /// <summary>
        /// A key that is equal for packages holding the same geometry: the SHA-256 of the
        /// package's entries with the source file path left out and every GUID (entity ids
        /// and the references to them) replaced by its order of first appearance. An unchanged
        /// DXF now imports with the same entity ids, but from another folder it records another
        /// path, and packages saved before stable ids carry random ones, so their hashes differ
        /// while this key does not.
        /// </summary>
        public static string GeometryKey(byte[] package)
        {
            ArgumentNullException.ThrowIfNull(package);
            var text = new StringBuilder();
            try
            {
                using var archive = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
                foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
                {
                    var content = ReadText(entry);
                    if (entry.FullName == "nest.json" && JsonNode.Parse(content) is JsonObject root)
                    {
                        foreach (var drawing in (root["drawings"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                            (drawing["source"] as JsonObject)?.Remove("path");
                        content = root.ToJsonString();
                    }

                    text.Append(entry.FullName).Append('\n').Append(content).Append('\n');
                }
            }
            catch (Exception ex) when (ex is JsonException || (ex is IOException && ex is not InvalidDataException))
            {
                throw new InvalidDataException("The drawing package is not a readable .nest archive.", ex);
            }

            var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var normalized = GuidPattern.Replace(text.ToString(), match =>
            {
                if (!ids.TryGetValue(match.Value, out var index))
                    ids[match.Value] = index = ids.Count;
                return "#" + index;
            });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        }

        private static readonly Regex GuidPattern = new(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Checks the archive's own declaration before the lenient nest reader sees it: that
        // reader defaults unknown units and drops empty plates, which a package must not hide.
        private static Units CheckShape(byte[] package)
        {
            using var archive = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
            var entry = archive.GetEntry("nest.json")
                ?? throw new InvalidDataException("The drawing package has no nest.json.");
            var root = JsonNode.Parse(ReadText(entry)) as JsonObject
                ?? throw new InvalidDataException("The drawing package's nest.json is not an object.");

            if (root["plates"] is JsonArray plates && plates.Count != 0)
                throw new InvalidDataException("A drawing package must not contain plates.");
            if (root["drawings"] is not JsonArray drawings || drawings.Count != 1)
                throw new InvalidDataException("A drawing package must contain exactly one drawing.");

            var text = root["units"] is JsonValue value && value.TryGetValue<string>(out var s) ? s : "";
            if (text.Length == 0 || !text.All(char.IsAsciiLetter)
                || !Enum.TryParse<Units>(text, ignoreCase: true, out var units) || !Enum.IsDefined(units))
                throw new InvalidDataException($"The drawing package has unsupported units '{text}'.");
            return units;
        }

        private static byte[] Canonicalize(byte[] archive)
        {
            using var source = new ZipArchive(new MemoryStream(archive, writable: false), ZipArchiveMode.Read);
            using var output = new MemoryStream();
            using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in source.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
                {
                    if (entry.FullName.StartsWith("bestfits/", StringComparison.Ordinal))
                        continue;

                    byte[] bytes;
                    if (entry.FullName == "nest.json")
                    {
                        bytes = Encoding.UTF8.GetBytes(CanonicalNestJson(ReadText(entry)));
                    }
                    else
                    {
                        using var input = entry.Open();
                        using var copy = new MemoryStream();
                        input.CopyTo(copy);
                        bytes = copy.ToArray();
                    }

                    var written = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    written.LastWriteTime = EntryTimestamp;
                    using var stream = written.Open();
                    stream.Write(bytes, 0, bytes.Length);
                }
            }

            return output.ToArray();
        }

        private static string CanonicalNestJson(string json)
        {
            var root = JsonNode.Parse(json)!.AsObject();
            root["dateCreated"] = FixedDate;
            root["dateLastModified"] = FixedDate;
            root["name"] = "";
            if (root["drawings"] is JsonArray drawings)
            {
                foreach (var drawing in drawings.OfType<JsonObject>())
                {
                    if (drawing["quantity"] is JsonObject quantity)
                        quantity["required"] = 0;
                    // A library link is nest-side provenance; the server's record describes the package.
                    drawing.Remove("library");
                    // Record-owned fields: the library record supplies them on insert.
                    drawing["name"] = "";
                    foreach (var field in RecordOwnedDrawingFields)
                        drawing.Remove(field);
                    if (drawing["material"] is JsonObject material)
                    {
                        material["name"] = "";
                        material["grade"] = "";
                    }
                }
            }

            return root.ToJsonString(NestFormat.JsonOptions);
        }

        // StreamReader honors the byte-order mark the nest writer emits.
        private static string ReadText(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    /// <summary>The drawing read from a package, the units it was drawn in, and reader warnings.</summary>
    public sealed class DrawingPackageContents
    {
        public DrawingPackageContents(Drawing drawing, Units units, IReadOnlyList<string> warnings)
        {
            Drawing = drawing;
            Units = units;
            Warnings = warnings;
        }

        public Drawing Drawing { get; }

        public Units Units { get; }

        public IReadOnlyList<string> Warnings { get; }
    }
}
