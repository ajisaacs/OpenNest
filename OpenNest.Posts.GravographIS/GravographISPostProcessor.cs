using System;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace OpenNest.Posts.GravographIS
{
    /// <summary>
    /// IPostProcessor implementation for the Gravograph IS8000. <see cref="Post(Nest, Stream)"/>
    /// writes the binary HPGL bytes. For serial streaming, use <see cref="Stream(Nest, string, Handshake, CancellationToken)"/>.
    /// </summary>
    public sealed class GravographISPostProcessor : IPostProcessor
    {
        public string Name => "Gravograph IS8000";
        public string Author => "OpenNest";
        public string Description => "Gravograph IS8000 mechanical engraver (binary HPGL over serial)";

        public GravographISWriterOptions WriterOptions { get; } = new GravographISWriterOptions();

        public NestPolylineExtractor Extractor { get; } = new NestPolylineExtractor();

        public double StitchTolerance { get; set; } = PolylinePrePass.DefaultStitchTolerance;

        public bool AllowReverse { get; set; } = true;

        public void Post(Nest nest, Stream outputStream)
        {
            if (nest == null) throw new ArgumentNullException(nameof(nest));
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

            var polylines = Extractor.Extract(nest);
            var prepared = PolylinePrePass.Prepare(polylines, StitchTolerance, AllowReverse);
            new GravographISWriter(WriterOptions).Write(prepared, outputStream);
        }

        public void Post(Nest nest, string outputFile)
        {
            using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
            Post(nest, fs);
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
