namespace OpenNest.IO.Tests;

public class IoDependencyTests
{
    [Fact]
    public void IoAssembly_DoesNotReferenceTheEngine()
    {
        var references = typeof(NestReader).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain("OpenNest.Engine", references);
    }
}
