using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Sequencing
{
    public class BottomSideSequencer : IPartSequencer
    {
        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            return parts
                .OrderBy(p => p.Location.Y)
                .ThenBy(p => p.Location.X)
                .Select(p => new SequencedPart { Part = p })
                .ToList();
        }
    }
}
