using System;
using System.IO;
using OpenNest.Posts.Cincinnati;
using OpenNest.Posts.CincinnatiCIFiber;
using Xunit;

namespace OpenNest.Tests.PostSettings;

/// <summary>
/// An update replaces the install folder, so a post's edited settings must live outside it:
/// saves go to the user's settings directory and the shipped JSON only seeds them.
/// </summary>
[Collection(Collection)]
public class PostSettingsFileTests : IDisposable
{
    /// <summary>Tests that set <see cref="PostSettingsFile.DirectoryOverride"/> share it, so they never run together.</summary>
    public const string Collection = "PostSettingsFile";

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "opennest-post-settings-" + Guid.NewGuid());

    public PostSettingsFileTests() => PostSettingsFile.DirectoryOverride = directory;

    public void Dispose()
    {
        PostSettingsFile.DirectoryOverride = null;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Save_WritesTheUserCopy_AndLeavesTheShippedDefaultsUntouched()
    {
        var assembly = typeof(CincinnatiPostProcessor).Assembly;
        var shipped = PostSettingsFile.ShippedPathFor(assembly);
        var shippedBefore = File.ReadAllText(shipped);

        var post = new CincinnatiPostProcessor();
        Assert.NotEmpty(post.Config.MaterialLibraries);
        post.Config.ConfigurationName = "EDITED";
        post.SaveConfig();

        Assert.Equal(shippedBefore, File.ReadAllText(shipped));
        Assert.StartsWith(directory, PostSettingsFile.PathFor(assembly));
        Assert.Equal("EDITED", new CincinnatiPostProcessor().Config.ConfigurationName);
        Assert.False(File.Exists(PostSettingsFile.PathFor(assembly) + ".tmp"));
    }

    [Fact]
    public void Constructing_WithNoSavedCopy_ReadsTheShippedDefaultsWithoutWriting()
    {
        var post = new CIFiberPostProcessor();

        Assert.Contains("Carbon Steel", post.Config.MaterialCodes.Keys);
        Assert.False(Directory.Exists(directory));
    }
}
