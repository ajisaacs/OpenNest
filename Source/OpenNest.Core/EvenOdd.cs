namespace OpenNest
{
    public static class EvenOdd
    {
        public static bool IsEven(this int i)
        {
            return (i % 2) == 0;
        }

        public static bool IsOdd(this int i)
        {
            return (i % 2) == 1;
        }
    }
}
