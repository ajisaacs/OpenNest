using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.RectanglePacking
{
    internal class Item : Box
    {
        public int Id { get; set; }

        public bool IsRotated { get; private set; }

        public void Rotate()
        {
            Generic.Swap(ref Size.Width, ref Size.Length);
            IsRotated = !IsRotated;
        }

        public object Clone()
        {
            return new Item
            {
                IsRotated = this.IsRotated,
                Location = this.Location,
                Size = this.Size,
                Id = this.Id,
            };
        }
    }
}
