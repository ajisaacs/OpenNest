using System.Drawing;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

public class ProgramEditorControlTests
{
    private static readonly Color BaseColor = Color.FromArgb(180, 200, 180);

    [Fact]
    public void TimeoutDuringInitialLoadKeepsCompleteTextAndBaseColorWithoutAParentHandle() => RunSta(() =>
    {
        using var control = new TestEditor { TimeoutFromCall = 1 };
        var status = GetControl<Label>(control, "lblGcode");
        Assert.False(control.IsHandleCreated);
        Assert.False(status.IsHandleCreated);

        control.LoadEntities(CreateEntities());

        Assert.True(control.IsLoaded);
        Assert.False(control.IsDirty);
        Assert.NotNull(control.Program);
        Assert.True(control.SpansCollectedBeforeTimeout > 0);
        AssertCompleteText(control);
        AssertUniformBaseColor(GetControl<RichTextBox>(control, "gcodeEditor"));
        Assert.Equal("G-Code (highlighting timed out)", status.Text);
        Assert.Equal(3, GetControl<EntityView>(control, "preview").Entities.Count);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReverseAndReorderFinishModelPreviewAndEventsWithOrWithoutTimeout(bool timeout) => RunSta(() =>
    {
        using var control = new TestEditor { TimeoutFromCall = timeout ? 2 : int.MaxValue };
        using var form = new Form { Width = 900, Height = 600 };
        control.Dock = DockStyle.Fill;
        form.Controls.Add(control);
        form.Show();
        var status = GetControl<Label>(control, "lblGcode");
        var preview = GetControl<EntityView>(control, "preview");
        var changes = 0;
        Entity[] beforeClick = Array.Empty<Entity>();
        var eventStates = new List<(string Label, bool PreviewChanged)>();
        control.ProgramChanged += (_, _) =>
        {
            changes++;
            // Prove the fallback and preview completed BEFORE the model change was announced.
            eventStates.Add((status.Text, !preview.Entities.SequenceEqual(beforeClick)));
        };

        control.LoadEntities(CreateEntities());
        Assert.Equal(0, changes); // Loading did not raise ProgramChanged before this repair either.
        Assert.False(control.IsDirty);
        AssertCompleteText(control);
        var list = GetControl<ListBox>(control, "contourList");
        list.SelectedIndex = 0;
        var firstContour = Assert.IsType<ContourInfo>(list.Items[0]);
        var oldDirection = firstContour.DirectionLabel;
        var oldProgram = control.Program;
        var oldPreview = preview.Entities.ToArray();

        beforeClick = oldPreview;
        // Overwrite any stale indication so the event snapshot can only show the label
        // applied by THIS operation's highlighting pass.
        status.Text = "<stale>";
        GetControl<Button>(control, "reverseButton").PerformClick();

        Assert.NotSame(oldProgram, control.Program);
        Assert.NotEqual(oldDirection, firstContour.DirectionLabel);
        Assert.True(control.IsDirty);
        Assert.Equal(1, changes);
        AssertHighlightingStateAtEvent(eventStates[0], timeout);
        AssertPreviewRebuilt(preview, oldPreview);
        Assert.Equal(Assert.IsType<Circle>(firstContour.Shape.Entities[0]).Rotation,
            Assert.IsType<Circle>(preview.Entities[0]).Rotation);
        AssertCompleteText(control);
        AssertHighlightingOutcome(control, timeout);

        oldProgram = control.Program;
        oldPreview = preview.Entities.ToArray();
        beforeClick = oldPreview;
        status.Text = "<stale>";
        GetField<ToolStripMenuItem>(control, "menuMoveDown").PerformClick();

        Assert.NotSame(oldProgram, control.Program);
        Assert.Same(firstContour, list.Items[1]);
        Assert.Equal(1, list.SelectedIndex);
        Assert.True(control.IsDirty);
        Assert.Equal(2, changes);
        AssertHighlightingStateAtEvent(eventStates[1], timeout);
        AssertPreviewRebuilt(preview, oldPreview);
        AssertCompleteText(control);
        AssertHighlightingOutcome(control, timeout);
        Assert.Equal(3, control.MatchCalls);
        if (timeout)
            Assert.True(control.SpansCollectedBeforeTimeout > 0);
    });

    [Fact]
    public void SuccessfulRetryAndClearRemoveTheNonmodalTimeoutIndication() => RunSta(() =>
    {
        using var control = new TestEditor { TimeoutFromCall = 1 };
        control.LoadEntities(CreateEntities());
        var status = GetControl<Label>(control, "lblGcode");
        Assert.Contains("timed out", status.Text);

        control.TimeoutFromCall = int.MaxValue;
        control.LoadEntities(CreateEntities());
        Assert.Equal("G-Code", status.Text);
        AssertCompleteText(control);
        var editor = GetControl<RichTextBox>(control, "gcodeEditor");
        editor.Select(0, 3);
        Assert.Equal(Color.FromArgb(200, 140, 220), editor.SelectionColor);

        control.TimeoutFromCall = 1;
        control.LoadEntities(CreateEntities());
        Assert.Contains("timed out", status.Text);
        control.Clear();
        Assert.Equal("G-Code", status.Text);
        Assert.Empty(editor.Text);
        Assert.False(control.IsLoaded);
        Assert.False(control.IsDirty);
        Assert.Null(control.Program);
    });

    [Fact]
    public void OrdinaryHighlightingPreservesAllColorsAndUnmatchedCoordinateText() => RunSta(() =>
    {
        using var control = new TestEditor();
        var editor = GetControl<RichTextBox>(control, "gcodeEditor");
        editor.Text = ";)\nG91\nG00 X1\nG01 X2\nG02 X3\nG03 X4\nG000";
        var original = editor.Text;

        ApplyHighlighting(control);

        Assert.Equal(NormalizeLineEndings(original), NormalizeLineEndings(editor.Text));
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(0, editor.SelectionLength);
        var colors = new[]
        {
            Color.FromArgb(120, 120, 140), Color.FromArgb(200, 140, 220),
            Color.FromArgb(230, 180, 80), Color.FromArgb(130, 200, 140),
            Color.FromArgb(120, 160, 255),
        };
        var spans = ProgramHighlighting.ComputeSpans(original);
        for (var index = 0; index < editor.TextLength; index++)
        {
            if (original[index] is '\r' or '\n')
                continue;
            var expected = BaseColor;
            foreach (var span in spans)
                if (index >= span.Index && index < span.Index + span.Length)
                    expected = colors[span.RuleIndex];
            editor.Select(index, 1);
            Assert.Equal(expected, editor.SelectionColor);
        }
    });

    [Fact]
    public void UnexpectedApplicationFailureStillRestoresSelectionAndResumesLayout() => RunSta(() =>
    {
        using var control = new TestEditor { ReturnInvalidRule = true };
        var editor = GetControl<RichTextBox>(control, "gcodeEditor");
        Assert.Throws<IndexOutOfRangeException>(() => control.LoadEntities(CreateEntities()));
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(0, editor.SelectionLength);
        var layouts = 0;
        editor.Layout += (_, _) => layouts++;
        editor.PerformLayout();
        Assert.True(layouts > 0); // PerformLayout would be deferred if SuspendLayout were unbalanced.
    });

    [Fact]
    public void UnexpectedMatchingFailureIsNotSwallowed() => RunSta(() =>
    {
        using var control = new TestEditor { FailMatching = true };
        var exception = Assert.Throws<InvalidOperationException>(() => control.LoadEntities(CreateEntities()));
        Assert.Equal("Unexpected matching failure", exception.Message);
    });

    private sealed class TestEditor : ProgramEditorControl
    {
        public int TimeoutFromCall { get; set; } = int.MaxValue;
        public int MatchCalls { get; private set; }
        public int SpansCollectedBeforeTimeout { get; private set; }
        public bool ReturnInvalidRule { get; set; }
        public bool FailMatching { get; set; }

        internal override IReadOnlyList<HighlightSpan> ComputeHighlightSpans(string text)
        {
            MatchCalls++;
            if (FailMatching)
                throw new InvalidOperationException("Unexpected matching failure");
            if (MatchCalls >= TimeoutFromCall)
            {
                // Simulate lazy enumeration succeeding for the comment rule, then timing out
                // in a later rule. The local prefix must never reach the color renderer.
                var partial = new List<HighlightSpan>();
                var comments = new Regex(@"^;.*$", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100));
                foreach (Match match in comments.Matches(text))
                    partial.Add(new HighlightSpan(match.Index, match.Length, 0));
                SpansCollectedBeforeTimeout = partial.Count;
                throw new RegexMatchTimeoutException();
            }
            if (ReturnInvalidRule)
                return new[] { new HighlightSpan(0, 3, 99) };
            return base.ComputeHighlightSpans(text);
        }
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n").Replace("\r", "\n");

    private static List<Entity> CreateEntities() => new()
    {
        new Circle(3, 3, 1), new Circle(7, 7, 1), new Circle(10, 10, 10),
    };

    private static void AssertCompleteText(ProgramEditorControl control)
    {
        var contours = GetField<List<ContourInfo>>(control, "contours");
        var raw = Assert.IsType<string>(typeof(ProgramEditorControl)
            .GetMethod("FormatProgram", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { control.Program, contours }));
        // A RichTextBox with a created handle returns native text with LF-normalized line
        // endings; before handle creation it returns the cached string as assigned. Compare
        // against the generated text normalized the same way so the assertion is independent
        // of the editor's handle state.
        var editor = GetControl<RichTextBox>(control, "gcodeEditor");
        Assert.NotEmpty(raw);
        Assert.Equal(NormalizeLineEndings(raw), NormalizeLineEndings(editor.Text));
        Assert.True(editor.ReadOnly);
    }

    private static void AssertHighlightingStateAtEvent(
        (string Label, bool PreviewChanged) state, bool timeout)
    {
        // Captured inside the ProgramChanged handler: highlighting fallback and preview
        // rebuild must already be complete when the model change is announced.
        Assert.Equal(timeout ? "G-Code (highlighting timed out)" : "G-Code", state.Label);
        Assert.True(state.PreviewChanged);
    }

    private static void AssertHighlightingOutcome(ProgramEditorControl control, bool timeout)
    {
        var editor = GetControl<RichTextBox>(control, "gcodeEditor");
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(0, editor.SelectionLength);
        Assert.Equal(timeout ? "G-Code (highlighting timed out)" : "G-Code",
            GetControl<Label>(control, "lblGcode").Text);
        if (timeout)
            AssertUniformBaseColor(editor);
    }

    private static void AssertUniformBaseColor(RichTextBox editor)
    {
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(0, editor.SelectionLength);
        editor.SelectAll();
        Assert.Equal(BaseColor, editor.SelectionColor);
        for (var index = 0; index < editor.TextLength; index++)
        {
            if (editor.Text[index] is '\r' or '\n')
                continue;
            editor.Select(index, 1);
            Assert.Equal(BaseColor, editor.SelectionColor);
        }
        editor.Select(0, 0);
    }

    private static void AssertPreviewRebuilt(EntityView preview, Entity[] before)
    {
        Assert.Equal(before.Length, preview.Entities.Count);
        Assert.All(preview.Entities, entity => Assert.DoesNotContain(entity, before));
    }

    private static T GetControl<T>(Control control, string name) where T : Control =>
        Assert.IsType<T>(control.Controls.Find(name, true).Single());

    private static T GetField<T>(ProgramEditorControl control, string name) =>
        Assert.IsType<T>(typeof(ProgramEditorControl)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control));

    private static void ApplyHighlighting(ProgramEditorControl control) => typeof(ProgramEditorControl)
        .GetMethod("ApplyHighlighting", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null);

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "The STA test did not complete.");
}
