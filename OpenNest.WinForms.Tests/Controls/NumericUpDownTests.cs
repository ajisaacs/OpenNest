using System.Globalization;
using NumericUpDown = OpenNest.Controls.NumericUpDown;

namespace OpenNest.WinForms.Tests.Controls;

public class NumericUpDownTests
{
    [Theory]
    [InlineData("0.5", " in", 0.5)]
    [InlineData("0.5 in", " in", 0.5)]
    [InlineData("0.5in", " in", 0.5)]
    [InlineData("3/8\"", " in", 0.375)]
    [InlineData("1 1/2 in", " in", 1.5)]
    [InlineData("-1 1/2", " in", -1.5)]
    [InlineData("25.4mm", " in", 1)]
    [InlineData("2ft", " in", 24)]
    [InlineData("1'", " in", 12)]
    [InlineData("1 in", " mm", 25.4)]
    [InlineData("2 cm", " mm", 20)]
    [InlineData("4 seconds", " seconds", 4)]
    [InlineData("120", " in/min", 120)]
    public void TryParseInput_ConvertsTypedUnits(string text, string suffix, double expected)
    {
        WithCulture(() =>
        {
            Assert.True(NumericUpDown.TryParseInput(text, suffix, out var value));
            Assert.Equal((decimal)expected, value, 6);
        });
    }

    [Theory]
    [InlineData("abc", " in")]
    [InlineData("5 mm", " seconds")]
    [InlineData("5 parsecs", " in")]
    [InlineData("1/0", " in")]
    public void TryParseInput_RejectsUnknownInput(string text, string suffix)
    {
        WithCulture(() => Assert.False(NumericUpDown.TryParseInput(text, suffix, out _)));
    }

    [Fact]
    public void TypedUnitsAreConverted_AndSuffixIsNotPartOfText()
    {
        StaTestThread.Run(() => WithCulture(() =>
        {
            using var box = new NumericUpDown { DecimalPlaces = 4, Maximum = 100, Suffix = " in" };
            box.CreateControl();
            Assert.Equal("0.0000", box.Text);

            box.Text = "12.7 mm";
            Assert.Equal(0.5m, box.Value);
            Assert.Equal("0.5000", box.Text);

            box.Text = "nonsense";
            Assert.Equal(0.5m, box.Value);
            Assert.Equal("0.5000", box.Text);
        }), TimeSpan.FromSeconds(30), "NumericUpDown test timed out.");
    }

    private static void WithCulture(Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
