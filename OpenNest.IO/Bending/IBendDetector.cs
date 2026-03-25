using ACadSharp;
using OpenNest.Bending;
using System.Collections.Generic;

namespace OpenNest.IO.Bending
{
    public interface IBendDetector
    {
        string Name { get; }
        List<Bend> DetectBends(CadDocument document);
    }
}
