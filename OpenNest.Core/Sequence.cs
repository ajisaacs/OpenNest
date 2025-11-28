using System.Collections.Generic;

namespace OpenNest
{
    public interface ISequencer
    {
        List<Part> SequenceParts(IList<Part> parts);
        List<Part> SequenceParts(IList<Part> parts, Vector origin);
    }

    public class SequenceByNearest : ISequencer
    {
        public List<Part> SequenceParts(IList<Part> parts)
        {
            return SequenceParts(parts, Vector.Zero);
        }

        public List<Part> SequenceParts(IList<Part> parts, Vector origin)
        {
            if (parts.Count == 0)
                return new List<Part>();

            var dupList = new List<Part>(parts);
            var seqList = new List<Part>(parts.Count);

            var lastPart = GetClosestPart(origin, dupList);
            seqList.Add(lastPart);
            dupList.Remove(lastPart);

            for (int i = 0; i < parts.Count - 1 /*STOP BEFORE LAST PART*/; i++)
            {
                var nextPart = GetClosestPart(lastPart.Location, dupList);

                if (nextPart == null)
                    break;

                seqList.Add(nextPart);
                dupList.Remove(nextPart);
                lastPart = nextPart;
            }

            return seqList;
        }

        private Part GetClosestPart(Vector pt, IList<Part> parts)
        {
            if (parts.Count == 0)
                return null;

            var closestPart = parts[0];
            var closestDistance = parts[0].Location.DistanceTo(pt);

            for (int i = 1; i < parts.Count; i++)
            {
                var distance = parts[i].Location.DistanceTo(pt);

                if (distance < closestDistance)
                {
                    closestPart = parts[i];
                    closestDistance = distance;
                }
            }

            return closestPart;
        }
    }
}
