using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace OpenNest.Avalonia
{
    /// <summary>
    /// One number to set, as the WinForms SetValueForm: an "Increment :" box from 0 to a maximum
    /// shown with four decimals, an optional check box that disables the box, and a Set button
    /// that is also Enter. Esc or closing the window changes nothing.
    /// </summary>
    internal sealed class SetValueDialog : Window
    {
        public const double DefaultMaximum = 999999;

        /// <summary>What the user set: the value and the option check box.</summary>
        public sealed record Result(double Value, bool OptionChecked);

        private readonly NumericUpDown value;
        private readonly CheckBox option;
        // The last value the box held, which an emptied box keeps, as the WinForms box does.
        private double lastValid;
        private readonly double maximum;

        private SetValueDialog(
            string title, double initialValue, double maximum, string optionText,
            bool optionChecked)
        {
            Title = title;
            // Wide enough for the option's text on one line, as the WinForms form widens.
            MinWidth = 300;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.maximum = maximum;
            lastValid = System.Math.Clamp(initialValue, 0, maximum);

            value = new NumericUpDown
            {
                Name = "Value",
                Minimum = 0,
                Maximum = (decimal)maximum,
                Increment = 1,
                // A typed value out of range goes to the nearest limit, as in WinForms, rather
                // than being refused.
                ClipValueToMinMax = true,
                FormatString = "0.0000",
                Value = (decimal)lastValid,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            value.ValueChanged += (_, e) =>
            {
                if (e.NewValue is { } v)
                    lastValid = (double)v;
            };
            var set = new Button { Name = "Set", Content = "Set", IsDefault = true };
            set.Click += (_, _) => Close(new Result(Current, option.IsChecked == true));

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 8,
            };
            var label = new TextBlock
            {
                Text = "Increment :",
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(label);
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            Grid.SetColumn(set, 2);
            row.Children.Add(set);

            option = new CheckBox
            {
                Name = "Option",
                Content = optionText,
                IsChecked = optionChecked,
                IsVisible = !string.IsNullOrEmpty(optionText),
            };
            value.IsEnabled = !optionChecked;
            option.IsCheckedChanged += (_, _) => value.IsEnabled = option.IsChecked != true;

            var panel = new StackPanel { Margin = new Thickness(12), Spacing = 10 };
            panel.Children.Add(row);
            panel.Children.Add(option);
            Content = panel;

            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Escape)
                    return;
                e.Handled = true;
                Close(null);
            }, RoutingStrategies.Tunnel);
            Opened += (_, _) => value.Focus();
        }

        // A value out of range is taken to the nearest limit, as the WinForms box does.
        private double Current => System.Math.Clamp(lastValid, 0, maximum);

        /// <summary>
        /// The value and option the user set, or null when they closed the dialog without Set.
        /// </summary>
        public static Task<Result> ShowAsync(
            Window owner, string title, double value, double maximum = DefaultMaximum,
            string optionText = null, bool optionChecked = false) =>
            new SetValueDialog(title, value, maximum, optionText, optionChecked)
                .ShowDialog<Result>(owner);
    }
}
