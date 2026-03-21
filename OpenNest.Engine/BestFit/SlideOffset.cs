namespace OpenNest.Engine.BestFit
{
    public readonly struct SlideOffset
    {
        public double Dx { get; }
        public double Dy { get; }
        public double DirX { get; }
        public double DirY { get; }

        public SlideOffset(double dx, double dy, double dirX, double dirY)
        {
            Dx = dx;
            Dy = dy;
            DirX = dirX;
            DirY = dirY;
        }
    }
}
