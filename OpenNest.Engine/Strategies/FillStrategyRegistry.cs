using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenNest.Engine.Strategies
{
    public static class FillStrategyRegistry
    {
        private static readonly List<IFillStrategy> strategies = new();
        private static List<IFillStrategy> sorted;

        static FillStrategyRegistry()
        {
            LoadFrom(typeof(FillStrategyRegistry).Assembly);
        }

        public static IReadOnlyList<IFillStrategy> Strategies =>
            sorted ??= strategies.OrderBy(s => s.Order).ToList();

        public static void LoadFrom(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IFillStrategy).IsAssignableFrom(type))
                    continue;

                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    Debug.WriteLine($"[FillStrategyRegistry] Skipping {type.Name}: no parameterless constructor");
                    continue;
                }

                try
                {
                    var instance = (IFillStrategy)ctor.Invoke(null);

                    if (strategies.Any(s => s.Name.Equals(instance.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        Debug.WriteLine($"[FillStrategyRegistry] Duplicate strategy '{instance.Name}' skipped");
                        continue;
                    }

                    strategies.Add(instance);
                    Debug.WriteLine($"[FillStrategyRegistry] Registered: {instance.Name} (Order={instance.Order})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FillStrategyRegistry] Failed to instantiate {type.Name}: {ex.Message}");
                }
            }

            sorted = null;
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
                    LoadFrom(assembly);
                    Debug.WriteLine($"[FillStrategyRegistry] Loaded plugin assembly: {Path.GetFileName(dll)}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FillStrategyRegistry] Failed to load {Path.GetFileName(dll)}: {ex.Message}");
                }
            }
        }
    }
}
