using OpenNest.IO;

namespace OpenNest.Desktop.Tests;

public sealed class NestTabsSaveTests : IDisposable
{
    private readonly string directory =
        Directory.CreateTempSubdirectory("opennest-desktop-tabs-save-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string InDir(string name) => Path.Combine(directory, name);

    // Opens a real nest file in a new tab, as File > Open does.
    private NestTab OpenSaved(NestTabs tabs, string name)
    {
        var path = InDir(name);
        var nest = TestNests.OneSquare();
        nest.Name = Path.GetFileNameWithoutExtension(name);
        Assert.True(new NestWriter(nest).Write(path));
        return tabs.Open(path, file => new Document { Nest = new NestReader(file).Read() });
    }

    [Fact]
    public void SavePath_IsNullForANestOnlyOpened_SoTheFirstSaveAsks()
    {
        var tabs = new NestTabs();

        var tab = OpenSaved(tabs, "opened.nest");

        Assert.Null(tab.SavePath);
    }

    [Fact]
    public void SaveAs_WritesTheFileAndMakesItTheTabsFileNameAndSavePath()
    {
        var tabs = new NestTabs();
        var tab = OpenSaved(tabs, "opened.nest");
        TestNests.Edit(tab);
        var changed = 0;
        tabs.Changed += (_, _) => changed++;
        var target = InDir("Renamed Job.nest");

        tabs.SaveAs(tab, target);

        Assert.Equal(target, tab.Path);
        Assert.Equal(target, tab.SavePath);
        Assert.Equal("Renamed Job", tab.Name);
        Assert.False(tab.Modified);
        Assert.Equal(1, changed);
        var read = new NestReader(target).Read();
        Assert.Equal("Renamed Job", read.Name);
        Assert.Single(read.Plates[0].Parts);
    }

    [Fact]
    public void SaveAs_ThenOpeningTheSavedFile_SwitchesToTheTabInsteadOfOpeningItTwice()
    {
        var tabs = new NestTabs();
        var tab = OpenSaved(tabs, "opened.nest");
        OpenSaved(tabs, "other.nest");
        var target = InDir("saved.nest");
        tabs.SaveAs(tab, target);

        var again = tabs.Open(target, _ => throw new InvalidOperationException("loaded again"));

        Assert.Same(tab, again);
        Assert.Equal(2, tabs.Tabs.Count);
    }

    [Fact]
    public void SaveAs_AFileAnotherTabShows_IsRefusedAndWritesNothing()
    {
        var tabs = new NestTabs();
        var a = OpenSaved(tabs, "a.nest");
        var b = OpenSaved(tabs, "b.nest");
        TestNests.Edit(a);
        var before = File.ReadAllBytes(b.Path);

        var ex = Assert.Throws<SaveTargetRefusedException>(() => tabs.SaveAs(a, b.Path));

        Assert.Contains("b.nest is open in the b tab", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(b.Path));
        Assert.Equal(InDir("a.nest"), a.Path);
        Assert.Equal("a", a.Name);
        Assert.True(a.Modified);
    }

    [Fact]
    public void AnOpenedNest_IsUnmodifiedWithItsEmptyPlateToNestOnto()
    {
        var tabs = new NestTabs();

        var tab = OpenSaved(tabs, "opened.nest");

        // The file has one plate; the empty one kept at the end is part of the opened state.
        Assert.Equal(2, tab.Document.Nest.Plates.Count);
        Assert.False(tab.Modified);
        Assert.False(tab.Undo.History.CanUndo);
    }

    [Fact]
    public void Modified_ClearsWhenTheEditsAreUndoneBackToTheSavedState()
    {
        var tabs = new NestTabs();
        var tab = OpenSaved(tabs, "undo.nest");
        tab.Undo.RecordChange("Move", () => tab.Document.Nest.Plates[0].Parts[0].Offset(4, 0));
        Assert.True(tab.Modified);

        tab.Undo.Undo();

        Assert.False(tab.Modified);
        tab.Undo.Redo();
        Assert.True(tab.Modified);
    }

    [Fact]
    public void SaveAs_RecordsAnEditNoScopeRecorded_SoTheSaveCoversIt()
    {
        var tabs = new NestTabs();
        var tab = OpenSaved(tabs, "unscoped.nest");
        tab.Document.Nest.Plates[0].Parts[0].Offset(4, 0);
        Assert.True(tab.Modified);

        tabs.SaveAs(tab, InDir("saved.nest"));

        Assert.False(tab.Modified);
        Assert.Equal("Undo Change", tab.Undo.UndoText);
        tab.Undo.Undo();
        Assert.True(tab.Modified);
    }

    [Fact]
    public void SaveAs_OntoItsOwnFile_IsAllowed()
    {
        var tabs = new NestTabs();
        var tab = OpenSaved(tabs, "own.nest");
        TestNests.Edit(tab);

        tabs.SaveAs(tab, tab.Path);

        Assert.False(tab.Modified);
        Assert.Equal(InDir("own.nest"), tab.SavePath);
    }

    [Fact]
    public void SaveAs_AWriteThatFails_KeepsTheFileNameAndUnsavedMark()
    {
        var tabs = new NestTabs();
        var tab = OpenSaved(tabs, "kept.nest");
        TestNests.Edit(tab);
        var changed = 0;
        tabs.Changed += (_, _) => changed++;

        Assert.ThrowsAny<IOException>(
            () => tabs.SaveAs(tab, InDir(Path.Combine("missing folder", "x.nest"))));

        Assert.Equal(InDir("kept.nest"), tab.Path);
        Assert.Equal("kept", tab.Name);
        Assert.Null(tab.Document.LastSavePath);
        Assert.True(tab.Modified);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void ExportCopy_WritesTheNestButKeepsTheTabsFileNameAndUnsavedMark()
    {
        var tabs = new NestTabs();
        var tab = OpenSaved(tabs, "job.nest");
        TestNests.Edit(tab);
        var target = InDir("copy.nest");

        tabs.ExportCopy(tab, target);

        Assert.Equal(InDir("job.nest"), tab.Path);
        Assert.Equal("job", tab.Name);
        Assert.Null(tab.SavePath);
        Assert.True(tab.Modified);
        var read = new NestReader(target).Read();
        Assert.Equal("job", read.Name);
        Assert.Single(read.Plates[0].Parts);
    }

    [Fact]
    public void SaveAs_AFailedWriteAfterAnEarlierSave_KeepsTheEarlierSavePath()
    {
        var tabs = new NestTabs();
        var tab = OpenSaved(tabs, "first.nest");
        var earlier = InDir("earlier.nest");
        tabs.SaveAs(tab, earlier);

        Assert.ThrowsAny<IOException>(
            () => tabs.SaveAs(tab, InDir(Path.Combine("missing folder", "x.nest"))));

        Assert.Equal(earlier, tab.SavePath);
        Assert.Equal(earlier, tab.Path);
    }

    [Fact]
    public void SaveAs_ATabThatWasClosed_IsRefusedAndWritesNothing()
    {
        var tabs = new NestTabs();
        var tab = OpenSaved(tabs, "closed.nest");
        tabs.Close(tab);
        var target = InDir("after-close.nest");

        Assert.Throws<SaveTargetRefusedException>(() => tabs.SaveAs(tab, target));
        Assert.Throws<SaveTargetRefusedException>(() => tabs.ExportCopy(tab, target));

        Assert.False(File.Exists(target));
    }

    [Fact]
    public void ExportCopy_OntoTheTabsOwnFile_IsRefusedAndWritesNothing()
    {
        var tabs = new NestTabs();
        var tab = OpenSaved(tabs, "own.nest");
        tab.Document.Nest.Notes = "not saved";
        var before = File.ReadAllBytes(tab.Path);

        var ex = Assert.Throws<SaveTargetRefusedException>(() => tabs.ExportCopy(tab, tab.Path));

        Assert.Contains("File > Save", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(tab.Path));
    }

    [Fact]
    public void ExportCopy_ToAFileAnotherTabShows_IsRefusedAndWritesNothing()
    {
        var tabs = new NestTabs();
        var a = OpenSaved(tabs, "a.nest");
        var b = OpenSaved(tabs, "b.nest");
        var before = File.ReadAllBytes(b.Path);

        Assert.Throws<SaveTargetRefusedException>(() => tabs.ExportCopy(a, b.Path));

        Assert.Equal(before, File.ReadAllBytes(b.Path));
    }
}
