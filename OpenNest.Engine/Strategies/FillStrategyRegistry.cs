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
        private static HashSet<string> enabledFilter;
        private static readonly HashSet<string> disabled = new(StringComparer.OrdinalIgnoreCase);

        static FillStrategyRegistry()
        {
            LoadFrom(typeof(FillStrategyRegistry).Assembly);
        }

        public static IReadOnlyList<IFillStrategy> Strategies =>
            sorted ??= FilterStrategies();

        /// <summary>
        /// Returns all registered strategies regardless of enabled/disabled state.
        /// </summary>
        public static IReadOnlyList<IFillStrategy> AllStrategies =>
            strategies.OrderBy(s => s.Order).ToList();

        /// <summary>
        /// Returns the names of all permanently disabled strategies.
        /// </summary>
        public static IReadOnlyCollection<string> DisabledNames => disabled;

        private static List<IFillStrategy> FilterStrategies()
        {
            var source = enabledFilter != null
                ? strategies.Where(s => enabledFilter.Contains(s.Name))
                : strategies.Where(s => !disabled.Contains(s.Name));
            return source.OrderBy(s => s.Order).ToList();
        }

        /// <summary>
        /// Permanently disables strategies by name. They remain registered
        /// but are excluded from the default pipeline.
        /// </summary>
        public static void Disable(params string[] names)
        {
            foreach (var name in names)
                disabled.Add(name);
            sorted = null;
        }

        /// <summary>
        /// Re-enables a previously disabled strategy.
        /// </summary>
        public static void Enable(params string[] names)
        {
            foreach (var name in names)
                disabled.Remove(name);
            sorted = null;
        }

        /// <summary>
        /// Restricts the active strategies to only those whose names are listed.
        /// Pass null to restore all strategies.
        /// </summary>
        public static void SetEnabled(params string[] names)
        {
            enabledFilter = names != null && names.Length > 0
                ? new HashSet<string>(names, StringComparer.OrdinalIgnoreCase)
                : null;
            sorted = null;
        }

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
