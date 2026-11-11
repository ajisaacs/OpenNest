using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenNest.Avalonia
{
    /// <summary>
    /// A message with a row of buttons ending in Cancel (OK for a plain message). That last
    /// button is the default and Esc, so a stray Enter never discards work, and closing the
    /// dialog counts as it.
    /// </summary>
    internal sealed class ConfirmDialog : Window
    {
        private ConfirmDialog(string title, string message, string[] choices, string cancelText)
        {
            Title = title;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
            };
            for (var i = 0; i < choices.Length; i++)
            {
                var index = i;
                var choice = new Button { Content = choices[i] };
                choice.Click += (_, _) => Close(index);
                buttons.Children.Add(choice);
            }
            var cancel = new Button { Content = cancelText, IsDefault = true, IsCancel = true };
            cancel.Click += (_, _) => Close(null);
            buttons.Children.Add(cancel);

            var panel = new StackPanel { Margin = new Thickness(18), Spacing = 18 };
            // A long message (many load warnings) scrolls rather than pushing the buttons away.
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            });
            panel.Children.Add(buttons);
            Content = panel;
            Opened += (_, _) => cancel.Focus();
        }

        /// <summary>True when the user chose <paramref name="confirmText"/>.</summary>
        public static async Task<bool> AskAsync(
            Window owner, string title, string message, string confirmText) =>
            await ChooseAsync(owner, title, message, confirmText) == 0;

        /// <summary>
        /// The index of the choice the user clicked, or -1 for Cancel (also Esc, Enter and
        /// closing the dialog).
        /// </summary>
        public static async Task<int> ChooseAsync(
            Window owner, string title, string message, params string[] choices) =>
            await new ConfirmDialog(title, message, choices, "Cancel")
                .ShowDialog<int?>(owner) ?? -1;

        /// <summary>A message with only an OK button.</summary>
        public static Task ShowAsync(Window owner, string title, string message) =>
            new ConfirmDialog(title, message, System.Array.Empty<string>(), "OK")
                .ShowDialog<int?>(owner);
    }
}
