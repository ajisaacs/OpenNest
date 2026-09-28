using System.Collections.Generic;

namespace OpenNest
{
    /// <summary>
    /// A post-processor whose <see cref="IPostProcessor.Post(Nest, string)"/>
    /// can write more than one file (for example one program per sheet).
    /// </summary>
    public interface IMultiFilePostProcessor : IPostProcessor
    {
        /// <summary>
        /// The files <see cref="IPostProcessor.Post(Nest, string)"/> will write
        /// for this nest and chosen path, in order, with the current settings.
        /// </summary>
        IReadOnlyList<string> GetOutputFiles(Nest nest, string outputFile);
    }
}
