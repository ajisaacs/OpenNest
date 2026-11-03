using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

// The title marks unsaved edits and closing asks before discarding them. Shares the undo
// tests' collection, since it drives the same main-window guards.
[Collection("Fill operation lifetime")]
public class UnsavedChangesTests
{
    [Fact]
    public void TheTitleIsMarkedWhileTheNestDiffersFromItsSavedState() => UndoTestEnv.Run(env =>
    {
        var editor = env.Editor;
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal("undo", editor.Text);

        DeleteFirstPlatesParts(editor);
        Assert.True(editor.HasUnsavedChanges);
        Assert.Equal("undo *", editor.Text);

        Assert.True(env.Host.TryUndo(editor));
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal("undo", editor.Text);

        Assert.True(env.Host.TryRedo(editor));
        Assert.Equal("undo *", editor.Text);

        var path = TempNestPath("saved");
        try
        {
            editor.SaveAs(path);
            Assert.False(editor.HasUnsavedChanges);
            Assert.Equal("saved", editor.Text);
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public void AnEditNoScopeRecordedStillCountsAsUnsaved() => UndoTestEnv.Run(env =>
    {
        var editor = env.Editor;
        editor.Nest.Plates[0].Parts.RemoveAt(0);

        Assert.True(editor.HasUnsavedChanges);
    });

    [Fact]
    public void ClosingAnUnchangedNestDoesNotAsk() => UndoTestEnv.Run(env =>
    {
        var asked = 0;
        WithPrompt(env.Editor, () => { asked++; return DialogResult.Cancel; }, () =>
        {
            env.Editor.Close();

            Assert.Equal(0, asked);
            Assert.True(env.Editor.IsDisposed);
        });
    });

    [Fact]
    public void CancelKeepsTheWindowOpenAndNoClosesWithoutSaving() => UndoTestEnv.Run(env =>
    {
        var editor = env.Editor;
        DeleteFirstPlatesParts(editor);
        var answer = DialogResult.Cancel;
        var asked = 0;
        WithPrompt(editor, () => { asked++; return answer; }, () =>
        {
            editor.Close();
            Assert.Equal(1, asked);
            Assert.False(editor.IsDisposed);
            Assert.True(editor.HasUnsavedChanges);

            answer = DialogResult.No;
            editor.Close();
            Assert.Equal(2, asked);
            Assert.True(editor.IsDisposed);
        });
    });

    // Without a main window (whose save follows the machine's storage mode) Yes saves to a file.
    [Fact]
    public void YesSavesToTheNestsFileAndCloses() => StaTestThread.Run(() =>
    {
        using var editor = new EditNestForm(UndoTestEnv.BuildNest());
        editor.PlateView.SetOverlapAutoCheck(null);
        editor.Show();
        var path = TempNestPath("yes");
        try
        {
            editor.SaveAs(path);
            var savedAt = File.GetLastWriteTimeUtc(path);
            DeleteFirstPlatesParts(editor);
            File.SetLastWriteTimeUtc(path, savedAt.AddMinutes(-1));

            WithPrompt(editor, () => DialogResult.Yes, () =>
            {
                editor.Close();

                Assert.True(editor.IsDisposed);
                Assert.True(File.GetLastWriteTimeUtc(path) > savedAt.AddMinutes(-1));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    private static void DeleteFirstPlatesParts(EditNestForm editor)
    {
        editor.PlateManager.LoadAt(0);
        editor.PlateView.SelectAll();
        editor.PlateView.Selection.DeleteSelected();
    }

    private static string TempNestPath(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenNest.WinForms.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name + ".nest");
    }

    // Answers for this test's window only; other windows closing meanwhile keep the default.
    private static void WithPrompt(EditNestForm editor, Func<DialogResult> answer, Action body)
    {
        var previous = EditNestForm.UnsavedChangesPrompt;
        EditNestForm.UnsavedChangesPrompt = form => ReferenceEquals(form, editor) ? answer() : previous(form);
        try
        {
            body();
        }
        finally
        {
            EditNestForm.UnsavedChangesPrompt = previous;
        }
    }
}
