namespace OpenNest.Desktop.Tests;

public class NestTabsTests
{
    private readonly List<string> loaded = new();

    private Document Load(string path)
    {
        loaded.Add(path);
        return new Document { Nest = new Nest { Name = Path.GetFileNameWithoutExtension(path) } };
    }

    private static string FullPath(string name) => Path.GetFullPath(Path.Combine("nests", name));

    [Fact]
    public void Empty_HasNoActiveTab()
    {
        var tabs = new NestTabs();

        Assert.Equal(-1, tabs.ActiveIndex);
        Assert.Null(tabs.Active);
        Assert.False(tabs.AnyModified);
    }

    [Fact]
    public void Open_AddsATabAfterTheOthersAndActivatesIt()
    {
        var tabs = new NestTabs();

        tabs.Open(FullPath("a.nest"), Load);
        var b = tabs.Open(FullPath("b.nest"), Load);

        Assert.Equal(["a", "b"], tabs.Tabs.Select(t => t.Name));
        Assert.Same(b, tabs.Active);
        Assert.Equal(FullPath("b.nest"), b.Path);
    }

    [Fact]
    public void Open_AFileAlreadyOpen_ActivatesItsTabWithoutLoadingAgain()
    {
        var tabs = new NestTabs();
        var a = tabs.Open(FullPath("a.nest"), Load);
        tabs.Open(FullPath("b.nest"), Load);

        var again = tabs.Open(Path.Combine("nests", "..", "nests", "a.nest"), Load);

        Assert.Same(a, again);
        Assert.Equal(0, tabs.ActiveIndex);
        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public void Open_ALoadThatFails_AddsNoTabAndKeepsTheActiveOne()
    {
        var tabs = new NestTabs();
        var a = tabs.Open(FullPath("a.nest"), Load);

        Assert.Throws<IOException>(() =>
            tabs.Open(FullPath("broken.nest"), _ => throw new IOException("bad file")));

        Assert.Single(tabs.Tabs);
        Assert.Same(a, tabs.Active);
    }

    [Fact]
    public void Close_TheActiveTab_ActivatesTheOneToItsRight()
    {
        var tabs = Three(out var a, out var b, out var c);
        tabs.Activate(1);

        tabs.Close(b);

        Assert.Same(c, tabs.Active);
        Assert.Equal([a, c], tabs.Tabs);
    }

    [Fact]
    public void Close_TheLastActiveTab_ActivatesTheOneToItsLeft()
    {
        var tabs = Three(out _, out var b, out var c);

        tabs.Close(c);

        Assert.Same(b, tabs.Active);
    }

    [Fact]
    public void Close_ATabLeftOfTheActiveOne_KeepsTheSameNestActive()
    {
        var tabs = Three(out var a, out _, out var c);

        tabs.Close(a);

        Assert.Same(c, tabs.Active);
        Assert.Equal(1, tabs.ActiveIndex);
    }

    [Fact]
    public void Close_TheOnlyTab_LeavesNoneActive()
    {
        var tabs = new NestTabs();
        var a = tabs.Open(FullPath("a.nest"), Load);

        tabs.Close(a);

        Assert.Empty(tabs.Tabs);
        Assert.Null(tabs.Active);
    }

    [Fact]
    public void Modified_IsPerTabAndSavedClearsIt()
    {
        var tabs = Three(out var a, out var b, out _);

        b.MarkModified();

        Assert.True(tabs.AnyModified);
        Assert.False(a.Modified);
        b.MarkSaved();
        Assert.False(tabs.AnyModified);
    }

    [Fact]
    public void Changed_IsRaisedForOpenActivateAndClose_ButNotReactivation()
    {
        var tabs = new NestTabs();
        var count = 0;
        tabs.Changed += (_, _) => count++;

        var a = tabs.Open(FullPath("a.nest"), Load);
        tabs.Open(FullPath("b.nest"), Load);
        tabs.Activate(0);
        tabs.Activate(0);
        tabs.Close(a);

        Assert.Equal(4, count);
    }

    private NestTabs Three(out NestTab a, out NestTab b, out NestTab c)
    {
        var tabs = new NestTabs();
        a = tabs.Open(FullPath("a.nest"), Load);
        b = tabs.Open(FullPath("b.nest"), Load);
        c = tabs.Open(FullPath("c.nest"), Load);
        return tabs;
    }
}
