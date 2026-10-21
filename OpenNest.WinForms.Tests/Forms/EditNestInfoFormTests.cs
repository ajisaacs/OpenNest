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
            // An unchanged name never checks the server, so this completes without awaiting.
            editor.ShowNestInfoEditorAsync().GetAwaiter().GetResult();
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
    public void Name_IsReadOnlyUnlessRenameIsAllowed()
    {
        RunSta(() =>
        {
            var nest = AcceptanceNest();
            using var form = new EditNestInfoForm();
            form.LoadNestInfo(nest);
            var nameBox = Assert.IsType<TextBox>(form.Controls.Find("nameBox", true).Single());
            Assert.True(nameBox.ReadOnly);

            form.AllowRename = true;
            Assert.False(nameBox.ReadOnly);
            nameBox.Text = "  Renamed job  ";
            form.SaveNestInfo(nest);

            Assert.Equal("Renamed job", nest.Name);
        });
    }

    [Fact]
    public void StatusList_FollowsWorkflowOrder_AndSavesDraft()
    {
        RunSta(() =>
        {
            var nest = AcceptanceNest();
            using var form = new EditNestInfoForm();
            form.LoadNestInfo(nest);
            var statusBox = Assert.IsType<ComboBox>(form.Controls.Find("statusBox", true).Single());

            Assert.Equal(NestStatuses.WorkflowOrder, statusBox.Items.Cast<NestStatus>());

            form.Status = NestStatus.Draft;
            form.SaveNestInfo(nest);

            Assert.Equal(NestStatus.Draft, nest.Status);
        });
    }

    [Fact]
    public void BlankName_DisablesOk()
    {
        RunSta(() =>
        {
            using var form = new EditNestInfoForm { AllowRename = true };
            form.LoadNestInfo(AcceptanceNest());
            form.EnableCheck(); // The size box re-checks on a debounce timer.
            var nameBox = Assert.IsType<TextBox>(form.Controls.Find("nameBox", true).Single());
            var ok = Assert.IsType<Button>(form.Controls.Find("applyButton", true).Single());
            Assert.True(ok.Enabled);

            nameBox.Text = "   ";
            Assert.False(ok.Enabled);
            nameBox.Text = "Job";
            Assert.True(ok.Enabled);
        });
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

    [Fact]
    public void GradeList_FollowsMaterial_AndSavesChosenGrade()
    {
        RunSta(() =>
        {
            var nest = new Nest { Material = new Material("Stainless", "304", 0.289) };
            using var form = new EditNestInfoForm();
            form.SetMaterialCatalog(new[]
            {
                new Data.MaterialConfig { Name = "Stainless", Grade = "316" },
                new Data.MaterialConfig { Name = "Stainless", Grade = "304" },
                new Data.MaterialConfig { Name = "Aluminum", Grade = "5052" },
            });
            form.LoadNestInfo(nest);
            var gradeBox = Assert.IsType<ComboBox>(form.Controls.Find("gradeBox", true).Single());

            Assert.Equal(new[] { "304", "316" }, gradeBox.Items.Cast<string>());
            Assert.Equal("304", form.MaterialGrade);

            form.MaterialName = "aluminum";
            Assert.Equal(new[] { "5052" }, gradeBox.Items.Cast<string>());
            Assert.Equal("", form.MaterialGrade);

            form.MaterialGrade = "5052";
            form.SaveNestInfo(nest);

            Assert.Equal("Aluminum", nest.Material.Name);
            Assert.Equal("5052", nest.Material.Grade);
            Assert.Equal(0, nest.Material.Density);
        });
    }

    [Fact]
    public void SaveWithChangedGrade_KeepsNameAndDropsDensity()
    {
        RunSta(() =>
        {
            var nest = new Nest { Material = new Material("Stainless", "304", 0.289) };
            using var form = new EditNestInfoForm();
            form.LoadNestInfo(nest);
            form.MaterialGrade = "316";

            form.SaveNestInfo(nest);

            Assert.Equal("Stainless", nest.Material.Name);
            Assert.Equal("316", nest.Material.Grade);
            Assert.Equal(0, nest.Material.Density);
        });
    }

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(15), "The STA test did not complete.");
}
