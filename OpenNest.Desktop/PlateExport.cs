using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenNest.IO;
using OpenNest.Math;

namespace OpenNest.Desktop;

/// <summary>What File &gt; Export writes for one plate.</summary>
public enum PlateExportFormat
{
    /// <summary>The plate's parts as DXF geometry.</summary>
    Dxf,

    /// <summary>A picture of the plate view as shown; the front end renders it.</summary>
    Image,

    /// <summary>One line per part: source drawing path | X,Y | rotation in degrees.</summary>
    PlacementText,

    /// <summary>The plate's parts as SVG in the nest's units.</summary>
    Svg,
}

/// <summary>
/// File &gt; Export, Export All and Plate &gt; View in CAD for both desktop apps: the file each
/// plate is suggested to be saved as, the format a chosen file asks for, and the writers. The
/// image of the view is the one format the toolkit draws, so its writer is passed in.
/// </summary>
public static class PlateExport
{
    // Characters Windows refuses in a file name, refused on every platform so a suggested name
    // works wherever the file is later copied. The platform's own set is added to them.
    private static readonly char[] InvalidFileNameChars = "<>:\"/\\|?*"
        .Concat(Enumerable.Range(0, 32).Select(c => (char)c))
        .Concat(Path.GetInvalidFileNameChars())
        .Distinct()
        .ToArray();

    /// <summary>
    /// The WinForms save dialog's file types in order: its 1-based filter index picks the format.
    /// </summary>
    public static IReadOnlyList<PlateExportFormat> DialogFormats { get; } = new[]
    {
        PlateExportFormat.Dxf,
        PlateExportFormat.Image,
        PlateExportFormat.PlacementText,
        PlateExportFormat.Svg,
    };

    /// <summary>The format for the save dialog's 1-based <paramref name="filterIndex"/>.</summary>
    public static PlateExportFormat FromFilterIndex(int filterIndex) =>
        filterIndex >= 1 && filterIndex <= DialogFormats.Count
            ? DialogFormats[filterIndex - 1]
            : throw new ArgumentException("Unsupported plate export format.", nameof(filterIndex));

    /// <summary>
    /// The format a chosen file's extension asks for (.dxf, .png/.jpg/.jpeg/.bmp, .txt, .svg,
    /// any case), or null for any other extension or none.
    /// </summary>
    public static PlateExportFormat? FromPath(string path) =>
        Path.GetExtension(path ?? "").ToLowerInvariant() switch
        {
            ".dxf" => PlateExportFormat.Dxf,
            ".png" or ".jpg" or ".jpeg" or ".bmp" => PlateExportFormat.Image,
            ".txt" => PlateExportFormat.PlacementText,
            ".svg" => PlateExportFormat.Svg,
            _ => null,
        };

    /// <summary>
    /// The name a plate's export is suggested under, without an extension: the nest name and
    /// "-P" with the 1-based plate number ("job-P2"). Characters a file name cannot hold on any
    /// platform become underscores; a blank nest name is "Nest".
    /// </summary>
    public static string SuggestedFileName(string nestName, int plateNumber)
    {
        var baseName = string.IsNullOrWhiteSpace(nestName) ? "Nest" : nestName.Trim();
        var chars = baseName.Select(c => Array.IndexOf(InvalidFileNameChars, c) >= 0 ? '_' : c).ToArray();
        return $"{new string(chars)}-P{plateNumber}";
    }

    /// <summary>The DXF that View in CAD writes for the plate and opens: <see cref="SuggestedFileName"/>.dxf.</summary>
    public static string ExternalCadFileName(string nestName, int plateNumber) =>
        SuggestedFileName(nestName, plateNumber) + ".dxf";

    /// <summary>Where View in CAD writes the plate's DXF: the user's temp folder.</summary>
    public static string ExternalCadPath(string nestName, int plateNumber) =>
        Path.Combine(Path.GetTempPath(), ExternalCadFileName(nestName, plateNumber));

    /// <summary>
    /// Writes <paramref name="plate"/> to <paramref name="destination"/> as
    /// <paramref name="format"/>. SVG is written in <paramref name="units"/>; the image is drawn by
    /// <paramref name="writeImage"/> (the shown view, which only the toolkit can draw). The file
    /// is written in place: a failure part-way can leave a partial file, as the WinForms export
    /// always has. The nest, plate and parts are only read.
    /// </summary>
    public static void Write(
        Plate plate,
        Units units,
        string destination,
        PlateExportFormat format,
        Action<string> writeImage)
    {
        ArgumentNullException.ThrowIfNull(plate);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        switch (format)
        {
            case PlateExportFormat.Dxf:
                Dxf.ExportPlate(plate, destination);
                break;
            case PlateExportFormat.Image:
                if (writeImage == null)
                    throw new NotSupportedException("This view cannot be saved as an image.");
                writeImage(destination);
                break;
            case PlateExportFormat.PlacementText:
                using (var writer = new StreamWriter(destination))
                    WritePlacements(plate, writer);
                break;
            case PlateExportFormat.Svg:
                Svg.ExportPlate(plate, destination, units);
                break;
            default:
                throw new ArgumentException("Unsupported plate export format.", nameof(format));
        }
    }

    /// <summary>
    /// The placement list: per part, in plate order, "source path|X,Y|degrees", where X,Y is the
    /// drawing's source origin on the plate (the part location less its rotated source offset)
    /// rounded to 8 places. Numbers use the current culture, as the WinForms export always has.
    /// </summary>
    public static void WritePlacements(Plate plate, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(plate);
        ArgumentNullException.ThrowIfNull(writer);
        foreach (var part in plate.Parts)
        {
            var pt = part.BaseDrawing.Source.Offset.Rotate(part.Rotation);
            writer.WriteLine(
                "{0}|{1},{2}|{3}",
                part.BaseDrawing.Source.Path,
                System.Math.Round(part.Location.X - pt.X, 8),
                System.Math.Round(part.Location.Y - pt.Y, 8),
                Angle.ToDegrees(part.Rotation));
        }
    }

    /// <summary>
    /// Export All: exports plates 0..<paramref name="plateCount"/>-1 in order through
    /// <paramref name="exportPlate"/> (which shows the plate, asks for its file and writes it),
    /// stopping at the first that is cancelled or fails. Returns how many were exported.
    /// </summary>
    public static async Task<int> ExportAllAsync(int plateCount, Func<int, Task<bool>> exportPlate)
    {
        ArgumentNullException.ThrowIfNull(exportPlate);
        var exported = 0;
        for (var i = 0; i < plateCount; i++)
        {
            if (!await exportPlate(i))
                break;
            exported++;
        }
        return exported;
    }
}
