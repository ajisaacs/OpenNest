using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.IO;
using OpenNest.Undo;

namespace OpenNest
{
    public class Document
    {
        public Nest Nest { get; set; }

        /// <summary>
        /// This document's undo history: whole-nest states, reset with the first one when the
        /// nest window opens. Saving marks a save point; it never clears the history.
        /// </summary>
        public UndoHistory<NestUndoState> History { get; } = new();

        public DateTime LastSaveDate { get; private set; }

        public string LastSavePath { get; private set; }

        private readonly NestSaveSession remoteSession = new();

        public Guid RemoteId => remoteSession.RemoteId;
        public Guid PendingCreateId => remoteSession.PendingCreateId;
        public bool PendingCreateRequiresReconciliation => remoteSession.PendingCreateId != Guid.Empty && remoteSession.RequiresReload;
        public bool PendingCreateWasCopy => remoteSession.PendingCreateWasCopy;
        public bool IsFirstDatabaseSave(string serverUrl) => remoteSession.IsFirstSave(serverUrl);

        /// <summary>
        /// A duplicate name the user already accepted in Nest Info, so the first Save
        /// does not ask about it again.
        /// </summary>
        public string AcceptedDuplicateName { get; set; }

        public void BindRemote(Guid id, string serverUrl) => remoteSession.Bind(id, serverUrl);

        public void BindRemote(Guid id, string serverUrl, int revision) => remoteSession.Bind(id, serverUrl, revision);

        public Task<NestRecord> ReconcilePendingCreateAsync(INestRepository repository, string serverUrl,
            CancellationToken cancellationToken = default) =>
            remoteSession.ReconcilePendingCreateAsync(repository, serverUrl, cancellationToken);

        public int RemoteRevision => remoteSession.Revision;

        public string Name => Nest?.Name;

        public bool HasSavePath => File.Exists(LastSavePath);

        public void SaveAs(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            Nest.Name = name;
            LastSaveDate = DateTime.Now;
            LastSavePath = path;

            var writer = new NestWriter(Nest) { BestFitStore = BestFitCacheStore.Shared };
            writer.Write(path);
            if (History.Current != null)
                History.MarkSaved();
        }

        /// <summary>Serializes the current nest to a .nest archive in memory.</summary>
        public byte[] Serialize()
        {
            using var stream = new MemoryStream();
            new NestWriter(Nest) { BestFitStore = BestFitCacheStore.Shared }.Write(stream);
            return stream.ToArray();
        }

        /// <summary>Uploads this nest, preserving its server identity only on success.</summary>
        public async Task<NestRecord> SaveToDatabaseAsync(
            INestRepository repository, string serverUrl, bool saveCopy = false,
            CancellationToken cancellationToken = default)
        {
            var bytes = Serialize();
            // The save point is the state serialized here, unless an edit was recorded since.
            var state = History.Current;
            var saved = await remoteSession.SaveAsync(
                repository, serverUrl, Nest, bytes, saveCopy, cancellationToken);
            LastSaveDate = DateTime.Now;
            if (state != null && ReferenceEquals(History.Current, state))
                History.MarkSaved();
            return saved;
        }
    }
}
