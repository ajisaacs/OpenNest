using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenNest.IO
{
    /// <summary>
    /// A drawing library package: a .nest archive holding exactly one drawing and no
    /// plates, written and read by the ordinary <see cref="NestWriter"/>/<see cref="NestReader"/>.
    /// Packages are canonical, so the same drawing content always has the same bytes and
    /// SHA-256: archive timestamps and nest dates are fixed, the nest-specific required
    /// quantity is zero, and derived best-fit caches and library links are left out.
    /// </summary>
    public static class DrawingPackage
    {
        // The earliest time a ZIP entry can record.
        private static readonly DateTimeOffset EntryTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private const string FixedDate = "2000-01-01T00:00:00.0000000";

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
            if (root["drawings"] is JsonArray drawings)
            {
                foreach (var drawing in drawings.OfType<JsonObject>())
                {
                    if (drawing["quantity"] is JsonObject quantity)
                        quantity["required"] = 0;
                    // A library link is nest-side provenance; the server's record describes the package.
                    drawing.Remove("library");
                }
            }

            return root.ToJsonString(NestFormat.JsonOptions);
        }

        // StreamReader honours the byte-order mark the nest writer emits.
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
