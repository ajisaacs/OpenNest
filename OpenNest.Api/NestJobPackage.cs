using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Remote;
using OpenNest.IO;

namespace OpenNest.Api;

/// <summary>
/// A whole-job nest request as one upload for the nest server: a ZIP holding
/// <c>job.json</c> (<see cref="NestJobDocument"/>) and <c>drawings.nest</c>, a nest archive of
/// exactly the items' drawings in the order the document names them. Drawings travel as the
/// nest file format stores them, so the server nests what the client has, edits included.
/// </summary>
public static class NestJobPackage
{
    public const string JobEntry = "job.json";
    public const string DrawingsEntry = "drawings.nest";

    public static byte[] Write(NestPipelineRequest request, Units units = Units.Inches)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Items);
        var drawings = new List<Drawing>();
        foreach (var item in request.Items)
        {
            if (item?.Drawing != null && !drawings.Any(d => ReferenceEquals(d, item.Drawing)))
                drawings.Add(item.Drawing);
        }
        var document = NestJobDocument.From(request, drawings);

        // A throwaway nest holds the drawings only; the caller's nest is never written.
        var nest = new Nest { Units = units };
        foreach (var drawing in drawings)
            nest.Drawings.Add(drawing);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry(JobEntry).Open(), new UTF8Encoding(false)))
                writer.Write(document.ToJson());
            using var archive = zip.CreateEntry(DrawingsEntry, CompressionLevel.NoCompression).Open();
            new NestWriter(nest).Write(archive);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// The package's <c>job.json</c> alone, without reading its drawings: a desktop reviewing the
    /// job in a later session binds it to its own drawings instead
    /// (<see cref="RemoteNestSubmission.Reopen"/>). Throws <see cref="FormatException"/> when it is not a package.
    /// </summary>
    public static NestJobDocument ReadDocument(byte[] package)
    {
        ArgumentNullException.ThrowIfNull(package);
        try
        {
            using var zip = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
            var jobEntry = zip.GetEntry(JobEntry) ?? throw new FormatException($"The nest job has no {JobEntry}.");
            using var reader = new StreamReader(jobEntry.Open(), Encoding.UTF8);
            return NestJobDocument.FromJson(reader.ReadToEnd());
        }
        catch (InvalidDataException ex)
        {
            throw new FormatException("The nest job is not a readable ZIP package.", ex);
        }
    }

    /// <summary>Reads a package back into a request; throws <see cref="FormatException"/> when it is not one.</summary>
    public static NestPipelineRequest Read(byte[] package)
    {
        ArgumentNullException.ThrowIfNull(package);
        try
        {
            using var zip = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
            var jobEntry = zip.GetEntry(JobEntry) ?? throw new FormatException($"The nest job has no {JobEntry}.");
            var drawingsEntry = zip.GetEntry(DrawingsEntry) ?? throw new FormatException($"The nest job has no {DrawingsEntry}.");

            string json;
            using (var reader = new StreamReader(jobEntry.Open(), Encoding.UTF8))
                json = reader.ReadToEnd();
            var document = NestJobDocument.FromJson(json);

            // NestReader needs a seekable stream.
            using var archive = new MemoryStream();
            using (var entry = drawingsEntry.Open())
                entry.CopyTo(archive);
            archive.Position = 0;
            var nest = new NestReader(archive).Read();
            return document.ToRequest(nest.Drawings.ToList());
        }
        catch (InvalidDataException ex)
        {
            throw new FormatException("The nest job is not a readable ZIP package.", ex);
        }
    }
}
