using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Sequencing
{
    public class RightSideSequencer : IPartSequencer
    {
        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            return parts
                .OrderByDescending(p => p.Location.X)
                .ThenBy(p => p.Location.Y)
                .Select(p => new SequencedPart { Part = p })
                .ToList();
        }
    }
}
