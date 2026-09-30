using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OpenNest.WinForms.Tests;

public class ArchUnitsTests
{
    [Fact]
    public void ArchitecturalRegexHasExplicitTimeout()
    {
        var regex = Assert.IsType<Regex>(typeof(ArchUnits)
            .GetField("UnitRegex", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        Assert.Equal(TimeSpan.FromMilliseconds(250), regex.MatchTimeout);
        Assert.Equal(RegexOptions.None, regex.Options);
    }

    [Theory]
    [InlineData("5' 6.5\"", 66.5)]
    [InlineData("2 feet 3 1/2 inches", 27.5)]
    [InlineData("1-1/2\"", 1.5)]
    [InlineData("48", 48)]
    [InlineData("", 0)]
    public void OrdinaryArchitecturalInputsKeepTheirMeaning(string input, double expected)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, ArchUnits.ParseToInches(input));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void InvalidFieldIsRedAndNaNAndCorrectionRestoresIt() => RunSta(() =>
    {
        using var field = new TextBox { Text = "invalid dimension" };
        Assert.True(double.IsNaN(ArchUnits.GetLengthInches(field)));
        Assert.Equal(Color.Red, field.ForeColor);
        field.Text = "12\"";
        Assert.Equal(12, ArchUnits.GetLengthInches(field));
        Assert.Equal(SystemColors.WindowText, field.ForeColor);
    });

    // Actual timeout execution is covered by the shared Regex mechanism in FractionTests;
    // these desktop tests deliberately avoid timing-sensitive oversized-input fixtures.
    internal static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
