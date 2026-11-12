using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenNest.Data;
using OpenNest.Desktop.Rendering;

namespace OpenNest.Avalonia
{
    /// <summary>
    /// View &gt; Draw Rapids, Pierce Points, Bounds, Offset, Cut Direction and Support: toggles of the
    /// active nest's plate view, as each WinForms nest window keeps its own. Rapids and bounds are
    /// read from the shared user settings when a nest opens and written back when it closes.
    /// </summary>
    public partial class MainWindow
    {
        private void RestoreViewToggles(NestView view) =>
            view.PlateView.Options = snapshotMode
                ? new PlateViewOptions()
                : PlateViewOptions.Read(UserSettingsFile.Read(UserSettingsFile.DefaultPath));

        // A snapshot must not depend on the user's settings.
        private void UseDefaultViewToggles()
        {
            foreach (var view in views.Values)
            {
                view.PlateView.Options = new PlateViewOptions();
                view.PlateView.InvalidateVisual();
            }
            UpdateViewToggles();
        }

        private void SaveViewToggles(NestView view)
        {
            if (snapshotMode)
                return;
            UserSettingsFile.Update(UserSettingsFile.DefaultPath, view.PlateView.Options.WriteTo);
        }

        // As the WinForms nest windows closing in turn: the last one's toggles are kept.
        private void SaveAllViewToggles()
        {
            foreach (var tab in tabs.Tabs)
            {
                if (views.TryGetValue(tab, out var view))
                    SaveViewToggles(view);
            }
        }

        private void UpdateViewToggles()
        {
            var options = ActiveView?.PlateView.Options;
            foreach (var (item, value) in new (MenuItem, Func<PlateViewOptions, bool>)[]
            {
                (DrawRapidsMenuItem, o => o.DrawRapid),
                (DrawPiercePointsMenuItem, o => o.DrawPiercePoints),
                (DrawBoundsMenuItem, o => o.DrawBounds),
                (DrawOffsetMenuItem, o => o.DrawOffset),
                (DrawCutDirectionMenuItem, o => o.DrawCutDirection),
                (DrawSupportMenuItem, o => o.DrawSupport),
            })
            {
                item.IsEnabled = options != null;
                item.IsChecked = options != null && value(options);
            }
        }

        private void Toggle(Action<PlateViewOptions> toggle)
        {
            if (ActiveView is { } view)
            {
                toggle(view.PlateView.Options);
                view.PlateView.InvalidateVisual();
            }
            UpdateViewToggles();
        }

        private void OnToggleDrawRapids(object sender, RoutedEventArgs e) => Toggle(o => o.DrawRapid = !o.DrawRapid);

        private void OnToggleDrawPiercePoints(object sender, RoutedEventArgs e) =>
            Toggle(o => o.DrawPiercePoints = !o.DrawPiercePoints);

        private void OnToggleDrawBounds(object sender, RoutedEventArgs e) => Toggle(o => o.DrawBounds = !o.DrawBounds);

        private void OnToggleDrawOffset(object sender, RoutedEventArgs e) => Toggle(o => o.DrawOffset = !o.DrawOffset);

        private void OnToggleDrawCutDirection(object sender, RoutedEventArgs e) =>
            Toggle(o => o.DrawCutDirection = !o.DrawCutDirection);

        private void OnToggleDrawSupport(object sender, RoutedEventArgs e) =>
            Toggle(o => o.DrawSupport = !o.DrawSupport);
    }
}
