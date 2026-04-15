using System.Collections.Generic;

namespace OpenNest
{
    public interface IMaterialProvidingPostProcessor
    {
        IEnumerable<string> GetMaterialNames();
    }
}
