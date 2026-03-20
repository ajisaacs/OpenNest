using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenNest
{
    public static class NestEngineRegistry
    {
        private static readonly List<NestEngineInfo> engines = new();

        static NestEngineRegistry()
        {
            Register("Default",
                "Multi-phase nesting (Linear, Pairs, RectBestFit, Remainder)",
                plate => new DefaultNestEngine(plate));

            Register("Strip",
                "Strip-based nesting for mixed-drawing layouts",
                plate => new StripNestEngine(plate));

            Register("NFP",
                "NFP-based mixed-part nesting with simulated annealing",
                plate => new NfpNestEngine(plate));
        }

        public static IReadOnlyList<NestEngineInfo> AvailableEngines => engines;

        public static string ActiveEngineName { get; set; } = "Default";

        public static NestEngineBase Create(Plate plate)
        {
            var info = engines.FirstOrDefault(e =>
                e.Name.Equals(ActiveEngineName, StringComparison.OrdinalIgnoreCase));

            if (info == null)
            {
                Debug.WriteLine($"[NestEngineRegistry] Engine '{ActiveEngineName}' not found, falling back to Default");
                info = engines[0];
            }

            return info.Factory(plate);
        }

        public static void Register(string name, string description, Func<Plate, NestEngineBase> factory)
        {
            if (engines.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.WriteLine($"[NestEngineRegistry] Duplicate engine '{name}' skipped");
                return;
            }

            engines.Add(new NestEngineInfo(name, description, factory));
        }

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
                        if (type.IsAbstract || !typeof(NestEngineBase).IsAssignableFrom(type))
                            continue;

                        var ctor = type.GetConstructor(new[] { typeof(Plate) });

                        if (ctor == null)
                        {
                            Debug.WriteLine($"[NestEngineRegistry] Skipping {type.Name}: no Plate constructor");
                            continue;
                        }

                        // Create a temporary instance to read Name and Description.
                        try
                        {
                            var tempPlate = new Plate();
                            var instance = (NestEngineBase)ctor.Invoke(new object[] { tempPlate });
                            Register(instance.Name, instance.Description,
                                plate => (NestEngineBase)ctor.Invoke(new object[] { plate }));
                            Debug.WriteLine($"[NestEngineRegistry] Loaded plugin engine: {instance.Name}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NestEngineRegistry] Failed to instantiate {type.Name}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NestEngineRegistry] Failed to load assembly {Path.GetFileName(dll)}: {ex.Message}");
                }
            }
        }
    }
}
