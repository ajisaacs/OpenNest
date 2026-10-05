namespace OpenNest.IO.Bom;

/// <summary>
/// One BOM line as the import dialog shows and edits it: the values read
/// from the BOM, the drawing file it resolved to and whether it can be
/// imported.
/// </summary>
public class BomPartRow
{
    public int? ItemNum { get; set; }

    public string FileName { get; set; }

    public int? Qty { get; set; }

    public string Description { get; set; }

    public string Material { get; set; }

    public double? Thickness { get; set; }

    public string DxfPath { get; set; }

    public string Status { get; set; }

    public bool IsEditable { get; set; }
}
