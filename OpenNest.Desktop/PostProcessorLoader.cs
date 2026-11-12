using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenNest
{
    /// <summary>
    /// Discovers post processors in the desktop's <c>Posts/</c> directory and registers the
    /// materials they provide. Each DLL loads in isolation: one that fails is reported in
    /// <see cref="Result.Failures"/> instead of stopping the others or startup.
    /// </summary>
    public static class PostProcessorLoader
    {
        public sealed record Result(IReadOnlyList<IPostProcessor> PostProcessors, IReadOnlyList<string> Failures);

        public static string DefaultDirectory =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Posts");

        public static Result Load(string directory)
        {
            var postProcessors = new List<IPostProcessor>();
            var failures = new List<string>();
            if (!Directory.Exists(directory))
                return new Result(postProcessors, failures);

            foreach (var file in Directory.GetFiles(directory, "*.dll"))
            {
                try
                {
                    // Instantiate the whole DLL before registering any of it.
                    var loaded = Assembly.LoadFile(file).GetTypes()
                        .Where(type => type.GetInterfaces().Contains(typeof(IPostProcessor)))
                        .Select(type => (IPostProcessor)Activator.CreateInstance(type))
                        .ToList();
                    foreach (var postProcessor in loaded)
                    {
                        postProcessors.Add(postProcessor);
                        if (postProcessor is IMaterialProvidingPostProcessor materialProvider)
                            PostProcessorMaterials.AddFrom(materialProvider);
                    }
                }
                catch (Exception ex)
                {
                    var reason = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
                    failures.Add($"{Path.GetFileName(file)}: {reason.Message}");
                }
            }
            return new Result(postProcessors, failures);
        }
    }
}
