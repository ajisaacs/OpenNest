using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenNest.Data;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;

namespace OpenNest.Forms
{
    /// <summary>
    /// App-scoped nesting-engine selection. The selected name addresses a whole-job
    /// INestingEngine resolved through NestingEngineRegistry at call time; single-plate
    /// interactive fill uses FillStrategy, which maps a built-in engine to its placement
    /// strategy and falls back to Default for jobs-only engines (StockLadder, plug-ins).
    /// </summary>
    public static class EngineSelection
    {
        public const string DefaultEngineName = EngineSelectionSettings.DefaultEngineName;

        /// <summary>Registered jobs engine deliberately kept out of the desktop combo.</summary>
        public const string HiddenEngineName = "StockLadder";

        private static string engineName = DefaultEngineName;

        /// <summary>Current selection. User changes are persisted for the next launch.</summary>
        public static string EngineName
        {
            get { return engineName; }
            set
            {
                engineName = string.IsNullOrWhiteSpace(value)
                    ? DefaultEngineName
                    : value.Trim();

                try
                {
                    new EngineSelectionSettings { EngineName = engineName }
                        .Save(EngineSelectionSettings.DefaultPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A read-only profile must not prevent selecting an engine for this session.
                    Debug.WriteLine($"[EngineSelection] Could not save engine selection: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Call after NestingEngineRegistry.LoadPlugins and before populating the desktop combo.
        /// Returns a status-bar warning when the saved engine is unavailable, otherwise empty.
        /// Loading never saves the fallback over a temporarily missing plug-in preference.
        /// </summary>
        public static string LoadSavedSelection()
        {
            var settings = EngineSelectionSettings.Load(EngineSelectionSettings.DefaultPath);
            engineName = settings.Resolve(UiEngineNames, out var statusMessage, NestingEngineRegistry.ResolveName);
            return statusMessage ?? string.Empty;
        }

        /// <summary>Desktop combo contents: registered jobs engines minus StockLadder.</summary>
        public static IEnumerable<string> UiEngineNames =>
            NestingEngineRegistry.AvailableEngines
                .Where(e => !e.Name.Equals(HiddenEngineName, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Name)
                .ToList();

        /// <summary>Placement strategy for single-plate fill: the selection when it names a
        /// built-in strategy, otherwise Default.</summary>
        public static string FillStrategy => IsFillStrategy(engineName) ? engineName : DefaultEngineName;

        public static bool IsFillStrategy(string name) =>
            !string.IsNullOrWhiteSpace(name)
            && PlateFillService.BuiltInStrategies.Any(
                s => s.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)
            );
    }
}
