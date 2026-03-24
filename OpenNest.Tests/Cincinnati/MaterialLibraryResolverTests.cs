using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

public class MaterialLibraryResolverTests
{
    private static CincinnatiPostConfig ConfigWithLibraries() => new()
    {
        DefaultAssistGas = "O2",
        DefaultEtchGas = "N2",
        MaterialLibraries = new()
        {
            new MaterialLibraryEntry { Material = "Mild Steel", Thickness = 0.250, Gas = "O2", Library = "MS250O2.lib" },
            new MaterialLibraryEntry { Material = "Mild Steel", Thickness = 0.250, Gas = "N2", Library = "MS250N2.lib" },
            new MaterialLibraryEntry { Material = "Aluminum", Thickness = 0.125, Gas = "N2", Library = "AL125N2.lib" },
            new MaterialLibraryEntry { Material = "Stainless Steel", Thickness = 0.375, Gas = "AIR", Library = "SS375AIR.lib" }
        },
        EtchLibraries = new()
        {
            new EtchLibraryEntry { Gas = "N2", Library = "EtchN2.lib" },
            new EtchLibraryEntry { Gas = "O2", Library = "EtchO2.lib" },
            new EtchLibraryEntry { Gas = "AIR", Library = "EtchAIR.lib" }
        }
    };

    [Fact]
    public void ResolveCutLibrary_ExactMatch()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var result = resolver.ResolveCutLibrary("Mild Steel", 0.250, "O2");
        Assert.Equal("MS250O2.lib", result);
    }

    [Fact]
    public void ResolveCutLibrary_CaseInsensitiveMaterial()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var result = resolver.ResolveCutLibrary("mild steel", 0.250, "O2");
        Assert.Equal("MS250O2.lib", result);
    }

    [Fact]
    public void ResolveCutLibrary_CaseInsensitiveGas()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var result = resolver.ResolveCutLibrary("Mild Steel", 0.250, "o2");
        Assert.Equal("MS250O2.lib", result);
    }

    [Fact]
    public void ResolveCutLibrary_ThicknessWithinTolerance()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var result = resolver.ResolveCutLibrary("Mild Steel", 0.2505, "O2");
        Assert.Equal("MS250O2.lib", result);
    }

    [Fact]
    public void ResolveCutLibrary_ThicknessOutsideTolerance_ReturnsEmpty()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var result = resolver.ResolveCutLibrary("Mild Steel", 0.260, "O2");
        Assert.Equal("", result);
    }

    [Fact]
    public void ResolveCutLibrary_NoMatch_ReturnsEmpty()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var result = resolver.ResolveCutLibrary("Titanium", 0.250, "O2");
        Assert.Equal("", result);
    }

    [Fact]
    public void ResolveCutLibrary_WrongGas_ReturnsEmpty()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var result = resolver.ResolveCutLibrary("Mild Steel", 0.250, "AIR");
        Assert.Equal("", result);
    }

    [Fact]
    public void ResolveCutLibrary_DifferentGasSameMaterial()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var o2 = resolver.ResolveCutLibrary("Mild Steel", 0.250, "O2");
        var n2 = resolver.ResolveCutLibrary("Mild Steel", 0.250, "N2");
        Assert.Equal("MS250O2.lib", o2);
        Assert.Equal("MS250N2.lib", n2);
    }

    [Fact]
    public void ResolveCutLibrary_EmptyList_ReturnsEmpty()
    {
        var config = new CincinnatiPostConfig { MaterialLibraries = new() };
        var resolver = new MaterialLibraryResolver(config);
        var result = resolver.ResolveCutLibrary("Mild Steel", 0.250, "O2");
        Assert.Equal("", result);
    }

    [Fact]
    public void ResolveEtchLibrary_ExactMatch()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var result = resolver.ResolveEtchLibrary("N2");
        Assert.Equal("EtchN2.lib", result);
    }

    [Fact]
    public void ResolveEtchLibrary_CaseInsensitive()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var result = resolver.ResolveEtchLibrary("n2");
        Assert.Equal("EtchN2.lib", result);
    }

    [Fact]
    public void ResolveEtchLibrary_NoMatch_ReturnsEmpty()
    {
        var resolver = new MaterialLibraryResolver(ConfigWithLibraries());
        var result = resolver.ResolveEtchLibrary("Argon");
        Assert.Equal("", result);
    }

    [Fact]
    public void ResolveEtchLibrary_EmptyList_ReturnsEmpty()
    {
        var config = new CincinnatiPostConfig { EtchLibraries = new() };
        var resolver = new MaterialLibraryResolver(config);
        var result = resolver.ResolveEtchLibrary("N2");
        Assert.Equal("", result);
    }

    [Fact]
    public void ResolveGas_UsesNestAssistGas_WhenSet()
    {
        var nest = new Nest("Test") { AssistGas = "N2" };
        var config = new CincinnatiPostConfig { DefaultAssistGas = "O2" };
        var result = MaterialLibraryResolver.ResolveGas(nest, config);
        Assert.Equal("N2", result);
    }

    [Fact]
    public void ResolveGas_FallsBackToConfig_WhenNestEmpty()
    {
        var nest = new Nest("Test") { AssistGas = "" };
        var config = new CincinnatiPostConfig { DefaultAssistGas = "O2" };
        var result = MaterialLibraryResolver.ResolveGas(nest, config);
        Assert.Equal("O2", result);
    }

    [Fact]
    public void ResolveGas_FallsBackToConfig_WhenNestNull()
    {
        var nest = new Nest("Test");
        var config = new CincinnatiPostConfig { DefaultAssistGas = "AIR" };
        var result = MaterialLibraryResolver.ResolveGas(nest, config);
        Assert.Equal("AIR", result);
    }
}
