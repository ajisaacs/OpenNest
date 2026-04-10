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
    }
}
