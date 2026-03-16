using System.Collections.Generic;

namespace OpenNest.Engine.Sequencing
{
    public readonly struct SequencedPart
    {
        public Part Part { get; init; }
    }

    public interface IPartSequencer
    {
        List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate);
    }
}
