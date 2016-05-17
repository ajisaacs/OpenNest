using OpenNest.Geometry;

namespace OpenNest.CirclePacking
{
    internal class Item : Circle
    {
        public int Id { get; set; }

        public object Clone()
        {
            return new Item
            {
                Radius = this.Radius,
                Center = this.Center,
                Id = this.Id
            };
        }
    }
}
