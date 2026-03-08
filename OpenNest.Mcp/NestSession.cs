using System.Collections.Generic;

namespace OpenNest.Mcp
{
    public class NestSession
    {
        public Nest Nest { get; set; }
        public List<Plate> Plates { get; } = new();
        public List<Drawing> Drawings { get; } = new();

        public Plate GetPlate(int index)
        {
            if (Nest != null && index < Nest.Plates.Count)
                return Nest.Plates[index];

            var adjustedIndex = index - (Nest?.Plates.Count ?? 0);
            if (adjustedIndex >= 0 && adjustedIndex < Plates.Count)
                return Plates[adjustedIndex];

            return null;
        }

        public Drawing GetDrawing(string name)
        {
            if (Nest != null)
            {
                foreach (var d in Nest.Drawings)
                {
                    if (d.Name == name)
                        return d;
                }
            }

            foreach (var d in Drawings)
            {
                if (d.Name == name)
                    return d;
            }

            return null;
        }

        public List<Plate> AllPlates()
        {
            var all = new List<Plate>();
            if (Nest != null)
                all.AddRange(Nest.Plates);
            all.AddRange(Plates);
            return all;
        }

        public List<Drawing> AllDrawings()
        {
            var all = new List<Drawing>();
            if (Nest != null)
                all.AddRange(Nest.Drawings);
            all.AddRange(Drawings);
            return all;
        }
    }
}
