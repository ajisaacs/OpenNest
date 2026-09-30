using OpenNest.Geometry;

namespace OpenNest.Engine.CirclePacking
{
    internal class Item : Circle
    {
        public int PackingId { get; set; }

        public override Item Clone()
        {
            var copy = new Item
            {
                Radius = this.Radius,
                Center = this.Center,
                Rotation = this.Rotation,
                PackingId = this.PackingId,
            };
            CopyBaseTo(copy);
            return copy;
        }
    }
}
