using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Strategies
{
    /// <summary>
    /// The built-in fill strategies in pipeline order, with one process-wide disabled set that the
    /// desktop Options form sets. Every fill that runs the strategy pipeline reads it.
    /// </summary>
    public static class FillStrategyRegistry
    {
        // Pipeline order, ascending by each strategy's Order value.
        private static readonly IReadOnlyList<IFillStrategy> builtIn = Array.AsReadOnly(
            new IFillStrategy[]
            {
                new PairsFillStrategy(),
                new RowFillStrategy(),
                new ColumnFillStrategy(),
                new RectBestFitStrategy(),
                new ExtentsFillStrategy(),
                new LinearFillStrategy(),
                new TiltedRemnantStrategy(),
            }
        );

        private static IReadOnlyList<IFillStrategy> strategies = builtIn;
        private static List<IFillStrategy> sorted;
        private static HashSet<string> enabledFilter;
        private static readonly HashSet<string> disabled = new(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<IFillStrategy> Strategies => sorted ??= FilterStrategies();

        /// <summary>
        /// Returns all strategies regardless of enabled/disabled state.
        /// </summary>
        public static IReadOnlyList<IFillStrategy> AllStrategies => strategies;

        /// <summary>
        /// Returns the names of all permanently disabled strategies.
        /// </summary>
        public static IReadOnlyCollection<string> DisabledNames => disabled;

        private static List<IFillStrategy> FilterStrategies()
        {
            var source =
                enabledFilter != null
                    ? strategies.Where(s => enabledFilter.Contains(s.Name))
                    : strategies.Where(s => !disabled.Contains(s.Name));
            return source.ToList();
        }

        /// <summary>
        /// Permanently disables strategies by name. They remain listed
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
            enabledFilter =
                names != null && names.Length > 0
                    ? new HashSet<string>(names, StringComparer.OrdinalIgnoreCase)
                    : null;
            sorted = null;
        }

        /// <summary>
        /// Replaces the built-in list, in the given order, until the result is disposed. For tests
        /// that drive fills through a host that builds its own filler, such as the plate view.
        /// </summary>
        internal static IDisposable UseForTesting(IReadOnlyList<IFillStrategy> replacement)
        {
            var prior = strategies;
            strategies = replacement;
            sorted = null;
            return new Restore(prior);
        }

        private sealed class Restore : IDisposable
        {
            private IReadOnlyList<IFillStrategy> prior;

            public Restore(IReadOnlyList<IFillStrategy> prior) => this.prior = prior;

            public void Dispose()
            {
                if (prior == null)
                    return;
                strategies = prior;
                sorted = null;
                prior = null;
            }
        }
    }
}
