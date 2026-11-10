using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenNest.Avalonia
{
    /// <summary>
    /// A two-button question. Cancel is the default and Esc, so a stray Enter never discards work.
    /// </summary>
    internal sealed class ConfirmDialog : Window
    {
        private ConfirmDialog(string title, string message, string confirmText)
        {
            Title = title;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var confirm = new Button { Content = confirmText };
            var cancel = new Button { Content = "Cancel", IsDefault = true, IsCancel = true };
            confirm.Click += (_, _) => Close(true);
            cancel.Click += (_, _) => Close(false);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
            };
            buttons.Children.Add(confirm);
            buttons.Children.Add(cancel);

            var panel = new StackPanel { Margin = new Thickness(18), Spacing = 18 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(buttons);
            Content = panel;
            Opened += (_, _) => cancel.Focus();
        }

        /// <summary>True when the user chose <paramref name="confirmText"/>.</summary>
        public static Task<bool> AskAsync(
            Window owner, string title, string message, string confirmText) =>
            new ConfirmDialog(title, message, confirmText).ShowDialog<bool>(owner);
    }
}
