using System;
using System.IO;
using OpenNest;
using OpenNest.CNC;

namespace OpenNest.Posts.Cincinnati;

/// <summary>
/// Emits the main program header and variable declaration subprogram
/// for a Cincinnati laser post-processor output file.
/// </summary>
public sealed class CincinnatiPreambleWriter
{
    private readonly CincinnatiPostConfig _config;

    public CincinnatiPreambleWriter(CincinnatiPostConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Writes the main program header block.
    /// </summary>
    /// <param name="initialLibrary">Resolved G89 library file for the initial process setup.</param>
    public void WriteMainProgram(TextWriter w, string nestName, string materialDescription,
        int sheetCount, string initialLibrary)
    {
        w.WriteLine(CoordinateFormatter.Comment($"NEST {nestName}"));
        w.WriteLine(CoordinateFormatter.Comment($"CONFIGURATION - {_config.ConfigurationName}"));
        w.WriteLine(CoordinateFormatter.Comment(DateTime.Now.ToString("MM-dd-yyyy hh:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture)));

        if (!string.IsNullOrEmpty(materialDescription))
            w.WriteLine(CoordinateFormatter.Comment($"Material = {materialDescription}"));

        if (_config.UseExactStopMode)
            w.WriteLine("G61");

        w.WriteLine(CoordinateFormatter.Comment("MAIN PROGRAM"));

        w.WriteLine(_config.PostedUnits == Units.Millimeters ? "G21 G90" : "G20 G90");

        if (_config.UseSmartRapids)
            w.WriteLine("G121 (SMART RAPIDS)");

        w.WriteLine("M42");

        if (_config.ProcessParameterMode == G89Mode.LibraryFile && !string.IsNullOrEmpty(initialLibrary))
            w.WriteLine($"G89 P{initialLibrary}");

        w.WriteLine($"M98 P{_config.VariableDeclarationSubprogram} (Variable Declaration)");

        if (_config.PalletExchange == PalletMode.StartAndEnd)
            w.WriteLine("M50");

        w.WriteLine("GOTO1 (GOTO SHEET NUMBER)");

        for (var i = 1; i <= sheetCount; i++)
        {
            var subNum = _config.SheetSubprogramStart + (i - 1);
            w.WriteLine($"N{i} M98 P{subNum} (SHEET {i})");
        }

        w.WriteLine("M42");
        w.WriteLine("M30 (END OF MAIN)");
    }

    /// <summary>
    /// Writes the variable declaration subprogram block.
    /// </summary>
    public void WriteVariableDeclaration(TextWriter w, ProgramVariableManager vars)
    {
        w.WriteLine("(*****************************************************)");
        w.WriteLine($":{_config.VariableDeclarationSubprogram}");
        w.WriteLine("(Variable Declaration Start)");

        foreach (var line in vars.EmitDeclarations())
            w.WriteLine(line);

        w.WriteLine("M99 (Variable Declaration End)");
    }
}
