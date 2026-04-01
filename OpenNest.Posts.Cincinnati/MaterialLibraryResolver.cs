using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Posts.Cincinnati;

public sealed class MaterialLibraryResolver
{
    private const double ThicknessTolerance = 0.001;

    private readonly List<MaterialLibraryEntry> _materialLibraries;
    private readonly List<EtchLibraryEntry> _etchLibraries;

    public MaterialLibraryResolver(CincinnatiPostConfig config)
    {
        _materialLibraries = config.MaterialLibraries ?? new List<MaterialLibraryEntry>();
        _etchLibraries = config.EtchLibraries ?? new List<EtchLibraryEntry>();
    }

    public string ResolveCutLibrary(string materialName, double thickness, string gas)
    {
        var entry = _materialLibraries.FirstOrDefault(e =>
            string.Equals(e.Material, materialName, StringComparison.OrdinalIgnoreCase) &&
            System.Math.Abs(e.Thickness - thickness) <= ThicknessTolerance &&
            string.Equals(e.Gas, gas, StringComparison.OrdinalIgnoreCase));

        return EnsureLibExtension(entry?.Library ?? "");
    }

    public string ResolveEtchLibrary(string gas)
    {
        var entry = _etchLibraries.FirstOrDefault(e =>
            string.Equals(e.Gas, gas, StringComparison.OrdinalIgnoreCase));

        return EnsureLibExtension(entry?.Library ?? "");
    }

    private static string EnsureLibExtension(string library)
    {
        if (string.IsNullOrEmpty(library))
            return library;

        if (!library.EndsWith(".lib", StringComparison.OrdinalIgnoreCase))
            return library + ".lib";

        return library;
    }

    public static string ResolveGas(Nest nest, CincinnatiPostConfig config)
    {
        return !string.IsNullOrEmpty(nest.AssistGas) ? nest.AssistGas : config.DefaultAssistGas;
    }
}
