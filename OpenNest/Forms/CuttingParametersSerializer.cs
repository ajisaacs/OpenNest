using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Forms;

// Keep the desktop settings API stable while sharing the mapping with nest persistence.
public static class CuttingParametersSerializer
{
    public static string Serialize(CuttingParameters parameters) =>
        IO.CuttingParametersSerializer.Serialize(parameters);

    public static CuttingParameters Deserialize(string json) =>
        IO.CuttingParametersSerializer.Deserialize(json);
}
