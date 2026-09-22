using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenNest.Engine.Jobs;

/// <summary>
/// Registry of whole-job INestingEngine implementations, parallel to NestEngineRegistry (which is for
/// the legacy single-plate NestEngineBase). The four production strategies are exposed here through
/// FixedStrategyNestingEngine so they compete on equal footing with model-submitted engines. Unlike
/// NestEngineRegistry, this has no ActiveEngineName/global-selection concept — callers choose an engine
/// explicitly from AvailableEngines.
/// </summary>
public static class NestingEngineRegistry
{
    private static readonly List<NestingEngineInfo> engines = new();

    static NestingEngineRegistry()
    {
        Register(
            "StockLadder",
            "Caller-stock constrained-first fill and equivalent-demand area repacking",
            () => new StockLadderNestingEngine()
        );

        Register(
            "Default",
            "Multi-phase nesting (Linear, Pairs, RectBestFit, Remainder)",
            () => new FixedStrategyNestingEngine("Default")
        );

        Register(
            "Strip",
            "Strip-based nesting for mixed-drawing layouts",
            () => new FixedStrategyNestingEngine("Strip")
        );

        Register(
            "Vertical Remnant",
            "Optimizes for largest right-side vertical drop",
            () => new FixedStrategyNestingEngine("Vertical Remnant")
        );

        Register(
            "Horizontal Remnant",
            "Optimizes for largest top-side horizontal drop",
            () => new FixedStrategyNestingEngine("Horizontal Remnant")
        );
    }

    public static IReadOnlyList<NestingEngineInfo> AvailableEngines => engines;

    /// <summary>
    /// Creates the engine registered under <paramref name="name"/> (case-insensitive). The caller's
    /// explicit choice is the whole selection mechanism: unlike the legacy registry there is no
    /// process-global active name to consult or mutate. Unknown names throw.
    /// </summary>
    public static INestingEngine Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var info = engines.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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

        engines.Add(new NestingEngineInfo(name, description, factory));
    }

    /// <summary>Scans *.dll in directory for non-abstract INestingEngine types with a public
    /// parameterless constructor, registering each under its CLR type name. Mirrors
    /// NestEngineRegistry.LoadPlugins's per-assembly/per-type isolation: one bad plugin never
    /// prevents the rest from loading.</summary>
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
