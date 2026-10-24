using System.IO;
using System.Linq;
using OpenNest.IO;
using Xunit;

namespace OpenNest.Tests.IO
{
    public class CadImporterTests
    {
        private static string TestDxf => Path.Combine("Bending", "TestData", "4526 A14 PT11.dxf");

        [Fact]
        public void Import_LoadsEntitiesAndDetectsBends()
        {
            var result = CadImporter.Import(TestDxf);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Entities);
            Assert.NotNull(result.Bends);
            Assert.NotNull(result.Bounds);
            Assert.Equal(TestDxf, result.SourcePath);
            Assert.Equal("4526 A14 PT11", result.Name);
        }

        [Fact]
        public void Import_WhenDetectBendsFalse_ReturnsEmptyBends()
        {
            var result = CadImporter.Import(TestDxf, new CadImportOptions { DetectBends = false });

            Assert.Empty(result.Bends);
        }

        [Fact]
        public void Import_WhenNameOverrideProvided_UsesOverride()
        {
            var result = CadImporter.Import(TestDxf, new CadImportOptions { Name = "custom" });

            Assert.Equal("custom", result.Name);
        }

        [Fact]
        public void Import_WhenNamedDetectorDoesNotExist_ReturnsEmptyBends()
        {
            // Exercises the named-detector branch: when BendDetectorName doesn't
            // match any registered detector, bends should be an empty list
            // (not a crash, and no fall-through to auto-detect).
            var result = CadImporter.Import(
                TestDxf,
                new CadImportOptions { BendDetectorName = "__nonexistent__" }
            );

            Assert.Empty(result.Bends);
        }

        [Fact]
        public void BuildDrawing_ProducesDrawingWithProgramAndMetadata()
        {
            var result = CadImporter.Import(TestDxf);

            var drawing = CadImporter.BuildDrawing(
                result,
                result.Entities,
                result.Bends,
                quantity: 5,
                customer: "ACME",
                editedProgram: null
            );

            Assert.NotNull(drawing);
            Assert.Equal("4526 A14 PT11", drawing.Name);
            Assert.Equal("ACME", drawing.Customer);
            Assert.Equal(5, drawing.Quantity.Required);
            Assert.Equal(TestDxf, drawing.Source.Path);
            Assert.NotNull(drawing.Program);
            Assert.NotEmpty(drawing.Program.Codes);
            Assert.NotNull(drawing.SourceEntities);
            Assert.NotEmpty(drawing.SourceEntities);
        }

        [Fact]
        public void BuildDrawing_ExtractsFirstRapidAsSourceOffset()
        {
            var result = CadImporter.Import(TestDxf);

            var drawing = CadImporter.BuildDrawing(
                result,
                result.Entities,
                result.Bends,
                quantity: 1,
                customer: null,
                editedProgram: null
            );

            Assert.NotNull(drawing.Source.Offset);
            // After offset extraction, the program's first rapid must start at origin.
            var firstRapid = (OpenNest.CNC.RapidMove)drawing.Program.Codes[0];
            Assert.Equal(0, firstRapid.EndPoint.X, 6);
            Assert.Equal(0, firstRapid.EndPoint.Y, 6);
        }

        [Fact]
        public void BuildDrawing_WhenEntityHidden_TracksSuppressedId()
        {
            var result = CadImporter.Import(TestDxf);
            // Suppress the first non-bend-source entity
            var bendSources = result
                .Bends.Where(b => b.SourceEntity != null)
                .Select(b => b.SourceEntity)
                .ToHashSet();
            var hidden = result.Entities.First(e => !bendSources.Contains(e));
            hidden.IsVisible = false;

            var drawing = CadImporter.BuildDrawing(
                result,
                result.Entities,
                result.Bends,
                quantity: 1,
                customer: null,
                editedProgram: null
            );

            Assert.Contains(hidden.Id, drawing.SuppressedEntityIds);
        }

        [Fact]
        public void BuildDrawing_WhenEditedProgramProvided_UsesEditedProgram()
        {
            var result = CadImporter.Import(TestDxf);
            var edited = new OpenNest.CNC.Program();
            edited.MoveTo(new OpenNest.Geometry.Vector(0, 0));

            var drawing = CadImporter.BuildDrawing(
                result,
                result.Entities,
                result.Bends,
                quantity: 1,
                customer: null,
                editedProgram: edited
            );

            Assert.Same(edited, drawing.Program);
        }

        [Fact]
        public void ImportDrawing_ComposesImportAndBuild()
        {
            var drawing = CadImporter.ImportDrawing(
                TestDxf,
                new CadImportOptions { Quantity = 3, Customer = "ACME" }
            );

            Assert.NotNull(drawing);
            Assert.Equal("4526 A14 PT11", drawing.Name);
            Assert.Equal(3, drawing.Quantity.Required);
            Assert.Equal("ACME", drawing.Customer);
            Assert.NotNull(drawing.Program);
            Assert.NotNull(drawing.SourceEntities);
        }

        [Fact]
        public void ImportDrawing_UnchangedFile_GivesTheSameLibraryPackage()
        {
            var first = CadImporter.ImportDrawing(TestDxf);
            var second = CadImporter.ImportDrawing(TestDxf);

            Assert.Equal(
                first.SourceEntities.Select(e => e.Id),
                second.SourceEntities.Select(e => e.Id)
            );
            Assert.Equal(
                DrawingPackage.Write(first, Units.Inches),
                DrawingPackage.Write(second, Units.Inches)
            );
        }

        [Fact]
        public void Import_EntityIdsAreUniqueAndNonempty()
        {
            var ids = CadImporter.Import(TestDxf).Entities.Select(e => e.Id).ToList();

            Assert.DoesNotContain(System.Guid.Empty, ids);
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void Import_ChangedFile_ReusesNoEntityIds()
        {
            // Ids key suppression, roles and edge selections, so a changed file must not
            // hand an old id to whatever entity now sits at that position.
            var changed = Path.Combine(Path.GetTempPath(), $"cadimporter-changed-{System.Guid.NewGuid():N}.dxf");
            try
            {
                File.WriteAllText(changed, "999\nchanged\n" + File.ReadAllText(TestDxf));
                var original = CadImporter.Import(TestDxf).Entities.Select(e => e.Id).ToHashSet();
                var edited = CadImporter.Import(changed).Entities.Select(e => e.Id).ToList();

                Assert.NotEmpty(edited);
                Assert.DoesNotContain(edited, original.Contains);
            }
            finally
            {
                File.Delete(changed);
            }
        }
    }
}
