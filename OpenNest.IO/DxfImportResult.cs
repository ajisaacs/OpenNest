using System.Collections.Generic;
using ACadSharp;
using OpenNest.Geometry;

namespace OpenNest.IO
{
    public class DxfImportResult
    {
        public List<Entity> Entities { get; set; } = new();
        public CadDocument Document { get; set; }
    }
}
