using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Posts.CincinnatiCIFiber;

namespace OpenNest.Tests.CincinnatiCIFiber;

/// <summary>
/// Steel is named "Carbon Steel" across OpenNest (the Cincinnati post's libraries and
/// the company material list), so the CI Fiber post must list it explicitly rather
/// than reach MSN only through DefaultMaterialCode. "Mild Steel" stays for
/// nests that already use it.
/// </summary>
public class CIFiberMaterialNameTests
{
    private static CIFiberPostConfig LoadShipped()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenNest.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var path = Path.Combine(
            dir!.FullName,
            "Posts",
            "OpenNest.Posts.CincinnatiCIFiber",
            "OpenNest.Posts.CincinnatiCIFiber.json"
        );
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        return JsonSerializer.Deserialize<CIFiberPostConfig>(File.ReadAllText(path), options)!;
    }

    public static IEnumerable<object[]> Configs()
    {
        yield return new object[] { "shipped json" };
        yield return new object[] { "built-in default" };
    }

    private static CIFiberPostConfig For(string source) =>
        source == "shipped json" ? LoadShipped() : new CIFiberPostConfig();

    [Theory]
    [MemberData(nameof(Configs))]
    public void CarbonSteel_IsListedAsMSN(string source)
    {
        var codes = For(source).MaterialCodes;

        Assert.True(codes.TryGetValue("Carbon Steel", out var code), $"{source}: no Carbon Steel entry");
        Assert.Equal("MSN", code);
    }

    [Theory]
    [MemberData(nameof(Configs))]
    public void MildSteel_StillMapsToMSN(string source)
    {
        var codes = For(source).MaterialCodes;

        Assert.True(codes.TryGetValue("Mild Steel", out var code), $"{source}: no Mild Steel entry");
        Assert.Equal("MSN", code);
    }
}
