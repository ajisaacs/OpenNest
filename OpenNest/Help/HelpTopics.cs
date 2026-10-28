using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace OpenNest.Help
{
    /// <summary>
    /// Opens the bundled help (built from the ajisaacs/opennest-help repository) at a topic.
    /// Topic IDs are a stable contract: the help's <c>topics.json</c> maps each ID to a page,
    /// so pages can move without app changes. Never rename an ID the app uses.
    /// </summary>
    public static class HelpTopics
    {
        public const string Home = "home";
        public const string FirstNest = "first-nest";
        public const string ImportDrawings = "import-drawings";
        public const string AutoNest = "auto-nest";
        public const string Post = "post";
        public const string SupportGrid = "support-grid";

        /// <summary>Overrides the help folder, e.g. a local opennest-help <c>site/</c> build.</summary>
        public const string DirectoryVariable = "OPENNEST_HELP_DIR";

        public static string Directory
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable(DirectoryVariable);
                return string.IsNullOrWhiteSpace(overridden)
                    ? Path.Combine(Application.StartupPath, "Help")
                    : overridden;
            }
        }

        public static bool IsInstalled => File.Exists(Path.Combine(Directory, "index.html"));

        /// <summary>Opens <paramref name="topic"/> when F1 is pressed anywhere in <paramref name="form"/>.</summary>
        public static void Attach(Form form, string topic)
        {
            form.HelpRequested += (_, e) =>
            {
                e.Handled = true;
                Show(form, topic);
            };
        }

        public static void Show(IWin32Window owner, string topic)
        {
            if (!IsInstalled)
            {
                MessageBox.Show(owner,
                    $"Help is not installed. Expected it in:\n{Directory}",
                    "OpenNest Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            HelpForm.ShowPage(ResolvePage(Directory, topic));
        }

        /// <summary>Maps a topic ID to a site-relative page, falling back to the home page.</summary>
        public static string ResolvePage(string helpDirectory, string topic)
        {
            var topics = LoadTopics(helpDirectory);
            if (topics.TryGetValue(topic, out var page))
                return page;
            return topics.TryGetValue(Home, out var home) ? home : "index.html";
        }

        private static Dictionary<string, string> LoadTopics(string helpDirectory)
        {
            var path = Path.Combine(helpDirectory, "topics.json");
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var topics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in document.RootElement.GetProperty("topics").EnumerateObject())
                    topics[property.Name] = property.Value.GetString();
                return topics;
            }
            catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException
                                           or InvalidOperationException or UnauthorizedAccessException)
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
