#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenNest.Engine.NestingEngines.Circles;
using OpenNest.Engine.NestingEngines.Default;
using OpenNest.Engine.NestingEngines.FlatBar;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Engine.NestingEngines.Rectangles;

namespace OpenNest.Engine.Jobs;

/// <summary>
/// Registry of whole-job <see cref="INestingEngine"/> implementations. "Default" chooses among the
/// built-in engines per job; the four fill strategies are exposed through
/// <see cref="FixedStrategyNestingEngine"/>. Callers choose an
/// engine explicitly from <see cref="AvailableEngines"/>; there is no process-global active
/// selection. Plug-ins loaded from an Engines/ folder register under their CLR type name.
/// </summary>
public static class NestingEngineRegistry
{
    private static readonly List<NestingEngineInfo> engines = new();

    /// <summary>
    /// Registry names used by earlier releases, mapped to the engine that replaced them, so saved
    /// selections and scripts keep working. Consulted only when no engine has the requested name.
    /// </summary>
    private static readonly Dictionary<string, string> RenamedEngines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Opus55NestingEngine"] = "Irregular",
        ["RectanglesNestingEngine"] = "Rectangles",
        // Retired: Default chooses stock at least as well on every benchmark job.
        ["StockLadder"] = "Default",
        ["Fill"] = "Lattice",
    };

    static NestingEngineRegistry()
    {
        // Listed first: the engine front ends use when the caller names none.
        Register(
            "Default",
            "Any job: tries Rectangles, Circles and Irregular and keeps the best",
            () => new DefaultNestingEngine()
        );

        Register(
            "Rectangles",
            "Packs each part as a box; best for plain rectangular parts",
            () => new RectanglesNestingEngine()
        );

        Register(
            "Irregular",
            "Fits irregular shapes into each other's gaps and notches",
            () => new IrregularNestingEngine()
        );

        Register(
            "Circles",
            "Round parts such as blanks, washers and flanges",
            () => new CirclesNestingEngine()
        );

        Register(
            "FlatBar",
            "One row of parts along a flat bar",
            () => new FlatBarNestingEngine()
        );

        Register(
            "Lattice",
            "Repeats parts in tight rows and pairs; best for many copies of a few parts",
            () => new FixedStrategyNestingEngine("Lattice")
        );

        Register(
            "Strip",
            "Fills one strip at a time, part by part; each uses the space the last one left",
            () => new FixedStrategyNestingEngine("Strip")
        );

        Register(
            "Vertical Remnant",
            "Repeats parts in rows and pairs, keeping the largest drop on the right side",
            () => new FixedStrategyNestingEngine("Vertical Remnant")
        );

        Register(
            "Horizontal Remnant",
            "Repeats parts in rows and pairs, keeping the largest drop on the top",
            () => new FixedStrategyNestingEngine("Horizontal Remnant")
        );
    }

    public static IReadOnlyList<NestingEngineInfo> AvailableEngines => engines;

    /// <summary>
    /// Registered name for <paramref name="name"/>: an exact (case-insensitive) match, else the
    /// engine a renamed legacy name now maps to, else null. Hosts use this to restore a saved
    /// selection made under an old name.
    /// </summary>
    public static string? ResolveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var trimmed = name.Trim();
        var info = engines.FirstOrDefault(e => e.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (info != null)
            return info.Name;
        return RenamedEngines.TryGetValue(trimmed, out var renamed)
            && engines.FirstOrDefault(e => e.Name.Equals(renamed, StringComparison.OrdinalIgnoreCase)) is { } target
            ? target.Name
            : null;
    }

    /// <summary>
    /// Creates the engine registered under <paramref name="name"/> (case-insensitive, renamed legacy
    /// names accepted). The caller's explicit choice is the whole selection mechanism; unknown names throw.
    /// </summary>
    public static INestingEngine Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var resolved = ResolveName(name);
        var info = resolved == null ? null : engines.First(e => e.Name == resolved);
        if (info == null)
            throw new NotSupportedException(
                $"Unknown nesting engine: {name}. Available: {string.Join(", ", engines.Select(e => e.Name))}."
            );
        return info.Factory();
    }

    public static void Register(string name, string description, Func<INestingEngine> factory)
    {
        if (engines.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Debug.WriteLine($"[NestingEngineRegistry] Duplicate engine '{name}' skipped");
            return;
        }

        // A leftover plug-in under a renamed engine's old name would shadow its built-in replacement.
        if (RenamedEngines.ContainsKey(name))
        {
            Debug.WriteLine($"[NestingEngineRegistry] '{name}' skipped: replaced by built-in '{RenamedEngines[name]}'");
            return;
        }

        engines.Add(new NestingEngineInfo(name, description, factory));
    }

    /// <summary>Scans *.dll in directory for non-abstract INestingEngine types with a public
    /// parameterless constructor, registering each under its CLR type name. Per-assembly and per-type
    /// isolation ensures one bad plugin never prevents the rest from loading.</summary>
    public static void LoadPlugins(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);

                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || !typeof(INestingEngine).IsAssignableFrom(type))
                        continue;

                    var ctor = type.GetConstructor(Type.EmptyTypes);

                    if (ctor == null)
                    {
                        Debug.WriteLine(
                            $"[NestingEngineRegistry] Skipping {type.Name}: no parameterless constructor"
                        );
                        continue;
                    }

                    try
                    {
                        Register(type.Name, string.Empty, () => (INestingEngine)ctor.Invoke(null));
                        Debug.WriteLine(
                            $"[NestingEngineRegistry] Loaded plugin engine: {type.Name}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[NestingEngineRegistry] Failed to register {type.Name}: {ex.Message}"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[NestingEngineRegistry] Failed to load assembly {Path.GetFileName(dll)}: {ex.Message}"
                );
            }
        }
    }
}
