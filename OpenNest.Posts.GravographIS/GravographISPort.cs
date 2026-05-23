using System;
using System.IO.Ports;
using System.Threading;

namespace OpenNest.Posts.GravographIS
{
    /// <summary>
    /// Serial streamer for the Gravograph IS8000. 9600 8-N-1; flow control is
    /// configurable and defaults to RTS/CTS (the controller is buffered and drops
    /// CTS to apply backpressure). The job is sent in modest chunks rather than as
    /// one giant write so the handshake can pause the write mid-stream.
    /// </summary>
    public sealed class GravographISPort : IDisposable
    {
        private SerialPort port;

        public const int DefaultBaudRate = 9600;
        public const int DefaultChunkSize = 256;
        public const int DefaultWriteTimeoutMs = 30000;

        public int ChunkSize { get; set; } = DefaultChunkSize;
        public int WriteTimeoutMs { get; set; } = DefaultWriteTimeoutMs;

        public bool IsOpen => port != null && port.IsOpen;

        /// <summary>
        /// Opens the port at the controller's required line settings (9600 8-N-1)
        /// with the given <paramref name="handshake"/>. Throws if the port is
        /// already open or if opening fails.
        /// </summary>
        public void Open(string portName, Handshake handshake = Handshake.RequestToSend)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("Port name is required.", nameof(portName));
            if (port != null)
                throw new InvalidOperationException("Port is already open.");

            port = new SerialPort(portName, DefaultBaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = handshake,
                WriteTimeout = WriteTimeoutMs,
                ReadTimeout = WriteTimeoutMs,
                // DTR/RTS are needed for some USB-serial bridges and for RTS/CTS flow:
                DtrEnable = true,
                RtsEnable = handshake != Handshake.RequestToSend &&
                            handshake != Handshake.RequestToSendXOnXOff,
            };

            port.Open();
        }

        /// <summary>
        /// Streams the encoded job to the port in chunks. Cancellable. The chunked
        /// write is intentional — Write() blocks until the OS accepts the bytes,
        /// which with RTS/CTS or XOn/XOff yields cleanly when the controller's
        /// buffer is full.
        /// </summary>
        public void StreamJob(byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (port == null || !port.IsOpen)
                throw new InvalidOperationException("Port is not open.");

            var chunk = ChunkSize > 0 ? ChunkSize : DefaultChunkSize;
            var offset = 0;

            while (offset < data.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var count = System.Math.Min(chunk, data.Length - offset);
                port.Write(data, offset, count);
                offset += count;
            }

            // Block until the OS has handed the last bytes to the line. SerialPort
            // doesn't expose flush-and-drain directly; BaseStream.Flush is a no-op
            // on Windows, so this is best-effort.
            try { port.BaseStream.Flush(); }
            catch { /* ignored — Flush is advisory on SerialPort */ }
        }

        public void Close()
        {
            if (port == null) return;
            try
            {
                if (port.IsOpen) port.Close();
            }
            finally
            {
                port.Dispose();
                port = null;
            }
        }

        public void Dispose() => Close();
    }
}
