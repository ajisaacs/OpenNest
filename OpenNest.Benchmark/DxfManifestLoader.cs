using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// Builds a BenchmarkJob from a JSON manifest that lists DXF files and the
    /// quantity of each to nest. DXF paths resolve relative to the manifest.
    /// Sheet sizes come from the manifest or the caller's override; unlike a
    /// .nest file there is no plate to inherit them from, so a job with none is
    /// an error. Sheet sizes must use the same units as the DXFs.
    /// </summary>
    public static class DxfManifestLoader
    {
        /// <summary>Suffix a manifest needs to be picked up when scanning a folder.</summary>
        public const string FolderSuffix = ".manifest.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static BenchmarkJob Load(
            string manifestPath,
            IReadOnlyList<Size> sheetSizeOverrides = null,
            double? partSpacingOverride = null
        )
        {
            var manifest = ReadManifest(manifestPath);
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath));

            if (manifest.Parts == null || manifest.Parts.Count == 0)
                throw new InvalidOperationException(
                    $"Manifest '{manifestPath}' has no parts. Add entries to \"parts\"."
                );

            var sizes = ResolveSheetSizes(manifest, sheetSizeOverrides, manifestPath);
            var requests = manifest.Parts.Select(p => BuildRequest(p, baseDir)).ToList();

            return new BenchmarkJob
            {
                SourceFile = manifestPath,
                CandidateSizes = sizes,
                EdgeSpacing = new Spacing(manifest.EdgeSpacing, manifest.EdgeSpacing),
                PartSpacing = partSpacingOverride ?? manifest.Spacing,
                Quadrant = manifest.Quadrant,
                Requests = requests,
            };
        }

        private static Manifest ReadManifest(string manifestPath)
        {
            try
            {
                return JsonSerializer.Deserialize<Manifest>(
                        File.ReadAllText(manifestPath),
                        JsonOptions
                    ) ?? throw new InvalidOperationException("The manifest is empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Manifest '{manifestPath}' is not valid JSON: {ex.Message}",
                    ex
                );
            }
        }

        private static List<Size> ResolveSheetSizes(
            Manifest manifest,
            IReadOnlyList<Size> overrides,
            string manifestPath
        )
        {
            if (overrides != null && overrides.Count > 0)
                return overrides.ToList();

            var sizes = new List<Size>();

            foreach (var text in manifest.SheetSizes ?? new List<string>())
            {
                if (!Size.TryParse(text, out var size))
                    throw new InvalidOperationException(
                        $"Manifest '{manifestPath}': could not parse sheet size '{text}' (expected e.g. \"48x96\")."
                    );

                sizes.Add(size);
            }

            if (sizes.Count == 0)
                throw new InvalidOperationException(
                    $"Manifest '{manifestPath}' has no sheet sizes. Set \"sheetSizes\" or pass --sheet-sizes."
                );

            return sizes.Distinct().ToList();
        }

        private static DrawingRequest BuildRequest(ManifestPart part, string baseDir)
        {
            if (string.IsNullOrWhiteSpace(part.Dxf))
                throw new InvalidOperationException("A manifest part is missing \"dxf\".");

            if (part.Quantity <= 0)
                throw new InvalidOperationException(
                    $"Manifest part '{part.Dxf}': quantity must be greater than 0 (was {part.Quantity})."
                );

            var dxfPath = Path.GetFullPath(Path.Combine(baseDir, part.Dxf));

            if (!File.Exists(dxfPath))
                throw new FileNotFoundException($"DXF file not found: {dxfPath}", dxfPath);

            Drawing drawing;

            try
            {
                drawing = CadImporter.ImportDrawing(
                    dxfPath,
                    new CadImportOptions { Quantity = part.Quantity }
                );
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to import DXF: {dxfPath}", ex);
            }

            if (drawing.Program == null || drawing.Program.Codes.Count == 0)
                throw new InvalidOperationException($"Failed to import DXF: {dxfPath}");

            // A zero legacy step means automatic rotation to DrawingJobMapper, so lock it explicitly.
            if (!part.AllowRotation)
            {
                drawing.Constraints ??= new NestConstraints();
                drawing.Constraints.StepAngle = OpenNest.Math.Angle.TwoPI;
                drawing.Constraints.StartAngle = 0;
                drawing.Constraints.EndAngle = 0;
            }

            var constraints = drawing.Constraints;

            return new DrawingRequest
            {
                Drawing = drawing,
                Quantity = part.Quantity,
                Priority = drawing.Priority,
                StepAngle = constraints?.StepAngle ?? 0,
                RotationStart = constraints?.StartAngle ?? 0,
                RotationEnd = constraints?.EndAngle ?? 0,
            };
        }

        private class Manifest
        {
            public List<string> SheetSizes { get; set; }
            public double Spacing { get; set; }
            public double EdgeSpacing { get; set; }
            public int Quadrant { get; set; } = 1;
            public List<ManifestPart> Parts { get; set; }
        }

        private class ManifestPart
        {
            public string Dxf { get; set; }
            public int Quantity { get; set; }
            public bool AllowRotation { get; set; } = true;
        }
    }
}
