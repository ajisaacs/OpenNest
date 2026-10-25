using System.Windows.Forms;
using OpenNest.Data.MaterialLedger;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class MaterialLedgerStoreTests
{
    [Fact]
    public void ApiKeyRoundTripsThroughDpapiWithoutPlainText()
    {
        var protectedKey = MaterialLedgerStore.Protect("secret-key");
        Assert.DoesNotContain("secret-key", protectedKey);
        Assert.Equal("secret-key", MaterialLedgerStore.Unprotect(protectedKey));
    }

    [Fact]
    public void TestButtonTreatsMappedFalseAsConnected()
    {
        Assert.Equal(MessageBoxIcon.Information,
            OptionsForm.DescribeLedgerTest(new LedgerStockResult.Unmapped()).Icon);
        Assert.Contains("rejected the API key",
            OptionsForm.DescribeLedgerTest(new LedgerStockResult.Unauthorized()).Text);
        Assert.Contains("timed out",
            OptionsForm.DescribeLedgerTest(new LedgerStockResult.Unreachable("timed out")).Text);
    }
}
