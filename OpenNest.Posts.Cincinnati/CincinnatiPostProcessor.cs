using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.CNC;

namespace OpenNest.Posts.Cincinnati
{
    public sealed class CincinnatiPostProcessor : IConfigurablePostProcessor
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string Name => "Cincinnati CL-707";
        public string Author => "OpenNest";
        public string Description => "Cincinnati CL-707/CL-800/CL-900/CL-940/CLX family";

        public CincinnatiPostConfig Config { get; }

        object IConfigurablePostProcessor.Config => Config;

        public CincinnatiPostProcessor()
        {
            var configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                Config = JsonSerializer.Deserialize<CincinnatiPostConfig>(json, JsonOptions);
            }
            else
            {
                Config = new CincinnatiPostConfig();
                SaveConfig();
            }
        }

        public CincinnatiPostProcessor(CincinnatiPostConfig config)
        {
            Config = config;
        }

        public void SaveConfig()
        {
            var configPath = GetConfigPath();
            var json = JsonSerializer.Serialize(Config, JsonOptions);
            File.WriteAllText(configPath, json);
        }

        private static string GetConfigPath()
        {
            var assemblyPath = typeof(CincinnatiPostProcessor).Assembly.Location;
            var dir = Path.GetDirectoryName(assemblyPath);
            var name = Path.GetFileNameWithoutExtension(assemblyPath);
            return Path.Combine(dir, name + ".json");
        }

        public void Post(Nest nest, Stream outputStream)
        {
            // 1. Create variable manager and register standard variables
            var vars = CreateVariableManager();

            // 2. Filter to non-empty plates
            var plates = nest.Plates
                .Where(p => p.Parts.Count > 0)
                .ToList();

            // 3. Resolve gas and library files
            var resolver = new MaterialLibraryResolver(Config);
            var gas = MaterialLibraryResolver.ResolveGas(nest, Config);
            var etchLibrary = resolver.ResolveEtchLibrary(Config.DefaultEtchGas);

            // Resolve cut library from nest material/thickness for preamble
            var firstPlate = plates.FirstOrDefault();
            var initialCutLibrary = resolver.ResolveCutLibrary(nest.Material?.Name ?? "", nest.Thickness, gas);

            // 4. Build part sub-program registry (if enabled)
            Dictionary<(int, long), int> partSubprograms = null;
            List<(int subNum, string name, Program program)> subprogramEntries = null;

            if (Config.UsePartSubprograms)
                (partSubprograms, subprogramEntries) = CincinnatiPartSubprogramWriter.BuildRegistry(plates, Config.PartSubprogramStart);

            // 5. Create writers
            var preamble = new CincinnatiPreambleWriter(Config);
            var sheetWriter = new CincinnatiSheetWriter(Config, vars);

            // 6. Build material description from nest
            var material = nest.Material;
            var materialDesc = material != null
                ? $"{material.Name}{(string.IsNullOrEmpty(material.Grade) ? "" : $", {material.Grade}")}"
                : "";

            // 7. Write to stream
            using var writer = new StreamWriter(outputStream, Encoding.UTF8, 1024, leaveOpen: true);

            // Main program
            preamble.WriteMainProgram(writer, nest.Name ?? "NEST", materialDesc, plates.Count, initialCutLibrary);

            // Variable declaration subprogram
            preamble.WriteVariableDeclaration(writer, vars);

            // Sheet subprograms
            for (var i = 0; i < plates.Count; i++)
            {
                var plate = plates[i];
                var sheetIndex = i + 1;
                var subNumber = Config.SheetSubprogramStart + i;
                var cutLibrary = resolver.ResolveCutLibrary(nest.Material?.Name ?? "", nest.Thickness, gas);
                var isLastSheet = i == plates.Count - 1;
                sheetWriter.Write(writer, plate, nest.Name ?? "NEST", sheetIndex, subNumber,
                    cutLibrary, etchLibrary, partSubprograms, isLastSheet);
            }

            // Part sub-programs (if enabled)
            if (subprogramEntries != null)
            {
                var partSubWriter = new CincinnatiPartSubprogramWriter(Config);
                var sheetDiagonal = firstPlate != null
                    ? System.Math.Sqrt(firstPlate.Size.Width * firstPlate.Size.Width
                        + firstPlate.Size.Length * firstPlate.Size.Length)
                    : 100.0;

                foreach (var (subNum, name, pgm) in subprogramEntries)
                {
                    partSubWriter.Write(writer, pgm, name, subNum,
                        initialCutLibrary, etchLibrary, sheetDiagonal);
                }
            }

            writer.Flush();
        }

        public void Post(Nest nest, string outputFile)
        {
            using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
            Post(nest, fs);
        }

        private ProgramVariableManager CreateVariableManager()
        {
            var vars = new ProgramVariableManager();
            vars.GetOrCreate("ProcessFeedrate", 148);  // Set by G89, no expression
            vars.GetOrCreate("LeadInFeedrate", 126, $"[#148*{Config.LeadInFeedratePercent}]");
            vars.GetOrCreate("LeadInArcLine2Feedrate", 127, $"[#148*{Config.LeadInArcLine2FeedratePercent}]");
            vars.GetOrCreate("CircleFeedrate", 128, Config.CircleFeedrateMultiplier.ToString("0.#"));
            vars.GetOrCreate("LeadOutFeedrate", 129, $"[#148*{Config.LeadOutFeedratePercent}]");

            if (Config.ArcFeedrate == ArcFeedrateMode.Variables)
            {
                foreach (var range in Config.ArcFeedrateRanges)
                {
                    var name = $"ArcFeedR{range.MaxRadius.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
                    vars.GetOrCreate(name, range.VariableNumber,
                        $"[#148*{range.FeedratePercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}]");
                }
            }

            return vars;
        }
    }
}
