namespace OpenNest.Geometry
{
    internal static class EntityEndpoints
    {
        /// <summary>The start and end of a line or arc; false for any other entity.</summary>
        public static bool TryGet(Entity entity, out Vector start, out Vector end)
        {
            switch (entity)
            {
                case Line line:
                    start = line.StartPoint;
                    end = line.EndPoint;
                    return true;

                case Arc arc:
                    start = arc.StartPoint();
                    end = arc.EndPoint();
                    return true;

                default:
                    start = end = default;
                    return false;
            }
        }
    }
}
