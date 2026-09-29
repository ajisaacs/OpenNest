using System.Reflection;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Diagnostics;

public class ConsolePostVerificationTests
{
    [Fact]
    public void RealConsoleBlocksOutputUntilExplicitRiskSwitchAndNeverRemembersConsent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennest-post-verification-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "job.nest");
            var output = Path.Combine(directory, "job.cnc");
            var nest = new Nest("VerificationFixture");
            var program = new OpenNest.CNC.Program();
            program.Codes.Add(new RapidMove(0, 0));
            program.Codes.Add(new LinearMove(2, 0));
            program.Codes.Add(new LinearMove(2, 2));
            program.Codes.Add(new LinearMove(0, 2));
            program.Codes.Add(new LinearMove(0, 0));
            var drawing = new Drawing("square", program);
            nest.Drawings.Add(drawing);
            var plate = nest.CreatePlate();
            plate.Size = new Size(10, 10);
            plate.Parts.Add(new Part(drawing));
            Assert.True(new NestWriter(nest).Write(input));
            var args = new[] { input, "--quantity", "1", "--no-save", "--post", "Cincinnati CL-707",
                "--posts-dir", AppContext.BaseDirectory, "--post-output", output };

            Assert.Equal(1, Run(args));
            Assert.False(File.Exists(output));

            File.WriteAllText(output, "existing CNC must survive refusal");
            Assert.Equal(1, Run(args));
            Assert.Equal("existing CNC must survive refusal", File.ReadAllText(output));

            var colliding = args.Where(arg => arg != "--no-save")
                .Concat(new[] { "--output", Path.Combine(directory, ".", "job.cnc") }).ToArray();
            Assert.Equal(1, Run(colliding));
            Assert.Equal("existing CNC must survive refusal", File.ReadAllText(output));
            Assert.Equal(1, Run(colliding.Append("--acknowledge-post-risks").ToArray()));
            Assert.Equal("existing CNC must survive refusal", File.ReadAllText(output));

            Assert.Equal(0, Run(args.Append("--acknowledge-post-risks").ToArray()));
            var posted = File.ReadAllText(output);
            Assert.Contains("M30", posted);
            Assert.Contains("VerificationFixture", posted);

            Assert.Equal(1, Run(args));
            Assert.Equal(posted, File.ReadAllText(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int Run(string[] args)
    {
        var entry = Assembly.Load("OpenNest.Console").GetType("NestConsole")!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        return (int)entry.Invoke(null, new object[] { args })!;
    }
}
