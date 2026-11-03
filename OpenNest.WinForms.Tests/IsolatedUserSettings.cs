using System.Runtime.CompilerServices;
using System.Windows.Forms;
using OpenNest.Forms;
using OpenNest.Properties;

namespace OpenNest.WinForms.Tests;

internal static class IsolatedUserSettings
{
#pragma warning disable CA2255 // Must run before anything reads Settings.Default.
    [ModuleInitializer]
    internal static void Redirect()
    {
        AppDataSettingsProvider.PathOverride = Path.Combine(
            Path.GetTempPath(), "OpenNest.WinForms.Tests", Guid.NewGuid().ToString("N"), "user-settings.json");
        // A closing nest window never waits on the unsaved-changes message box.
        EditNestForm.UnsavedChangesPrompt = _ => DialogResult.No;
    }
#pragma warning restore CA2255
}
