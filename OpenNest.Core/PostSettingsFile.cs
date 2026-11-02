using System;
using System.IO;
using System.Reflection;

namespace OpenNest
{
    /// <summary>
    /// Where a post keeps its own settings: %APPDATA%\OpenNest\Posts\&lt;post assembly&gt;.json.
    /// The install folder holds only the shipped defaults, because an update replaces it and
    /// would silently reset every edit saved there. The shipped JSON is read until the first save.
    /// </summary>
    public static class PostSettingsFile
    {
        /// <summary>Replaces <see cref="Directory"/>, so test runs never read or write the user's settings.</summary>
        public static string DirectoryOverride { get; set; }

        public static string Directory => DirectoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenNest", "Posts");

        public static string PathFor(Assembly post) =>
            Path.Combine(Directory, Path.GetFileNameWithoutExtension(post.Location) + ".json");

        /// <summary>The defaults deployed beside the post's assembly.</summary>
        public static string ShippedPathFor(Assembly post) => Path.ChangeExtension(post.Location, ".json");

        /// <summary>The saved settings, else the shipped defaults, else null.</summary>
        public static string Read(Assembly post)
        {
            var saved = PathFor(post);
            if (File.Exists(saved))
                return File.ReadAllText(saved);
            var shipped = ShippedPathFor(post);
            return File.Exists(shipped) ? File.ReadAllText(shipped) : null;
        }

        /// <summary>
        /// Saves through a temporary file, so a failed write leaves the previous settings intact.
        /// </summary>
        public static void Write(Assembly post, string json)
        {
            var path = PathFor(post);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
    }
}
