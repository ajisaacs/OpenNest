using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace OpenNest
{
    public class Drawing
    {
        private static int nextId;
        private Program program;

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

        public NestConstraints Constraints { get; set; }

        public SourceInfo Source { get; set; }

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
