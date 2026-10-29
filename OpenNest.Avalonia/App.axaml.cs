using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace OpenNest.Avalonia
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new MainWindow();
                desktop.MainWindow = window;
                var args = desktop.Args ?? System.Array.Empty<string>();
                if (args.Length > 0)
                    window.OpenNest(args[0]);
                // --auto-nest: run Auto Nest on the opened nest (with --snapshot, before rendering).
                if (System.Array.IndexOf(args, "--auto-nest") >= 0)
                    window.AutoNestOnOpen();
                // --snapshot <png> [plate]: render the window to an image (status line in <png>.txt) and exit.
                var snapshot = System.Array.IndexOf(args, "--snapshot");
                if (snapshot >= 0 && snapshot + 1 < args.Length)
                    window.SaveSnapshotAndClose(args[snapshot + 1],
                        snapshot + 2 < args.Length && int.TryParse(args[snapshot + 2], out var plate) ? plate : 1);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
