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
    public sealed class CIFiberPostProcessor : IConfigurablePostProcessor
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

        public void Post(Nest nest, Stream outputStream)
        {
            if (nest == null)
                throw new ArgumentNullException(nameof(nest));
            if (outputStream == null)
                throw new ArgumentNullException(nameof(outputStream));

            // CRLF file, ASCII (UTF-8 without BOM), matching the machine sample.
            var encoding = new UTF8Encoding(false);
            using var writer = new StreamWriter(
                outputStream,
                encoding,
                1024,
                leaveOpen: true
            );
            new CIFiberProgramWriter(Config).Write(nest, writer);
            writer.Flush();
        }

        public void Post(Nest nest, string outputFile)
        {
            using var fs = new FileStream(
                outputFile,
                FileMode.Create,
                FileAccess.Write
            );
            Post(nest, fs);
        }
    }
}
