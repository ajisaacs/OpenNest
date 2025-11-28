using System.IO;

namespace OpenNest
{
    public interface IPostProcessor
    {
        string Name { get; }

        string Author { get; }

        string Description { get; }

        void Post(Nest nest, Stream outputStream);

        void Post(Nest nest, string outputFile);
    }
}
