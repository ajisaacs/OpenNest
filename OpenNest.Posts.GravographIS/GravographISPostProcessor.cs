using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest.Posts.GravographIS
{
    /// <summary>
    /// IPostProcessor implementation for the Gravograph IS8000. <see cref="Post(Nest, Stream)"/>
    /// writes the binary HPGL bytes. For serial streaming, use <see cref="Stream(Nest, string, Handshake, CancellationToken)"/>.
    ///
    /// Geometry is split by <see cref="OpenNest.CNC.LayerType"/> into an engrave pass
    /// (Scribe) and a cut pass (Cut), each with its own feed/depth from <see cref="Config"/>.
    /// The cut pass pauses by default so the operator can swap/adjust the tool.
    /// </summary>
    public sealed class GravographISPostProcessor : IConfigurablePostProcessor
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string Name => "Gravograph IS8000";
        public string Author => "OpenNest";
        public string Description => "Gravograph IS8000 mechanical engraver (binary HPGL over serial)";

        public GravographISWriterOptions WriterOptions { get; } = new GravographISWriterOptions();

        public NestPolylineExtractor Extractor { get; } = new NestPolylineExtractor();

        public double StitchTolerance { get; set; } = PolylinePrePass.DefaultStitchTolerance;

        public bool AllowReverse { get; set; } = true;

        public GravographISPostConfig Config { get; }

        object IConfigurablePostProcessor.Config => Config;

        public GravographISPostProcessor()
        {
            var configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                Config = JsonSerializer.Deserialize<GravographISPostConfig>(json, JsonOptions)
                    ?? new GravographISPostConfig();
            }
            else
            {
                Config = new GravographISPostConfig();
                SaveConfig();
            }
        }

        public GravographISPostProcessor(GravographISPostConfig config)
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
            var assemblyPath = typeof(GravographISPostProcessor).Assembly.Location;
            var dir = Path.GetDirectoryName(assemblyPath);
            var name = Path.GetFileNameWithoutExtension(assemblyPath);
            return Path.Combine(dir, name + ".json");
        }

        public void Post(Nest nest, Stream outputStream)
        {
            if (nest == null) throw new ArgumentNullException(nameof(nest));
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

            var passes = BuildPasses(Extractor.ExtractLayered(nest));
            new GravographISWriter(WriterOptions).Write(passes, outputStream);
        }

        public void Post(Nest nest, string outputFile)
        {
            using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
            Post(nest, fs);
        }

        /// <summary>
        /// Groups layer-tagged polylines into ordered tool passes: engrave (Scribe)
        /// first, then cut. Each group is stitch/reverse-optimized independently.
        /// Geometry whose layer maps to no config (Display) is skipped. When only one
        /// group is present, a single pass is returned (and so the writer emits no pause).
        /// </summary>
        public IReadOnlyList<GravographPass> BuildPasses(IEnumerable<LayeredPolyline> polylines)
        {
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));

            var engrave = new List<IReadOnlyList<Vector>>();
            var cut = new List<IReadOnlyList<Vector>>();

            foreach (var poly in polylines)
            {
                if (poly == null) continue;
                var block = Config.ConfigFor(poly.Layer);
                if (block == null)
                    continue; // non-cutting (Display) geometry
                if (ReferenceEquals(block, Config.Engrave))
                    engrave.Add(poly.Points);
                else
                    cut.Add(poly.Points);
            }

            var passes = new List<GravographPass>();
            if (engrave.Count > 0)
                passes.Add(MakePass(Config.Engrave, engrave));
            if (cut.Count > 0)
                passes.Add(MakePass(Config.Cut, cut));

            return passes;
        }

        private GravographPass MakePass(LayerCutConfig block, List<IReadOnlyList<Vector>> polylines)
        {
            return new GravographPass
            {
                Polylines = PolylinePrePass.Prepare(polylines, StitchTolerance, AllowReverse),
                FeedMmPerSec = block.FeedMmPerSec,
                DepthInches = block.Depth,
                PauseBefore = block.PauseBefore,
                PauseMessage = block.PauseMessage ?? "",
            };
        }

        /// <summary>
        /// Buffers the encoded job in memory, then streams it to the named COM port.
        /// </summary>
        public void Stream(Nest nest, string portName,
            Handshake handshake = Handshake.RequestToSend,
            CancellationToken cancellationToken = default)
        {
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                Post(nest, ms);
                bytes = ms.ToArray();
            }

            using var port = new GravographISPort();
            port.Open(portName, handshake);
            port.StreamJob(bytes, cancellationToken);
        }
    }
}
