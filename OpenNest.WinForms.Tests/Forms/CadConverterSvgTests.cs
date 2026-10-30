using System;
using System.IO;
using OpenNest.Forms;
using OpenNest.IO;
using Xunit;

namespace OpenNest.WinForms.Tests.Forms;

public class CadConverterSvgTests
{
    [Fact]
    public void ScaleFormDefaultsToCssPixels()
    {
        using var form = new SvgScaleForm("part.svg", 96, 48, Units.Inches);

        Assert.Equal(96, form.UserUnitsPerInch);
    }

    [Fact]
    public void ResolveLeavesOtherFilesAlone()
    {
        var options = new CadImportOptions();

        Assert.True(SvgScaleForm.TryResolve(null, "part.dxf", options));
        Assert.Null(options.SvgUserUnitsPerInch);
    }

    [Fact]
    public void ResolveDoesNotAskWhenTheFileStatesItsSize()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".svg");
        File.WriteAllText(
            path,
            "<svg xmlns='http://www.w3.org/2000/svg' width='1in' height='1in' viewBox='0 0 1 1'/>"
        );
        try
        {
            var options = new CadImportOptions();

            Assert.True(SvgScaleForm.TryResolve(null, path, options));
            Assert.Null(options.SvgUserUnitsPerInch);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
