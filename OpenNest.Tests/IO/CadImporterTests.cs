using System.IO;
using System.Linq;
using OpenNest.IO;
using Xunit;

namespace OpenNest.Tests.IO
{
    public class CadImporterTests
    {
        private static string TestDxf =>
            Path.Combine("Bending", "TestData", "4526 A14 PT11.dxf");

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
        public void BuildDrawing_ProducesDrawingWithProgramAndMetadata()
        {
            var result = CadImporter.Import(TestDxf);

            var drawing = CadImporter.BuildDrawing(
                result,
                result.Entities,
                result.Bends,
                quantity: 5,
                customer: "ACME",
                editedProgram: null);

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

            var drawing = CadImporter.BuildDrawing(result, result.Entities, result.Bends,
                quantity: 1, customer: null, editedProgram: null);

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
            var bendSources = result.Bends
                .Where(b => b.SourceEntity != null)
                .Select(b => b.SourceEntity)
                .ToHashSet();
            var hidden = result.Entities.First(e => !bendSources.Contains(e));
            hidden.IsVisible = false;

            var drawing = CadImporter.BuildDrawing(result, result.Entities, result.Bends,
                quantity: 1, customer: null, editedProgram: null);

            Assert.Contains(hidden.Id, drawing.SuppressedEntityIds);
        }

        [Fact]
        public void BuildDrawing_WhenEditedProgramProvided_UsesEditedProgram()
        {
            var result = CadImporter.Import(TestDxf);
            var edited = new OpenNest.CNC.Program();
            edited.MoveTo(new OpenNest.Geometry.Vector(0, 0));

            var drawing = CadImporter.BuildDrawing(result, result.Entities, result.Bends,
                quantity: 1, customer: null, editedProgram: edited);

            Assert.Same(edited, drawing.Program);
        }
    }
}
