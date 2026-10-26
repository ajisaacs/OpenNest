namespace OpenNest
{
    /// <summary>
    /// A configurable post whose settings travel as JSON, so a host can keep them somewhere other
    /// than the post's own file (the nest server's shared copy, for one).
    /// </summary>
    public interface IJsonConfigPostProcessor : IConfigurablePostProcessor
    {
        /// <summary>The settings as the post's own file holds them.</summary>
        string SerializeConfig();

        /// <summary>
        /// Replaces the settings with <paramref name="json"/>, in the post's file format; the
        /// file itself is not written. Throws <see cref="System.Text.Json.JsonException"/> and
        /// keeps the current settings when the JSON is not usable.
        /// </summary>
        void LoadConfig(string json);
    }
}
