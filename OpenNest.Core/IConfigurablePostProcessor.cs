namespace OpenNest
{
    public interface IConfigurablePostProcessor : IPostProcessor
    {
        object Config { get; }

        void SaveConfig();
    }
}
