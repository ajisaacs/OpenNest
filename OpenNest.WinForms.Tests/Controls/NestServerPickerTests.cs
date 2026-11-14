using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Controls;

public class NestServerPickerTests
{
    private static NestStorageSettings Settings()
    {
        var settings = new NestStorageSettings { Mode = NestStorageMode.Database, ServerUrl = "http://work:8090" };
        settings.RememberServer("http://work:8090", "Work production");
        settings.RememberServer("http://barge.lan:8090", "Home test");
        return settings;
    }

    private static (Form Form, NestServerPicker Picker, ComboBox Saved, TextBox Url, TextBox Name, Button Forget) Show()
    {
        var picker = new NestServerPicker();
        var form = new Form { ShowInTaskbar = false };
        form.Controls.Add(picker);
        form.Show();
        picker.Bind(Settings());
        return (form, picker,
            Assert.Single(All<ComboBox>(form)),
            Assert.Single(All<TextBox>(form), box => box.Name == "serverUrlBox"),
            Assert.Single(All<TextBox>(form), box => box.Name == "serverNameBox"),
            Assert.Single(All<Button>(form)));
    }

    [Fact]
    public void BindListsSavedServersByNameAndShowsTheOneInUse() => StaTestThread.Run(() =>
    {
        var (form, picker, saved, _, _, forget) = Show();
        using var owner = form;

        Assert.Equal(
            new[] { "Work production — http://work:8090", "Home test — http://barge.lan:8090" },
            saved.Items.Cast<object>().Select(saved.GetItemText));
        Assert.Equal(0, saved.SelectedIndex);
        Assert.Equal("http://work:8090", picker.ServerUrl);
        Assert.Equal("Work production", picker.ServerName);
        Assert.False(forget.Enabled); // The server in use cannot be forgotten.
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void PickingASavedServerFillsItsAddressAndName() => StaTestThread.Run(() =>
    {
        var (form, picker, saved, _, _, forget) = Show();
        using var owner = form;

        saved.SelectedIndex = 1;

        Assert.Equal("http://barge.lan:8090", picker.ServerUrl);
        Assert.Equal("Home test", picker.ServerName);
        Assert.True(forget.Enabled);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void TypingAnAddressFollowsTheSavedList() => StaTestThread.Run(() =>
    {
        var (form, picker, saved, url, name, _) = Show();
        using var owner = form;

        url.Text = "http://new-server:8090";
        Assert.Equal(-1, saved.SelectedIndex);
        name.Text = "Corpus";
        Assert.Equal("Corpus", picker.ServerName);

        url.Text = "http://BARGE.lan:8090/";
        Assert.Equal(1, saved.SelectedIndex);
        Assert.Equal("Home test", picker.ServerName);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void ForgetRemovesTheServerAndReturnsToTheOneInUse() => StaTestThread.Run(() =>
    {
        var (form, picker, saved, _, _, forget) = Show();
        using var owner = form;
        var forgotten = new List<string>();
        picker.ForgetServer = forgotten.Add;

        saved.SelectedIndex = 1;
        forget.PerformClick();

        Assert.Equal(new[] { "http://barge.lan:8090" }, forgotten);
        Assert.Single(saved.Items);
        Assert.Equal("http://work:8090", picker.ServerUrl);
        Assert.Equal("Work production", picker.ServerName);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
