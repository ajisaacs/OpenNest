using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

[Collection("Fill operation lifetime")]
public class EditNestInfoFormTests
{
    [Fact]
    public void ShownEditorOk_FileRoundTripPreservesUnchangedMaterialGradeAndDensity()
    {
        RunSta(() =>
        {
            using var files = new WindowsAcceptanceFiles();
            var nest = AcceptanceNest();

            AcceptShownEditor(nest, materialName: null);

            Assert.Equal("Stainless", nest.Material.Name);
            Assert.Equal("304", nest.Material.Grade);
            Assert.Equal(0.289, nest.Material.Density);
            var restored = files.RoundTrip(nest);
            Assert.Equal("Stainless", restored.Material.Name);
            Assert.Equal("304", restored.Material.Grade);
            Assert.Equal(0.289, restored.Material.Density);
            Assert.Equal("Acceptance nest", restored.Name);
        });
    }

    [Fact]
    public void ShownEditorOk_FileRoundTripClearsOldMetadataWhenMaterialNameChanges()
    {
        RunSta(() =>
        {
            using var files = new WindowsAcceptanceFiles();
            var nest = AcceptanceNest();

            AcceptShownEditor(nest, materialName: "Aluminum");

            Assert.Equal("Aluminum", nest.Material.Name);
            Assert.True(string.IsNullOrEmpty(nest.Material.Grade));
            Assert.Equal(0, nest.Material.Density);
            var restored = files.RoundTrip(nest);
            Assert.Equal("Aluminum", restored.Material.Name);
            Assert.True(string.IsNullOrEmpty(restored.Material.Grade));
            Assert.Equal(0, restored.Material.Density);
            Assert.Equal("Acceptance nest", restored.Name);
        });
    }

    private static Nest AcceptanceNest()
    {
        var nest = new Nest("Acceptance nest")
        {
            Material = new Material("Stainless", "304", 0.289),
            Thickness = 0.25,
            Units = Units.Inches,
        };
        nest.PlateDefaults.Size = new Geometry.Size(10, 20);
        nest.CreatePlate();
        return nest;
    }

    private static void AcceptShownEditor(Nest nest, string? materialName)
    {
        using var editor = new EditNestForm(nest);
        editor.Show();
        Assert.True(editor.Visible);
        EditNestInfoForm? info = null;
        Exception? failure = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        using var accept = new System.Windows.Forms.Timer { Interval = 20 };
        accept.Tick += (_, _) =>
        {
            info = Application.OpenForms.OfType<EditNestInfoForm>().SingleOrDefault();
            if (info == null)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("The Nest Info editor was not shown.");
                return;
            }

            accept.Stop();
            try
            {
                Assert.True(info.Visible);
                Assert.True(info.Modal);
                if (materialName != null)
                    Assert.IsType<ComboBox>(info.Controls.Find("materialBox", true).Single()).Text = materialName;
                info.EnableCheck();
                var ok = Assert.IsType<Button>(info.Controls.Find("applyButton", true).Single());
                Assert.True(ok.Enabled);
                Assert.Equal(DialogResult.OK, ok.DialogResult);
                ok.PerformClick();
            }
            catch (Exception ex)
            {
                failure = ex;
                info.DialogResult = DialogResult.Cancel;
                info.Close();
            }
        };

        try
        {
            accept.Start();
            // This is the live Nest Info entry point; it owns LoadNestInfo,
            // ShowDialog and SaveNestInfo after the actual OK button accepts.
            editor.ShowNestInfoEditor();
            accept.Stop();
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.NotNull(info);
            Assert.Equal(DialogResult.OK, info.DialogResult);
            Assert.False(info.Visible);
        }
        finally
        {
            accept.Stop();
            info?.Dispose();
        }
    }

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
