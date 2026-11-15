using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest.Desktop;

/// <summary>What exporting a drawing from the drawing library writes.</summary>
public enum LibraryExportFormat
{
    /// <summary>The drawing's program as DXF geometry.</summary>
    Dxf,

    /// <summary>The drawing's program as SVG in the units it was saved in.</summary>
    Svg,

    /// <summary>The revision's package as stored: a .nest holding the one drawing.</summary>
    Nest,
}

/// <summary>One drawing to export and the file it goes to.</summary>
public sealed record LibraryExportTarget(DrawingLibraryRecord Record, string Path);

/// <summary>What a library export wrote, and what it could not.</summary>
public sealed class LibraryExportResult
{
    /// <summary>The files written, in order.</summary>
    public List<string> Written { get; } = new();

    /// <summary>The drawings whose listed revision is no longer on the server.</summary>
    public List<DrawingLibraryRecord> Missing { get; } = new();

    /// <summary>The drawings that could not be read or written, with the reason.</summary>
    public List<(DrawingLibraryRecord Record, string Reason)> Failed { get; } = new();
}

/// <summary>
/// Export from the drawing library for both desktop apps: downloads each listed revision's
/// exact package and writes it as DXF, SVG or the package itself. Nothing on the server or in
/// an open nest changes. The package leaves customer, color and nesting settings to the
/// library record, so a .nest export carries the geometry, units, bends and cut settings only.
/// </summary>
public static class LibraryDrawingExport
{
    /// <summary>The save dialog's file types in order: its 1-based filter index picks the format.</summary>
    public static IReadOnlyList<LibraryExportFormat> DialogFormats { get; } = new[]
    {
        LibraryExportFormat.Dxf,
        LibraryExportFormat.Svg,
        LibraryExportFormat.Nest,
    };

    /// <summary>The WinForms save dialog filter, in <see cref="DialogFormats"/> order.</summary>
    public const string DialogFilter = "DXF (*.dxf)|*.dxf|SVG (*.svg)|*.svg|OpenNest nest (*.nest)|*.nest";

    /// <summary>The format for the save dialog's 1-based <paramref name="filterIndex"/>.</summary>
    public static LibraryExportFormat FromFilterIndex(int filterIndex) =>
        filterIndex >= 1 && filterIndex <= DialogFormats.Count
            ? DialogFormats[filterIndex - 1]
            : throw new ArgumentException("Unsupported drawing export format.", nameof(filterIndex));

    /// <summary>The file extension a format is written with, including the dot.</summary>
    public static string Extension(LibraryExportFormat format) => format switch
    {
        LibraryExportFormat.Dxf => ".dxf",
        LibraryExportFormat.Svg => ".svg",
        LibraryExportFormat.Nest => ".nest",
        _ => throw new ArgumentException("Unsupported drawing export format.", nameof(format)),
    };

    /// <summary>
    /// The file a drawing is suggested to be saved as: its name, made safe for a file name on
    /// every platform ("Drawing" when blank), with the format's extension.
    /// </summary>
    public static string SuggestedFileName(DrawingLibraryRecord record, LibraryExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(record);
        return PlateExport.SafeFileName(record.Name, "Drawing") + Extension(format);
    }

    /// <summary>
    /// The file in <paramref name="folder"/> each drawing is exported to: its suggested name, with
    /// " (2)", " (3)"... added when another drawing in the batch already took it (names compared
    /// ignoring case, as Windows does). Files already in the folder are not considered; callers
    /// ask before overwriting them.
    /// </summary>
    public static IReadOnlyList<LibraryExportTarget> PlanFolder(
        IEnumerable<DrawingLibraryRecord> records, string folder, LibraryExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<LibraryExportTarget>();
        foreach (var record in records)
        {
            var name = SuggestedFileName(record, format);
            var stem = System.IO.Path.GetFileNameWithoutExtension(name);
            var extension = Extension(format);
            for (var copy = 2; !taken.Add(name); copy++)
                name = $"{stem} ({copy}){extension}";
            targets.Add(new LibraryExportTarget(record, System.IO.Path.Combine(folder, name)));
        }
        return targets;
    }

    /// <summary>
    /// Writes one revision's <paramref name="package"/> to <paramref name="destination"/>. The file
    /// is written beside it under a temporary name and then moved over it, so a failure never
    /// leaves a partial file or damages one already there. Throws
    /// <see cref="InvalidDataException"/> for an unreadable package.
    /// </summary>
    public static void Write(byte[] package, string destination, LibraryExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        Extension(format);

        // Reading first refuses a damaged package, even when it would be copied as is.
        var contents = DrawingPackage.Read(package);
        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destination))!;
        var temporary = System.IO.Path.Combine(folder, $".{System.IO.Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            switch (format)
            {
                case LibraryExportFormat.Dxf:
                    Dxf.ExportProgram(contents.Drawing.Program, temporary);
                    break;
                case LibraryExportFormat.Svg:
                    Svg.ExportProgram(contents.Drawing.Program, temporary, contents.Units);
                    break;
                case LibraryExportFormat.Nest:
                    File.WriteAllBytes(temporary, package);
                    break;
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>
    /// Downloads each target's listed revision and writes it in <paramref name="format"/>, in
    /// order. A revision no longer on the server, an unreadable package or a file that cannot be
    /// written is recorded and the rest still export; cancellation stops the batch.
    /// <paramref name="progress"/> receives the number of drawings finished so far.
    /// </summary>
    public static async Task<LibraryExportResult> ExportAsync(
        IDrawingLibrary library,
        IReadOnlyList<LibraryExportTarget> targets,
        LibraryExportFormat format,
        IProgress<int> progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(targets);
        Extension(format);

        var result = new LibraryExportResult();
        for (var i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (record, path) = targets[i];
            try
            {
                var package = await library.GetRevisionPackageAsync(record.Id, record.Revision, cancellationToken);
                if (package == null)
                    result.Missing.Add(record);
                else
                {
                    await Task.Run(() => Write(package, path, format), cancellationToken);
                    result.Written.Add(path);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                && (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException
                    || ex is HttpRequestException || ex is TaskCanceledException))
            {
                // ServerTooOldException is an IOException; InvalidDataException (an unreadable package) is not.
                result.Failed.Add((record, ex.Message));
            }
            progress?.Report(i + 1);
        }
        return result;
    }
}
