using OpenNest.Help;

namespace OpenNest.WinForms.Tests;

public class HelpTopicsTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("opennest-help-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private void WriteTopics(string json) => File.WriteAllText(Path.Combine(directory, "topics.json"), json);

    [Fact]
    public void MapsTopicToItsPageAndAnchor()
    {
        WriteTopics("""{ "topics": { "home": "index.html", "auto-nest": "first-nest.html#run-auto-nest" } }""");

        Assert.Equal("first-nest.html#run-auto-nest", HelpTopics.ResolvePage(directory, HelpTopics.AutoNest));
    }

    [Fact]
    public void UnknownTopicOpensTheHomeTopic()
    {
        WriteTopics("""{ "topics": { "home": "welcome.html" } }""");

        Assert.Equal("welcome.html", HelpTopics.ResolvePage(directory, "not-a-topic"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{ "pages": {} }""")]
    public void MissingOrUnreadableTopicsFallBackToIndex(string json)
    {
        if (json != null)
            WriteTopics(json);

        Assert.Equal("index.html", HelpTopics.ResolvePage(directory, HelpTopics.Post));
    }
}
