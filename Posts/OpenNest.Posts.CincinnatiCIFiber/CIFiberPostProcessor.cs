using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.CNC;

namespace OpenNest.Posts.CincinnatiCIFiber
{
    /// <summary>
    /// Post-processor for the CI Fiber family (e.g. the CI Fiber 4020 8kW with
    /// nLight CLX / Beckhoff TF5200 / Precitec ProCutter). Named by machine
    /// family, not table size: the same post drives other CI Fiber tables via
    /// the configured table envelope. Emits the TF5200 skippable-macro program
    /// structure of the machine sample (see <see cref="CIFiberProgramWriter"/>).
    /// </summary>
    public sealed class CIFiberPostProcessor
        : IConfigurablePostProcessor,
            IMultiFilePostProcessor
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public string Name => "Cincinnati CI Fiber";
        public string Author => "OpenNest";
        public string Description =>
            "CI Fiber family laser (TF5200 / nLight CLX), e.g. CI Fiber 4020 8kW";

        public CIFiberPostConfig Config { get; }

        object IConfigurablePostProcessor.Config => Config;

        public CIFiberPostProcessor()
        {
            var configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                Config =
                    JsonSerializer.Deserialize<CIFiberPostConfig>(json, JsonOptions)
                    ?? new CIFiberPostConfig();
            }
            else
            {
                Config = new CIFiberPostConfig();
                SaveConfig();
            }
        }

        public CIFiberPostProcessor(CIFiberPostConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void SaveConfig()
        {
            var configPath = GetConfigPath();
            var json = JsonSerializer.Serialize(Config, JsonOptions);
            File.WriteAllText(configPath, json);
        }

        private static string GetConfigPath()
        {
            var assemblyPath = typeof(CIFiberPostProcessor).Assembly.Location;
            var dir = Path.GetDirectoryName(assemblyPath);
            var name = Path.GetFileNameWithoutExtension(assemblyPath);
            return Path.Combine(dir, name + ".json");
        }

        /// <summary>
        /// Writes every sheet into one program. A stream holds a single program,
        /// so this throws when "One program per sheet" is on and the nest has more
        /// than one sheet; use <see cref="Post(Nest, string)"/> for that.
        /// </summary>
        public void Post(Nest nest, Stream outputStream)
        {
            if (nest == null)
                throw new ArgumentNullException(nameof(nest));
            if (outputStream == null)
                throw new ArgumentNullException(nameof(outputStream));

            if (SplitsSheets(nest))
                throw new InvalidOperationException(
                    "\"One program per sheet\" writes several files; post this nest to a file path."
                );

            var program = Render(w => new CIFiberProgramWriter(Config).Write(nest, w));
            outputStream.Write(program, 0, program.Length);
        }

        /// <summary>
        /// Writes the program(s) for the nest. With "One program per sheet" and more
        /// than one sheet, <c>JOB.cnc</c> becomes <c>JOB-1.cnc</c>, <c>JOB-2.cnc</c>, ...;
        /// otherwise the single program goes to <paramref name="outputFile"/>. All
        /// programs are generated before any file is written, so a validation
        /// failure leaves no partial output.
        /// </summary>
        public void Post(Nest nest, string outputFile)
        {
            if (nest == null)
                throw new ArgumentNullException(nameof(nest));
            if (string.IsNullOrEmpty(outputFile))
                throw new ArgumentNullException(nameof(outputFile));

            var files = GetOutputFiles(nest, outputFile);
            var programs = new List<byte[]>();

            if (files.Count == 1)
            {
                programs.Add(Render(w => new CIFiberProgramWriter(Config).Write(nest, w)));
            }
            else
            {
                var sheets = CIFiberProgramWriter.PostedSheets(nest);
                var writer = new CIFiberProgramWriter(Config);
                writer.Validate(sheets, singleProgram: false);
                for (var i = 0; i < sheets.Count; i++)
                {
                    var sheetNumber = i + 1;
                    var sheet = sheets[i];
                    programs.Add(Render(w => writer.WriteSheet(nest, sheet, sheetNumber, w)));
                }
            }

            for (var i = 0; i < files.Count; i++)
                File.WriteAllBytes(files[i], programs[i]);
        }

        public IReadOnlyList<string> GetOutputFiles(Nest nest, string outputFile)
        {
            if (nest == null)
                throw new ArgumentNullException(nameof(nest));
            if (string.IsNullOrEmpty(outputFile))
                throw new ArgumentNullException(nameof(outputFile));

            if (!SplitsSheets(nest))
                return new[] { outputFile };

            var dir = Path.GetDirectoryName(outputFile) ?? "";
            var name = Path.GetFileNameWithoutExtension(outputFile);
            var ext = Path.GetExtension(outputFile);
            var count = CIFiberProgramWriter.PostedSheets(nest).Count;

            return Enumerable
                .Range(1, count)
                .Select(n => Path.Combine(dir, $"{name}-{n}{ext}"))
                .ToList();
        }

        private bool SplitsSheets(Nest nest) =>
            Config.OneProgramPerSheet && CIFiberProgramWriter.PostedSheets(nest).Count > 1;

        // CRLF file, ASCII (UTF-8 without BOM), matching the machine sample.
        private static byte[] Render(Action<TextWriter> write)
        {
            using var ms = new MemoryStream();
            using (var writer = new StreamWriter(ms, new UTF8Encoding(false), 1024, leaveOpen: true))
            {
                write(writer);
            }
            return ms.ToArray();
        }
    }
}
