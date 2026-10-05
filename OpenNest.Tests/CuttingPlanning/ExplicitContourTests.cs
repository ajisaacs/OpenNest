using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class ExplicitContourTests
{
    public static IEnumerable<object[]> RetainedStyles()
    {
        foreach (var style in new[] { "line", "arc", "linearc", "lineline", "clean" })
            foreach (var reversed in new[] { false, true })
                foreach (var internalContour in new[] { false, true })
                    yield return [style, reversed, internalContour];
    }

    [Theory]
    [MemberData(nameof(RetainedStyles))]
    public void LegacyEmission_RetainsStylesWindingAndCornerFallback(string style, bool reversed, bool internalContour)
    {
        var program = Square(reversed);
        var entities = program.ToGeometry().Where(e => e.Layer != SpecialLayers.Rapid).ToArray();
        var parameters = Parameters(style);
        if (internalContour)
        {
            program.MoveTo(-5, -5); program.LineTo(-5, 15); program.LineTo(15, 15);
            program.LineTo(15, -5); program.LineTo(-5, -5);
        }
        var result = new ContourCuttingStrategy { Parameters = parameters }.ApplySingle(program,
            new Vector(0, 0), entities[0], internalContour ? ContourType.Internal : ContourType.External);
        var expected = $"{style}:{reversed}:{internalContour}" switch
        {
            "arc:False:False" => "4F89EDF317A545A0F496A1C7F9C633755663D98AFCF71F783B4AABF700D1711C",
            "arc:False:True" => "478018E0A86BA1FF0F08204EF5449D4484ADD1B58A40EA0936069DE6B6002E3F",
            "arc:True:False" => "CDC68828AD39D4B88C205347FDC06227C03A947F6AB3A19376F4BD670987C1A6",
            "arc:True:True" => "83CAD7BDCF4A655730D779966355F535E36429B5AAFA4423CB58E3B3FAAD4376",
            "clean:False:False" => "E3A6BE74CC4B72EC5DEFD9E53743DD080F6AF6854A0E7143860E5246745D1C12",
            "clean:False:True" => "196CC9BE57E9740AB8E35A77A48473DCD8B62312DE2903B2E66F62BB39818DDA",
            "clean:True:False" => "6E51376D40D93BD0DAD2E51FFAC732A9BE1717C252964BA2285A8440A23C3480",
            "clean:True:True" => "80DB265B4E9846169421B3DDCB8DAE003D720EEE8EF9DAB64C40EE00CC322DE6",
            "line:False:False" => "A9DAB8F802CDE0A89A0D26B4C5AC5546E121E71AF9EBA2283F307ADF88866347",
            "line:False:True" => "61EE8CF9A30B18C72DDACD314886709692CA8E50E7DC61170A900429562A45B5",
            "line:True:False" => "AB6E00410C3AB8592B9DD7C5EC1E9E8ABD39A2DCA9486FCEAE28593D9BA7504C",
            "line:True:True" => "23E2404046438ECF67BAC46AC7692B6DF60EFA674C286D72B25F98FE77431588",
            "linearc:False:False" => "E3A6BE74CC4B72EC5DEFD9E53743DD080F6AF6854A0E7143860E5246745D1C12",
            "linearc:False:True" => "196CC9BE57E9740AB8E35A77A48473DCD8B62312DE2903B2E66F62BB39818DDA",
            "linearc:True:False" => "6E51376D40D93BD0DAD2E51FFAC732A9BE1717C252964BA2285A8440A23C3480",
            "linearc:True:True" => "80DB265B4E9846169421B3DDCB8DAE003D720EEE8EF9DAB64C40EE00CC322DE6",
            "lineline:False:False" => "F25E4FA39713A53D04FC5DA22BD3FD1EB15330462153356906AD4DAA1C1A23C2",
            "lineline:False:True" => "911C85F1C6A053F9C94F63A40B520C30B00AA43B804634E0203DD406BB98BF6E",
            "lineline:True:False" => "AE8E6CD1B641D33A39DAE07A6FAF539668BD794DC8105F4D41C1BE33927FFC65",
            "lineline:True:True" => "54E1F71B8420ACAF1C57E124A0F927BA00F04F52CDBAB48EDFC27BAB8C346EA5",
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(expected, Fingerprint(result.Program));
    }

    [Fact]
    public void LegacyCircle_RoundsActualEntryClampsAndSharesSubprograms()
    {
        var program = Square(false);
        foreach (var center in new[] { new Vector(3, 3), new Vector(7, 7) })
        {
            program.MoveTo(center.X + 0.5, center.Y);
            program.ArcTo(center.X + 0.5, center.Y, center.X, center.Y, RotationType.CCW);
        }
        var parameters = Parameters("line");
        parameters.RoundLeadInAngles = true;
        parameters.LeadInAngleIncrement = 90;
        parameters.ArcCircleLeadIn = new LineLeadIn { Length = 2 };
        var result = new ContourCuttingStrategy { Parameters = parameters }.Apply(program, new Vector(20, 20));
        var calls = result.Program.Codes.OfType<SubProgramCall>().ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Same(calls[0].Program, calls[1].Program);
        var moves = ExecutionMotionReader.Read(result.Program, Vector.Zero, null, default).Motions;
        Assert.All(moves.Where(m => m.Layer == LayerType.Leadin).Take(2), m => Assert.True(m.Length < 1));
        Assert.Equal("9BB777CDD1F7407EB0D4B0FE1D1A760370A094A606E61B7E3875DCB5EF051F20", Fingerprint(result.Program));
    }

    [Fact]
    public void LegacyTab_LeavesGenuineGap()
    {
        var parameters = Parameters("line");
        parameters.TabsEnabled = true;
        parameters.TabConfig = new NormalTab { Size = 0.2 };
        var result = new ContourCuttingStrategy { Parameters = parameters }.Apply(Square(false), new Vector(-1, 5));
        var moves = ExecutionMotionReader.Read(result.Program, Vector.Zero, null, default).Motions;
        var cuts = moves.Where(m => !m.Rapid && m.Layer == LayerType.Display).ToArray();
        Assert.True(cuts[0].Start!.Value.DistanceTo(cuts[^1].End) > 0.1);
        Assert.Equal("C5CDFACDE4019AEBFCD4AA304D149DD603313D341311F27C9ECBB4ED940D2204", Fingerprint(result.Program));
    }

    internal static Program Square(bool reversed)
    {
        var p = new Program();
        p.MoveTo(0, 0);
        if (reversed)
        {
            p.LineTo(10, 0); p.LineTo(10, 10); p.LineTo(0, 10);
        }
        else
        {
            p.LineTo(0, 10); p.LineTo(10, 10); p.LineTo(10, 0);
        }
        p.LineTo(0, 0);
        return p;
    }

    internal static CuttingParameters Parameters(string style = "line")
    {
        LeadIn lead = style switch
        {
            "arc" => new ArcLeadIn { Radius = 0.2 },
            "linearc" => new LineArcLeadIn { LineLength = 0.1, ArcRadius = 0.2 },
            "lineline" => new LineLineLeadIn { Length1 = 0.1, Length2 = 0.2 },
            "clean" => new CleanHoleLeadIn { LineLength = 0.1, ArcRadius = 0.2, Kerf = 0.01 },
            _ => new LineLeadIn { Length = 0.3, ApproachAngle = 45 }
        };
        return new CuttingParameters
        {
            ExternalLeadIn = lead,
            InternalLeadIn = lead,
            ArcCircleLeadIn = lead,
            ExternalLeadOut = new NoLeadOut(),
            InternalLeadOut = new NoLeadOut(),
            PierceClearance = 0.05
        };
    }

    internal static string Fingerprint(Program program)
    {
        var execution = ExecutionMotionReader.Read(program, Vector.Zero, null, default);
        var text = string.Join("\n", execution.Motions.Select(m => string.Join("|", m.Rapid, m.Layer,
            m.Start?.X.ToString("R", CultureInfo.InvariantCulture), m.Start?.Y.ToString("R", CultureInfo.InvariantCulture),
            m.End.X.ToString("R", CultureInfo.InvariantCulture), m.End.Y.ToString("R", CultureInfo.InvariantCulture),
            m.Length.ToString("R", CultureInfo.InvariantCulture))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
