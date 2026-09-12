using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Bending;
using OpenNest.Geometry;

namespace OpenNest.Controls
{
    public class CadText
    {
        public ulong? SourceHandle { get; set; }

        public bool IsReplacedByBendNote(IEnumerable<Bend> bends) =>
            SourceHandle.HasValue && bends != null && bends.Any(b =>
                b.SourceNoteHandle == SourceHandle && !string.IsNullOrEmpty(b.NoteText));

        public Vector Position { get; set; }
        public string Value { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public string LayerName { get; set; }
        public StringAlignment HAlign { get; set; }
        public StringAlignment VAlign { get; set; }
    }
}
