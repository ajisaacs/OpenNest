
namespace OpenNest
{
    public interface IBoundable
    {
        Box BoundingBox { get; }

        double Left { get; }

        double Right { get; }

        double Top { get; }

        double Bottom { get; }

        void UpdateBounds();
    }
}
