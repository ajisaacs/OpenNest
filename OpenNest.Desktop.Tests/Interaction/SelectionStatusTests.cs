using System.Globalization;
using OpenNest.Desktop.Interaction;

namespace OpenNest.Desktop.Tests.Interaction;

public class SelectionStatusTests
{
    [Fact]
    public void One_part_shows_its_box_and_several_show_their_count_and_combined_box()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var plate = InteractionFixtures.Plate();

            Assert.Equal(string.Empty, SelectionStatus.Text(Array.Empty<Part>()));
            Assert.Equal("Selected: [20.0000, 0.0000] 10.0000 x 10.0000",
                SelectionStatus.Text(new[] { plate.Parts[1] }));
            Assert.Equal("Selected (2): [20.0000, 0.0000] 30.0000 x 10.0000",
                SelectionStatus.Text(new[] { plate.Parts[1], plate.Parts[4] }));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
