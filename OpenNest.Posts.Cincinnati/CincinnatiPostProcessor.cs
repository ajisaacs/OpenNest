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
    public sealed class CincinnatiPostProcessor : IConfigurablePostProcessor, IPostProcessorNestAware, IMaterialProvidingPostProcessor
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

        public IEnumerable<string> GetMaterialNames()
        {
            if (Config?.MaterialLibraries == null)
                return System.Array.Empty<string>();

            return Config.MaterialLibraries
                .Select(e => e.Material)
                .Where(s => !string.IsNullOrWhiteSpace(s));
        }

        public void PrepareForNest(Nest nest)
        {
            var materialName = nest?.Material?.Name ?? "";
            var thickness = nest?.Thickness ?? 0.0;
            Config.SelectedLibrary = Config.FindBestLibrary(materialName, thickness);
        }

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

            // 3. Register user variables from drawing programs
            var userVarMapping = RegisterUserVariables(vars, plates);

            // 4. Resolve gas and library files
            var resolver = new MaterialLibraryResolver(Config);
            var gas = MaterialLibraryResolver.ResolveGas(nest, Config);
            var etchLibrary = resolver.ResolveEtchLibrary(Config.DefaultEtchGas);

            // Resolve cut library from nest material/thickness for preamble
            var firstPlate = plates.FirstOrDefault();
            var initialCutLibrary = resolver.ResolveCutLibrary(nest.Material?.Name ?? "", nest.Thickness, gas);

            // 5. Build part sub-program registry (if enabled)
            Dictionary<(int, long), int> partSubprograms = null;
            List<(int subNum, string name, Program program)> subprogramEntries = null;

            if (Config.UsePartSubprograms)
                (partSubprograms, subprogramEntries) = CincinnatiPartSubprogramWriter.BuildRegistry(plates, Config.PartSubprogramStart);

            // 5b. Build hole sub-program registry (SubProgramCalls across all parts)
            var holeStartNumber = Config.PartSubprogramStart
                + (subprogramEntries?.Count ?? 0);
            var (holeMapping, holeEntries) = CincinnatiPartSubprogramWriter.BuildHoleRegistry(plates, holeStartNumber);

            // 6. Create writers
            var preamble = new CincinnatiPreambleWriter(Config);
            var sheetWriter = new CincinnatiSheetWriter(Config, vars,
                holeMapping.Count > 0 ? holeMapping : null);

            // 7. Build material description from nest
            var material = nest.Material;
            var materialDesc = material != null
                ? $"{material.Name}{(string.IsNullOrEmpty(material.Grade) ? "" : $", {material.Grade}")}"
                : "";

            // 8. Write to stream
            using var writer = new StreamWriter(outputStream, Encoding.UTF8, 1024, leaveOpen: true);

            // Main program
            preamble.WriteMainProgram(writer, nest.Name ?? "NEST", materialDesc, plates, initialCutLibrary);

            // Variable declaration subprogram
            preamble.WriteVariableDeclaration(writer, vars);

            // Sheet subprograms (one per unique layout, quantity handled via L count in main)
            for (var i = 0; i < plates.Count; i++)
            {
                var plate = plates[i];
                var layoutIndex = i + 1;
                var subNumber = Config.SheetSubprogramStart + i;
                var cutLibrary = resolver.ResolveCutLibrary(nest.Material?.Name ?? "", nest.Thickness, gas);
                sheetWriter.Write(writer, plate, nest.Name ?? "NEST", layoutIndex, subNumber,
                    cutLibrary, etchLibrary, partSubprograms, userVarMapping);
            }

            // Part sub-programs (if enabled)
            if (subprogramEntries != null)
            {
                var partSubWriter = new CincinnatiPartSubprogramWriter(Config,
                    holeMapping.Count > 0 ? holeMapping : null);
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

            // Hole sub-programs (SubProgramCall definitions)
            if (holeEntries.Count > 0)
            {
                var holeSubWriter = new CincinnatiPartSubprogramWriter(Config);
                var sheetDiagonal = firstPlate != null
                    ? System.Math.Sqrt(firstPlate.Size.Width * firstPlate.Size.Width
                        + firstPlate.Size.Length * firstPlate.Size.Length)
                    : 100.0;

                foreach (var (subNum, pgm) in holeEntries)
                {
                    CincinnatiPartSubprogramWriter.EnsureLeadingRapid(pgm);
                    holeSubWriter.Write(writer, pgm, "HOLE", subNum,
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

        private Dictionary<(int drawingId, string varName), int> RegisterUserVariables(
            ProgramVariableManager vars, List<Plate> plates)
        {
            var mapping = new Dictionary<(int drawingId, string varName), int>();
            var nextNumber = Config.UserVariableStart;

            // Track global variables by name so they share a single number
            var globalNumbers = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

            // Collect unique drawings from all plates
            var seenDrawings = new HashSet<int>();
            foreach (var plate in plates)
            {
                foreach (var part in plate.Parts)
                {
                    var drawing = part.BaseDrawing;
                    if (drawing.IsCutOff || !seenDrawings.Add(drawing.Id))
                        continue;

                    foreach (var kvp in drawing.Program.Variables)
                    {
                        var varDef = kvp.Value;

                        // Skip inline variables — they emit literal values
                        if (varDef.Inline)
                            continue;

                        if (varDef.Global)
                        {
                            if (!globalNumbers.TryGetValue(varDef.Name, out var globalNum))
                            {
                                globalNum = nextNumber++;
                                globalNumbers[varDef.Name] = globalNum;

                                // Register once in the variable manager
                                var commentName = ToPascalCase(varDef.Name);
                                var expression = FormatVariableValue(varDef.Value);
                                vars.GetOrCreate(commentName, globalNum, expression);
                            }

                            mapping[(drawing.Id, varDef.Name)] = globalNum;
                        }
                        else
                        {
                            var num = nextNumber++;
                            mapping[(drawing.Id, varDef.Name)] = num;

                            // Register with drawing name prefix in the comment
                            var drawingLabel = ToPascalCase(drawing.Name);
                            var varLabel = ToPascalCase(varDef.Name);
                            var commentName = $"{drawingLabel}{varLabel}";
                            var expression = FormatVariableValue(varDef.Value);
                            vars.GetOrCreate(commentName, num, expression);
                        }
                    }
                }
            }

            return mapping;
        }

        /// <summary>
        /// Converts a variable name from snake_case or camelCase to PascalCase.
        /// Examples: "sheet_width" → "SheetWidth", "holeSpacing" → "HoleSpacing"
        /// </summary>
        private static string ToPascalCase(string name)
        {
            var sb = new StringBuilder(name.Length);
            var capitalizeNext = true;

            foreach (var c in name)
            {
                if (c == '_')
                {
                    capitalizeNext = true;
                    continue;
                }

                if (capitalizeNext)
                {
                    sb.Append(char.ToUpper(c));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static string FormatVariableValue(double value)
        {
            return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
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
