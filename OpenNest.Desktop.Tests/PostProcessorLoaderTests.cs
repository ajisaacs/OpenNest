namespace OpenNest.Desktop.Tests;

/// <summary>
/// Post processors load from a Posts directory one DLL at a time: a DLL that fails is
/// reported and the others still load, and the materials they provide are registered.
/// </summary>
public sealed class PostProcessorLoaderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "OpenNestDesktopTests", Guid.NewGuid().ToString("N"));

    public PostProcessorLoaderTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void Load_IsolatesAFailingDllReportsItAndRegistersTheOthersMaterials()
    {
        // This test assembly is a post plug-in (it holds MaterialPost); the broken DLL is not .NET at all.
        File.Copy(typeof(MaterialPost).Assembly.Location, Path.Combine(directory, "Good.Posts.dll"));
        File.WriteAllText(Path.Combine(directory, "Broken.Posts.dll"), "not an assembly");
        File.WriteAllText(Path.Combine(directory, "Readme.txt"), "ignored");

        var result = PostProcessorLoader.Load(directory);

        var failure = Assert.Single(result.Failures);
        Assert.StartsWith("Broken.Posts.dll: ", failure);
        Assert.Contains(result.PostProcessors, post => post.Name == MaterialPost.PostName);
        Assert.Contains("Unobtainium", PostProcessorMaterials.Names, StringComparer.Ordinal);
        Assert.Contains("aluminum 5052", PostProcessorMaterials.Names, StringComparer.Ordinal);
    }

    [Fact]
    public void Load_WithoutAPostsDirectory_LoadsNothing()
    {
        var result = PostProcessorLoader.Load(Path.Combine(directory, "missing"));

        Assert.Empty(result.PostProcessors);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Materials_AreAddedOnceIgnoringCaseAndBlanksInNameOrder()
    {
        PostProcessorMaterials.AddFrom(new MaterialPost(["zz Order B", "ZZ ORDER a", " ", "zz order b"]));

        var names = PostProcessorMaterials.Names.Where(n => n.StartsWith("zz", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(["ZZ ORDER a", "zz Order B"], names);
        Assert.DoesNotContain(" ", PostProcessorMaterials.Names);
    }

    internal sealed class MaterialPost : IPostProcessor, IMaterialProvidingPostProcessor
    {
        public const string PostName = "Material Test Post";

        private readonly IReadOnlyList<string> materials;

        public MaterialPost() : this(["Unobtainium", "aluminum 5052"])
        {
        }

        public MaterialPost(IReadOnlyList<string> materials) => this.materials = materials;

        public string Name => PostName;

        public string Author => "Tests";

        public string Description => "Provides materials.";

        public IEnumerable<string> GetMaterialNames() => materials;

        public void Post(Nest nest, Stream outputStream) { }

        public void Post(Nest nest, string outputFile) { }
    }
}
