namespace OpenNest.Server.Tests;

public sealed class ServerOptionsTests
{
    private static readonly string Default = Path.Combine("data", "nests.db");

    [Theory]
    [InlineData(new[] { "--database=/srv/a.db" }, "/srv/a.db")]
    [InlineData(new[] { "--database", "/srv/a.db" }, "/srv/a.db")]
    [InlineData(new[] { "--urls=http://127.0.0.1:8090", "--database", "/srv/a.db" }, "/srv/a.db")]
    [InlineData(new[] { "--database=/srv/a=b.db" }, "/srv/a=b.db")]
    public void DatabasePath_AcceptsBothForms_AndOverridesEnvironment(string[] args, string expected)
    {
        Assert.Equal(expected, ServerOptions.DatabasePath(args, "/env/nests.db"));
    }

    [Fact]
    public void DatabasePath_WithoutArgument_UsesEnvironmentThenDefault()
    {
        Assert.Equal("/env/nests.db", ServerOptions.DatabasePath(["--urls=http://127.0.0.1:8090"], "/env/nests.db"));
        Assert.Equal(Default, ServerOptions.DatabasePath([], null));
    }

    [Theory]
    [InlineData("--database")]
    [InlineData("--database", "--urls=http://127.0.0.1:8090")]
    [InlineData("--database=")]
    [InlineData("--database=  ")]
    [InlineData("--database=/a.db", "--database", "/b.db")]
    public void DatabasePath_MissingOrRepeated_FailsInsteadOfFallingBack(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => ServerOptions.DatabasePath(args, "/env/nests.db"));
    }
}
