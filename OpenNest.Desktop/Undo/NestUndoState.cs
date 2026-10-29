using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest.Undo
{
    /// <summary>One whole-nest state in a document's undo history.</summary>
    public sealed record NestUndoState(NestSnapshot Snapshot) : IUndoState
    {
        public long SizeInBytes => Snapshot.SizeInBytes;
    }
}
