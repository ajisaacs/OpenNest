using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace OpenNest
{
    public class Drawing
    {
        private static int nextId;
        private static int nextColorIndex;
        private Program program;

        public static readonly Color[] PartColors = new Color[]
        {
            Color.FromArgb(205, 92, 92),    // Indian Red
            Color.FromArgb(148, 103, 189),  // Medium Purple
            Color.FromArgb(75, 180, 175),   // Teal
            Color.FromArgb(210, 190, 75),   // Goldenrod
            Color.FromArgb(190, 85, 175),   // Orchid
            Color.FromArgb(185, 115, 85),   // Sienna
            Color.FromArgb(120, 100, 190),  // Slate Blue
            Color.FromArgb(200, 100, 140),  // Rose
            Color.FromArgb(80, 175, 155),   // Sea Green
            Color.FromArgb(195, 160, 85),   // Dark Khaki
            Color.FromArgb(175, 95, 160),   // Plum
            Color.FromArgb(215, 130, 130),  // Light Coral
        };

        public static Color GetNextColor()
        {
            var color = PartColors[nextColorIndex % PartColors.Length];
            nextColorIndex++;
            return color;
        }

        public Drawing()
            : this(string.Empty, new Program())
        {
        }

        public Drawing(string name)
            : this(name, new Program())
        {
        }

        public Drawing(string name, Program pgm)
        {
            Id = Interlocked.Increment(ref nextId);
            Name = name;
            Material = new Material();
            Program = pgm;
            Constraints = new NestConstraints();
            Source = new SourceInfo();
        }

        public int Id { get; }

        public string Name { get; set; }

        public string Customer { get; set; }

        public int Priority { get; set; }

        public DwgQty Quantity;

        public Material Material { get; set; }

        public Program Program
        {
            get { return program; }
            set
            {
                program = value;
                UpdateArea();
            }
        }

        public Color Color { get; set; }

        public bool IsCutOff { get; set; }

        public NestConstraints Constraints { get; set; }

        public SourceInfo Source { get; set; }

        public List<Bend> Bends { get; set; } = new List<Bend>();

        public double Area { get; protected set; }

        public void UpdateArea()
        {
            var geometry = ConvertProgram.ToGeometry(Program).Where(entity => entity.Layer != SpecialLayers.Rapid);
            var shapes = ShapeBuilder.GetShapes(geometry);

            if (shapes.Count == 0)
                return;

            var areas = new double[shapes.Count];

            for (int i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                areas[i] = shape.Area();
            }

            int largestAreaIndex = 0;

            for (int i = 1; i < areas.Length; i++)
            {
                var area = areas[i];

                if (area > areas[largestAreaIndex])
                    largestAreaIndex = i;
            }

            var outerArea = areas[largestAreaIndex];

            Area = outerArea - (areas.Sum() - outerArea);
        }

        public override bool Equals(object obj)
        {
            if (obj is Drawing == false)
                return false;

            var dwg = (Drawing)obj;

            return Name == dwg.Name;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }

    public class SourceInfo
    {
        /// <summary>
        /// Path to the source file.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Offset distances to the original location.
        /// </summary>
        public Vector Offset { get; set; }
    }
}
