using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using OpenNest.Data;
using OpenNest.Desktop;

namespace OpenNest.Avalonia
{
    /// <summary>
    /// Restores a window's size, position and maximized state from the shared user-settings
    /// file and saves them when it closes, under the same names the WinForms app uses.
    /// </summary>
    /// <remarks>
    /// Sizes are stored in screen pixels. Avalonia sets the client area, while WinForms stores
    /// the outer window, so a size saved by one app opens a frame's width different in the
    /// other; each app's own round trip is exact.
    /// </remarks>
    internal sealed class WindowPlacementBinding
    {
        private readonly Window window;
        private readonly string name;
        private readonly string settingsPath;
        private PixelPoint? normalPosition;
        private (int Width, int Height)? normalSize;

        private WindowPlacementBinding(Window window, string name, string settingsPath)
        {
            this.window = window;
            this.name = name;
            this.settingsPath = settingsPath;
        }

        /// <summary>Call before the window is shown.</summary>
        public static void Remember(Window window, string name, string settingsPath)
        {
            var binding = new WindowPlacementBinding(window, name, settingsPath);
            binding.Restore();
            window.Opened += (_, _) => binding.Track();
            window.PositionChanged += (_, _) => binding.Track();
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == TopLevel.ClientSizeProperty
                    || e.Property == Window.WindowStateProperty)
                    binding.Track();
            };
            window.Closing += (_, _) => binding.Save();
        }

        private void Restore()
        {
            var saved = WindowPlacement.Read(UserSettingsFile.Read(settingsPath), name);
            var screens = window.Screens?.All;
            if (saved == null || screens == null)
                return;

            var areas = screens
                .Select(s => s.WorkingArea)
                .Select(a => new OpenNest.Desktop.PixelRect(a.X, a.Y, a.Width, a.Height))
                .ToList();
            var placement = saved.FitTo(areas);
            var screen = placement.Location is { } at
                ? screens.First(s => s.WorkingArea.Contains(new PixelPoint(at.X, at.Y)))
                : screens.FirstOrDefault(s => s.IsPrimary) ?? screens.FirstOrDefault();

            if (placement.Location is { } location)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(location.X, location.Y);
            }

            if (placement.Size is { } size)
            {
                var scaling = screen?.Scaling ?? 1;
                window.Width = size.Width / scaling;
                window.Height = size.Height / scaling;
            }

            if (placement.Maximized)
                window.WindowState = WindowState.Maximized;
        }

        /// <summary>Remembers the normal bounds, which maximizing keeps.</summary>
        private void Track()
        {
            if (window.WindowState != WindowState.Normal || !window.IsVisible)
                return;

            normalPosition = window.Position;
            var scaling = window.DesktopScaling;
            normalSize = ((int)System.Math.Round(window.ClientSize.Width * scaling),
                (int)System.Math.Round(window.ClientSize.Height * scaling));
        }

        private void Save()
        {
            Track();
            var maximized = window.WindowState == WindowState.Maximized;
            UserSettingsFile.Update(settingsPath, values =>
            {
                // A window never shown normal this session keeps the bounds saved before.
                var previous = WindowPlacement.Read(values, name);
                var placement = new WindowPlacement(
                    normalPosition is { } p ? (p.X, p.Y) : previous?.Location,
                    normalSize ?? previous?.Size,
                    maximized);
                placement.WriteTo(values, name);
            });
        }
    }
}
