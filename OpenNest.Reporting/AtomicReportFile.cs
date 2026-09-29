namespace OpenNest.Reporting;

internal static class AtomicReportFile
{
    internal static void Write(string destination, Action<Stream> render)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(render);
        var target = Path.GetFullPath(destination);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                render(stream);
                stream.Flush(flushToDisk: true);
            }
            // The sibling stays on the same filesystem. Never truncate/delete the target first.
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
