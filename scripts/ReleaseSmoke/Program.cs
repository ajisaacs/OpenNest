using System.Reflection;
using System.Runtime.Loader;

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

    var engineAssemblyPath = Path.Combine(package, "OpenNest.Engine.dll");
    var engineAssembly = Assembly.LoadFrom(engineAssemblyPath);
    var registry = engineAssembly.GetType("OpenNest.Engine.Jobs.NestingEngineRegistry", true)!;
    var create = registry.GetMethod("Create")!;

    // Every built-in engine the desktop offers must instantiate from the packaged assembly.
    string[] expected = ["Rectangles", "Irregular", "StockLadder", "Fill", "Strip", "Vertical Remnant", "Horizontal Remnant"];
    foreach (var name in expected)
    {
        var engine = create.Invoke(null, [name])!;
        if (!string.Equals(engine.GetType().Assembly.Location, engineAssemblyPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Engine {name} was not loaded from the release package.");
        Console.WriteLine($"PASS: {name} instantiated from {engineAssemblyPath}");
    }

    // A selection saved before the engines were renamed must still resolve.
    var resolved = registry.GetMethod("ResolveName")!.Invoke(null, ["Opus55NestingEngine"]);
    if (!Equals(resolved, "Irregular"))
        throw new InvalidOperationException($"Legacy selection Opus55NestingEngine resolved to '{resolved}', expected Irregular.");
    Console.WriteLine("PASS: legacy selection Opus55NestingEngine resolves to Irregular");

    // Failure case: an unknown engine must be rejected, so a broken registry cannot pass silently.
    try
    {
        create.Invoke(null, ["ReleaseSmokeMissingEngine"]);
        throw new InvalidOperationException("Unknown engine name was accepted.");
    }
    catch (TargetInvocationException exception) when (exception.InnerException is NotSupportedException)
    {
        Console.WriteLine("PASS: unknown engine name rejected");
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
