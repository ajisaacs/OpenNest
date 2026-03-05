namespace OpenNest
{
    public struct DwgQty
    {
        public int Nested { get; internal set; }

        public int Required { get; set; }

        public int Remaining
        {
            get 
            {
                var x = Required - Nested;
                return x < 0 ? 0: x; 
            }
        }
    }
}
