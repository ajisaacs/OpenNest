using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenNest.Data;
using OpenNest.Reporting;

namespace OpenNest.Desktop;

/// <summary>
/// View Nest Report for both desktop apps: the checks before a report is written, the report
/// file name, capturing and writing the PDF, and the private temporary folders a viewed report is
/// rendered into. Whether the nest is busy (nesting, a fill
/// or plate action) is the front end's to say; see docs/nest-reports.md.
/// </summary>
public static class NestReportExport
{
    /// <summary>How long a viewed report's temporary folder is kept before a later view removes it.</summary>
    public static readonly TimeSpan ViewedReportLifetime = TimeSpan.FromDays(1);

    /// <summary>
    /// The report's file name, "&lt;nest name&gt;.report.pdf". Throws
    /// <see cref="InvalidOperationException"/> when there is no nest, or its name is blank or
    /// holds a character a file name cannot, so the user renames it before any dialog opens.
    /// </summary>
    public static string ReportFileName(Nest nest)
    {
        if (nest == null)
            throw new InvalidOperationException("No nest is available to report.");
        if (string.IsNullOrWhiteSpace(nest.Name) || nest.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException(
                $"The nest name '{nest.Name}' cannot be used as a report file name. Rename the nest first.");
        return $"{nest.Name}.report.pdf";
    }

    /// <summary>
    /// The report of <paramref name="nest"/>, captured synchronously while the caller keeps the
    /// nest stable. The nest material's density comes from <paramref name="machine"/> (the nest's
    /// machine, may be null) and the <paramref name="materials"/> catalog when the material has
    /// none, and the thickness's name ("16 ga") from the machine's cutting defaults; with
    /// <paramref name="cutParameters"/> each plate gets an estimated cut time.
    /// Capturing reads the nest only: no quantity, selection or dirty-state change.
    /// </summary>
    public static NestReportSnapshot Capture(
        Nest nest,
        DateTimeOffset generatedAt,
        MachineConfig machine,
        IEnumerable<MaterialConfig> materials,
        CutParameters cutParameters)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var material = nest.Material;
        var catalogDensity = MaterialDensityLookup.Resolve(material?.Name, material?.Grade, machine, materials);
        return NestReportBuilder.Capture(nest, generatedAt, cutParameters, catalogDensity) with
        {
            ThicknessName = MachineThicknessName(machine, nest),
        };
    }

    /// <summary>The name the machine config gives the nest's thickness ("16 ga"), or empty.</summary>
    public static string MachineThicknessName(MachineConfig machine, Nest nest) =>
        MachineCuttingDefaults.Find(machine, nest)?.Name?.Trim() ?? "";

    /// <summary>
    /// The report captured as the desktop apps do, from machines already read: the nest's
    /// machine (by id) and every machine's materials from <paramref name="machines"/>, and the
    /// cut-time settings last used in Calculate Cut Time or the simulator
    /// (<paramref name="userSettings"/>, the shared user settings) for the estimated cut times.
    /// </summary>
    public static NestReportSnapshot Capture(
        Nest nest,
        DateTimeOffset generatedAt,
        IEnumerable<MachineConfig> machines,
        IReadOnlyDictionary<string, string> userSettings)
    {
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentNullException.ThrowIfNull(userSettings);
        var known = machines.Where(m => m != null).ToList();
        var saved = SavedCutTimeSettings.Read(userSettings);
        var cutParameters = saved.ToCutParameters(nest.Units, SavedCutTimeSettings.ChooseMachine(known, saved.Machine));
        var machine = nest.Machine == null ? null : known.FirstOrDefault(m => m.Id == nest.Machine.Id);
        var materials = known.SelectMany(m => m.Materials ?? new List<MaterialConfig>());
        return Capture(nest, generatedAt, machine, materials, cutParameters);
    }

    /// <summary>
    /// Writes a captured report to <paramref name="destination"/>. The writer replaces an
    /// existing destination only once the new report is complete.
    /// </summary>
    public static void Write(NestReportSnapshot snapshot, string destination) =>
        NestPdfWriter.Write(snapshot, destination);

    /// <summary>The folder viewed reports are rendered under: OpenNest/Reports in the temp folder.</summary>
    public static string ViewedReportsRoot => Path.Combine(Path.GetTempPath(), "OpenNest", "Reports");

    /// <summary>
    /// A new private folder under <paramref name="root"/> (default <see cref="ViewedReportsRoot"/>)
    /// for one viewed report, created, and the report's path in it.
    /// </summary>
    public static string CreateViewPath(string reportFileName, string root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportFileName);
        var folder = Path.Combine(root ?? ViewedReportsRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, reportFileName);
    }

    /// <summary>Deletes a viewed report's folder. Best effort: a leftover temp report is harmless.</summary>
    public static void DeleteReportFolder(string pdfPath)
    {
        try
        {
            var folder = Path.GetDirectoryName(pdfPath);
            if (folder != null && Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Deletes the viewed-report folders under <paramref name="root"/> (default
    /// <see cref="ViewedReportsRoot"/>) last written before <paramref name="before"/>. A report
    /// opened in the system's PDF viewer cannot be deleted when its viewer closes, so each view
    /// clears those older than <see cref="ViewedReportLifetime"/>. Best effort; returns how many
    /// folders were removed.
    /// </summary>
    public static int DeleteViewedReportsBefore(DateTime before, string root = null)
    {
        root ??= ViewedReportsRoot;
        var removed = 0;
        try
        {
            if (!Directory.Exists(root))
                return 0;
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(folder) >= before.ToUniversalTime())
                        continue;
                    Directory.Delete(folder, true);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return removed;
    }
}
