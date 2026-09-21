using ClosedXML.Excel;
using OpenNest.IO.Bom;

namespace OpenNest.IO.Tests;

public class PartQuantityReaderTests
{
    [Fact]
    public void ReadsExactNamesAndQuantitiesIncludingZeroDemand()
    {
        WithWorkbook(
            sheet =>
            {
                sheet.Cell(2, 1).Value = "Part 01";
                sheet.Cell(2, 2).Value = 58;
                sheet.Cell(3, 1).Value = "Skeleton";
                sheet.Cell(3, 2).Value = 0;
            },
            path =>
            {
                var result = PartQuantityReader.Read(path);
                Assert.Equal(58, result["Part 01"]);
                Assert.Equal(0, result["Skeleton"]);
                Assert.Equal(2, result.Count);
            }
        );
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("")]
    [InlineData("2147483648")]
    public void RejectsInvalidQuantityInsteadOfDefaultingOrTruncating(string value)
    {
        WithWorkbook(
            sheet =>
            {
                sheet.Cell(2, 1).Value = "Part";
                sheet.Cell(2, 2).Value = value;
            },
            path => Assert.Throws<InvalidDataException>(() => PartQuantityReader.Read(path))
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsDuplicatePartsOrMissingQuantityHeader(bool duplicate)
    {
        WithWorkbook(
            sheet =>
            {
                sheet.Cell(2, 1).Value = "Part";
                sheet.Cell(2, 2).Value = 1;
                if (duplicate)
                {
                    sheet.Cell(3, 1).Value = "Part";
                    sheet.Cell(3, 2).Value = 2;
                }
                else
                    sheet.Cell(1, 2).Value = "Unrecognized";
            },
            path => Assert.Throws<InvalidDataException>(() => PartQuantityReader.Read(path))
        );
    }

    private static void WithWorkbook(Action<IXLWorksheet> prepare, Action<string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), $"opennest-quantities-{Guid.NewGuid()}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Parts");
                sheet.Cell(1, 1).Value = "Part Name";
                sheet.Cell(1, 2).Value = "Qty Required";
                prepare(sheet);
                workbook.SaveAs(path);
            }
            check(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
