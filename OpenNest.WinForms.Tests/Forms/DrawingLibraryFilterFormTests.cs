using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Forms;
using static OpenNest.WinForms.Tests.TestReflection;

namespace OpenNest.WinForms.Tests.Forms;

public class DrawingLibraryFilterFormTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void ReadsGaugesFractionsAndCounts_AsAnInchFilter()
    {
        StaTestThread.Run(
            () =>
            {
                using var form = new DrawingLibraryFilterForm(DrawingLibraryFilter.None, new[] { "Synthetic Co" });
                Field<ComboBox>(form, "customerBox").Text = " Synthetic Co ";
                Field<ThicknessComboBox>(form, "minThicknessBox").Text = "16 ga";
                Field<ThicknessComboBox>(form, "maxThicknessBox").Text = "3/16";
                Field<CheckBox>(form, "blankThicknessCheckBox").Checked = false;
                Field<TextBox>(form, "maxLengthBox").Text = "4\"";
                Field<TextBox>(form, "minWidthBox").Text = "1-1/2";
                Field<TextBox>(form, "minHolesBox").Text = "2";
                var from = Field<DateTimePicker>(form, "savedFromPicker");
                from.Value = new DateTime(2026, 1, 2);
                from.Checked = true;

                Assert.True(form.TryReadFilter(out var filter, out var error), error);

                Assert.Equal(new DrawingLibraryFilter
                {
                    Customer = "Synthetic Co",
                    MinThickness = 0.0598,
                    MaxThickness = 0.1875,
                    IncludeBlankThickness = false,
                    MaxLength = 4,
                    MinWidth = 1.5,
                    MinHoles = 2,
                    SavedFrom = new DateOnly(2026, 1, 2),
                }, filter);
            },
            TestTimeout,
            "The filter dialog read test did not complete.");
    }

    [Theory]
    [InlineData("minThicknessBox", "thick")]
    [InlineData("maxLengthBox", "16 ga")]
    [InlineData("maxLengthBox", "-1")]
    [InlineData("minHolesBox", "1.5")]
    public void UnreadableBox_KeepsTheDialogOpenWithTheReason(string box, string text)
    {
        StaTestThread.Run(
            () =>
            {
                using var form = new DrawingLibraryFilterForm(DrawingLibraryFilter.None);
                form.Show();
                ((Control)typeof(DrawingLibraryFilterForm).GetField(box, System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!).Text = text;

                Field<Button>(form, "okButton").PerformClick();

                Assert.Equal(DialogResult.None, form.DialogResult);
                Assert.NotEqual("", Field<Label>(form, "errorLabel").Text);
                Assert.Same(DrawingLibraryFilter.None, form.Filter);
            },
            TestTimeout,
            "The filter dialog refusal test did not complete.");
    }

    [Fact]
    public void ReversedRange_IsRefused_AndClearEmptiesEveryBox()
    {
        StaTestThread.Run(
            () =>
            {
                var start = new DrawingLibraryFilter { MinHoles = 3, MaxLength = 4, SavedTo = new DateOnly(2026, 5, 6) };
                using var form = new DrawingLibraryFilterForm(start);
                form.Show();
                Assert.Equal("4", Field<TextBox>(form, "maxLengthBox").Text);
                Assert.True(Field<DateTimePicker>(form, "savedToPicker").Checked);
                Field<TextBox>(form, "maxHolesBox").Text = "2";

                Field<Button>(form, "okButton").PerformClick();
                Assert.Equal(DialogResult.None, form.DialogResult);

                Field<Button>(form, "clearButton").PerformClick();
                Field<Button>(form, "okButton").PerformClick();

                Assert.Equal(DialogResult.OK, form.DialogResult);
                Assert.True(form.Filter.IsEmpty);
            },
            TestTimeout,
            "The filter dialog range test did not complete.");
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("4", 4.0)]
    [InlineData(" 3/16 in ", 0.1875)]
    [InlineData("2-1/2\"", 2.5)]
    [InlineData("#16", double.NaN)]
    [InlineData("abc", double.NaN)]
    public void TryParseLength_ReadsInchesAndRefusesGauges(string text, double? expected)
    {
        var read = DrawingLibraryFilterForm.TryParseLength(text, out var value);

        if (expected is double number && double.IsNaN(number))
        {
            Assert.False(read);
            return;
        }
        Assert.True(read);
        Assert.Equal(expected, value);
    }
}
