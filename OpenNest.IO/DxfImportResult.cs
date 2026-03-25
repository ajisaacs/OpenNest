using ACadSharp;
using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.IO
{
    public class DxfImportResult
    {
        public List<Entity> Entities { get; set; } = new();
        public CadDocument Document { get; set; }
    }
}
