namespace OpenNest
{
    public interface IPostProcessorNestAware
    {
        void PrepareForNest(Nest nest);
    }
}
