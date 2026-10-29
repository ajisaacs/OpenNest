using System;

namespace OpenNest.Undo
{
    /// <summary>
    /// Records one undoable edit. Call <see cref="MarkChanged"/> once the nest is (about to be)
    /// changed and <see cref="Complete"/> when the edit succeeded; disposing then commits one
    /// history entry. Disposing without <see cref="Complete"/> (an exception, a cancellation)
    /// rolls a changed nest back to the last recorded state. A scope opened while another is
    /// open merges into the outermost one: its changes join that entry, and leaving it
    /// incomplete rolls back the whole entry. A scope with nothing marked changed records and
    /// restores nothing.
    /// </summary>
    public sealed class ChangeScope : IDisposable
    {
        private readonly ChangeScope root;
        private readonly Action<ChangeScope> end;
        private bool completed;
        private bool disposed;
        private bool changed;
        private bool failed;

        private ChangeScope(string label, object coalesceKey, ChangeScope root, Action<ChangeScope> end)
        {
            Label = label;
            CoalesceKey = coalesceKey;
            this.root = root;
            this.end = end;
        }

        /// <summary>A scope with no history behind it, for views that are not a nest document.</summary>
        public static ChangeScope Detached() => new(null, null, null, null);

        /// <summary>An outermost scope; <paramref name="end"/> runs once when it is disposed.</summary>
        internal static ChangeScope Root(string label, object coalesceKey, Action<ChangeScope> end) =>
            new(label, coalesceKey, null, end);

        /// <summary>A scope inside <paramref name="outer"/>; it merges into the outermost one.</summary>
        internal static ChangeScope Nested(ChangeScope outer) => new(outer.Label, null, outer.Outermost, null);

        public string Label { get; }

        /// <summary>Repeated edits with an equal key shortly after each other undo as one.</summary>
        public object CoalesceKey { get; }

        internal ChangeScope Outermost => root ?? this;

        /// <summary>True once this scope or any scope merged into it marked the nest changed.</summary>
        public bool IsChanged => Outermost.changed;

        /// <summary>True when the outermost scope completed and no merged scope was left incomplete.</summary>
        internal bool Succeeded => completed && !failed;

        public void MarkChanged() => Outermost.changed = true;

        public void Complete() => completed = true;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (root != null)
            {
                if (!completed)
                    root.failed = true;
                return;
            }
            end?.Invoke(this);
        }
    }
}
