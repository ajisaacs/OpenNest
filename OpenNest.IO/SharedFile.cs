using System.IO;

namespace OpenNest.IO
{
    /// <summary>
    /// Opens source drawings read-only without blocking, or being blocked by, the program
    /// that has them open: AutoCAD keeps an open DWG/DXF locked for writing, and a reader
    /// that denies write sharing (the <see cref="File.OpenRead"/> default) fails on it.
    /// </summary>
    internal static class SharedFile
    {
        public static FileStream OpenRead(string path) =>
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
    }
}
