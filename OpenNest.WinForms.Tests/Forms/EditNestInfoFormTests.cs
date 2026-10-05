using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class EditNestInfoFormTests
{
    [Fact]
    public void SaveWithUnchangedMaterial_KeepsGradeAndDensity()
    {
        RunSta(() =>
        {
            var nest = new Nest { Material = new Material("Stainless", "304", 0.289) };
            using var form = new EditNestInfoForm();
            form.LoadNestInfo(nest);

            form.SaveNestInfo(nest);

            Assert.Equal("Stainless", nest.Material.Name);
            Assert.Equal("304", nest.Material.Grade);
            Assert.Equal(0.289, nest.Material.Density);
        });
    }

    [Fact]
    public void SaveWithChangedMaterial_GetsNameOnly()
    {
        RunSta(() =>
        {
            var nest = new Nest { Material = new Material("Stainless", "304", 0.289) };
            using var form = new EditNestInfoForm();
            form.LoadNestInfo(nest);
            form.MaterialName = "Aluminum";

            form.SaveNestInfo(nest);

            Assert.Equal("Aluminum", nest.Material.Name);
            Assert.True(string.IsNullOrEmpty(nest.Material.Grade));
            Assert.Equal(0, nest.Material.Density);
        });
    }

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(15), "The STA test did not complete.");
}
