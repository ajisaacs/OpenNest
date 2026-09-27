using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

try
{
    if (args.Length != 1)
        throw new ArgumentException("Usage: ReleaseSmoke <extracted-package-directory>");

    var package = Path.GetFullPath(args[0]);
    // No project references: resolve against the extracted release, never the build tree.
    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        var path = Path.Combine(package, name.Name + ".dll");
        return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
    };

    var engineAssembly = Assembly.LoadFrom(Path.Combine(package, "OpenNest.Engine.dll"));
    var registry = engineAssembly.GetType("OpenNest.Engine.Jobs.NestingEngineRegistry", true)!;
    var engineDirectory = Path.Combine(package, "Engines");
    registry.GetMethod("LoadPlugins")!.Invoke(null, [engineDirectory]);
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(engineDirectory, "manifest.json")));
    foreach (var entry in manifest.RootElement.GetProperty("engines").EnumerateArray())
    {
        var name = entry.GetProperty("registryName").GetString()!;
        var expected = Path.Combine(engineDirectory, entry.GetProperty("project").GetString() + ".dll");
        if (!File.Exists(expected))
            throw new FileNotFoundException("Required engine missing", expected);
        var engine = registry.GetMethod("Create")!.Invoke(null, [name])!;
        if (!string.Equals(engine.GetType().Assembly.Location, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Engine {name} was not loaded from the release package.");
        Console.WriteLine($"PASS: {name} loaded and instantiated from {expected}");
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
