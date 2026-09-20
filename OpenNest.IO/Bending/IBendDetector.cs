using System.Collections.Generic;
using ACadSharp;
using OpenNest.Bending;

namespace OpenNest.IO.Bending
{
    public interface IBendDetector
    {
        string Name { get; }
        List<Bend> DetectBends(CadDocument document);
    }
}
