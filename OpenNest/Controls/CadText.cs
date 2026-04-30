using System.Drawing;
using OpenNest.Geometry;

namespace OpenNest.Controls
{
    public class CadText
    {
        public Vector Position { get; set; }
        public string Value { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public string LayerName { get; set; }
        public StringAlignment HAlign { get; set; }
        public StringAlignment VAlign { get; set; }
    }
}
