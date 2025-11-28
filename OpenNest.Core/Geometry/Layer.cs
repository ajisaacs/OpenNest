using System.Drawing;

namespace OpenNest.Geometry
{
    public class Layer
    {
        public static readonly Layer Default = new Layer("0")
        {
            Color = Color.White,
            IsVisible = true
        };

        public Layer(string name)
        {
            Name = name;
        }

        public string Name { get; set; }

        public bool IsVisible { get; set; }

        public Color Color { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
