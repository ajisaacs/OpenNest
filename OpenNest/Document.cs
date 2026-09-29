using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest
{
    public class Document
    {
        public Nest Nest { get; set; }

        public DateTime LastSaveDate { get; private set; }

        public string LastSavePath { get; private set; }

        private readonly NestSaveSession remoteSession = new();

        public Guid RemoteId => remoteSession.RemoteId;

        public void BindRemote(Guid id, string serverUrl) => remoteSession.Bind(id, serverUrl);

        public string Name => Nest?.Name;

        public bool HasSavePath => File.Exists(LastSavePath);

        public void SaveAs(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            Nest.Name = name;
            LastSaveDate = DateTime.Now;
            LastSavePath = path;

            var writer = new NestWriter(Nest);
            writer.Write(path);
        }

        /// <summary>Serializes the current nest to a .nest archive in memory.</summary>
        public byte[] Serialize()
        {
            using var stream = new MemoryStream();
            new NestWriter(Nest).Write(stream);
            return stream.ToArray();
        }

        /// <summary>Uploads this nest, preserving its server identity only on success.</summary>
        public async Task<NestRecord> SaveToDatabaseAsync(
            INestRepository repository, string serverUrl, bool saveCopy = false,
            CancellationToken cancellationToken = default)
        {
            var bytes = Serialize();
            var saved = await remoteSession.SaveAsync(
                repository, serverUrl, Nest, bytes, saveCopy, cancellationToken);
            LastSaveDate = DateTime.Now;
            return saved;
        }
    }
}
