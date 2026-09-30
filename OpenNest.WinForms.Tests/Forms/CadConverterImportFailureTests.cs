using System.Reflection;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using OpenNest.Controls;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class CadConverterImportFailureTests
{
    [Fact]
    public void MTextRegexHasExplicitTimeout()
    {
        var regex = Assert.IsType<Regex>(typeof(CadConverterForm)
            .GetField("MTextFormattingPattern", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        Assert.Equal(TimeSpan.FromSeconds(1), regex.MatchTimeout);
        Assert.Equal(RegexOptions.None, regex.Options);
    }

    [Theory]
    [InlineData(@"{\H0.2;Label}", "Label")]
    [InlineData(@"\unterminated", @"\unterminated")]
    [InlineData("  {Plain}  ", "Plain")]
    [InlineData("", "")]
    public void MTextStrippingKeepsItsExistingRules(string input, string expected) => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ImportTestForm();
        Assert.Equal(expected, form.StripMTextFormatting(input));
    });

    [Fact]
    public void SuccessfulExtractionPreservesTextAndControlCodeSemantics() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ImportTestForm();
        var doc = Document(@"{\H0.2;Hole %%c}", "Angle %%d");
        doc.Entities.Add(new TextEntity { Value = "%%p %%P %%d %%D %%c %%C %%%" });
        var texts = form.ExtractTexts(doc);
        Assert.Equal(new[] { "Hole ⌀", "Angle °", "± ± ° ° ⌀ ⌀ %" }, texts.Select(t => t.Value));
        Assert.Equal(2, form.SuccessfulMTexts);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExtractionTimeoutPublishesNeitherRawTextNorPartialFile(int successesBeforeFailure) => ArchUnitsTests.RunSta(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenNest-CadText-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var priorPath = Path.Combine(directory, "previous.dxf");
            var failedPath = Path.Combine(directory, "failed.dxf");
            DxfWriter.Write(priorPath, Document("Previous text"), false);
            DxfWriter.Write(failedPath, Document(@"{\H0.2;First text}", "RAW PRIVATE TEXT"), false);
            var originalBytes = File.ReadAllBytes(failedPath);
            using var form = new ImportTestForm();
            form.AddFile(priorPath);
            var fileList = Assert.IsType<FileListControl>(form.Controls.Find("fileList", true).Single());
            var prior = Assert.Single(fileList.Items);
            var selection = fileList.SelectedIndex;
            Assert.Same(prior, fileList.SelectedItem);
            Assert.Empty(form.Notices);

            form.SuccessfulMTexts = 0;
            form.FailAfter = successesBeforeFailure;
            form.AddFile(failedPath);

            Assert.Same(prior, Assert.Single(fileList.Items));
            Assert.Same(prior, fileList.SelectedItem);
            Assert.Equal(selection, fileList.SelectedIndex);
            Assert.Equal(new[] { "Previous text" }, prior.Texts.Select(t => t.Value));
            Assert.Equal(successesBeforeFailure, form.SuccessfulMTexts);
            var notice = Assert.Single(form.Notices);
            Assert.Equal(failedPath, notice.File);
            Assert.Contains("text extraction timed out", notice.Error.Message);
            Assert.IsType<RegexMatchTimeoutException>(notice.Error.InnerException);
            Assert.DoesNotContain("RAW PRIVATE TEXT", notice.Error.Message);
            Assert.Equal(originalBytes, File.ReadAllBytes(failedPath));

            form.FailAfter = null;
            form.AddFile(failedPath);
            Assert.Equal(2, fileList.Items.Count);
            Assert.Same(prior, fileList.SelectedItem);
            Assert.Equal(new[] { "First text", "RAW PRIVATE TEXT" },
                fileList.Items.Single(item => item.Path == failedPath).Texts.Select(t => t.Value));
            Assert.Single(form.Notices);
        }
        finally { Directory.Delete(directory, true); }
    });

    private static CadDocument Document(params string[] values)
    {
        var doc = new CadDocument();
        doc.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0)));
        foreach (var value in values)
            doc.Entities.Add(new MText { Value = value, Height = 0.2, InsertPoint = new XYZ(0, 1, 0) });
        return doc;
    }

    private sealed class ImportTestForm : CadConverterForm
    {
        public int? FailAfter { get; set; }
        public int SuccessfulMTexts { get; set; }
        public List<(string File, Exception Error)> Notices { get; } = new();

        internal override string StripMTextFormatting(string text)
        {
            if (SuccessfulMTexts == FailAfter)
                throw new RegexMatchTimeoutException("RAW PRIVATE TEXT", "MText", TimeSpan.FromSeconds(1));
            var result = base.StripMTextFormatting(text);
            SuccessfulMTexts++;
            return result;
        }

        internal override void ReportImportFailure(string file, Exception error) => Notices.Add((file, error));
    }
}
