using System.ComponentModel;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.PostSettings;

namespace OpenNest.WinForms.Tests.Controls;

public class PostSettingsEditorTests
{
    [Fact]
    public void UnchangedEditor_AppliesTheSameValues()
    {
        Run(() =>
        {
            var config = new SampleConfig();
            using var editor = NewEditor(config);

            Assert.True(editor.TryApply(out var error), error);

            Assert.Null(config.Threshold);
            Assert.Equal(2000, config.Raise);
            var range = Assert.Single(config.Ranges);
            Assert.Equal("Steel", range.Name);
            Assert.Equal(0.125, range.Size);
            Assert.Equal(7, range.Code);
            Assert.Equal("STEEL", config.Selected);
        });
    }

    [Fact]
    public void ClearingAnOptionalNumber_AppliesNull_AndCheckingItAppliesTheBox()
    {
        Run(() =>
        {
            var config = new SampleConfig();
            using var editor = NewEditor(config);

            Find<CheckBox>(editor, "Raise enabled").Checked = false;
            Find<CheckBox>(editor, "Threshold enabled").Checked = true;

            Assert.True(editor.TryApply(out var error), error);
            Assert.Null(config.Raise);
            Assert.Equal(0, config.Threshold);
        });
    }

    [Fact]
    public void MalformedTableCell_LeavesTheConfigUnchanged()
    {
        Run(() =>
        {
            var config = new SampleConfig();
            using var editor = NewEditor(config);
            var grid = All<DataGridView>(editor).Single();

            grid.Rows[0].Cells[1].Value = "wide";
            Find<CheckBox>(editor, "Raise enabled").Checked = false;

            Assert.False(editor.TryApply(out var error));
            Assert.Contains("Row 1", error);
            Assert.Equal(2000, config.Raise);
            Assert.Equal(0.125, Assert.Single(config.Ranges).Size);
        });
    }

    [Fact]
    public void Suggestions_ListTheTablesCurrentRows()
    {
        Run(() =>
        {
            var config = new SampleConfig();
            using var editor = NewEditor(config);
            var grid = All<DataGridView>(editor).Single();
            grid.Rows.Add("Aluminum", "0.25", "9", "AL250");

            var combo = All<ComboBox>(editor).Single(c => c.DropDownStyle == ComboBoxStyle.DropDown);
            combo.DroppedDown = true;
            combo.DroppedDown = false;

            Assert.Equal(new object[] { "", "AL250", "STEEL" }, combo.Items.Cast<object>());
            Assert.Equal("STEEL", combo.Text);
        });
    }

    private static PostSettingsEditor NewEditor(SampleConfig config)
    {
        var editor = new PostSettingsEditor(config, PostSettingsLayout.TryBuild(typeof(SampleConfig)))
        {
            Size = new System.Drawing.Size(700, 500),
        };
        editor.CreateControl();
        return editor;
    }

    private static T Find<T>(Control root, string accessibleName) where T : Control =>
        All<T>(root).Single(c => c.AccessibleName == accessibleName);

    private static IEnumerable<T> All<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;
            foreach (var nested in All<T>(child))
                yield return nested;
        }
    }

    private static void Run(Action body) =>
        StaTestThread.Run(body, TimeSpan.FromSeconds(30), "PostSettingsEditor test timed out.");

    public sealed class SampleConfig
    {
        [Description("Optional decimal.")]
        [PostSetting("Main", 0, Minimum = 0, Maximum = 100)]
        public double? Threshold { get; set; }

        [Description("Optional integer.")]
        [PostSetting("Main", 1, Minimum = 0, Maximum = 100000)]
        public int? Raise { get; set; } = 2000;

        [Description("Rows.")]
        [PostSetting("Main", 2)]
        public List<SampleRow> Ranges { get; set; } =
            new() { new() { Name = "Steel", Size = 0.125, Code = 7, Library = "STEEL" } };

        [Description("Picks a row.")]
        [PostSetting("Main", 3, SuggestionsTable = nameof(Ranges), SuggestionsColumn = nameof(SampleRow.Library))]
        public string Selected { get; set; } = "STEEL";
    }

    public sealed class SampleRow
    {
        public string Name { get; set; } = "";
        public double Size { get; set; }
        public int Code { get; set; }
        public string Library { get; set; } = "";
    }
}
