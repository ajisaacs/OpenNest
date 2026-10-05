using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenNest.IO.Bom;

/// <summary>Whether a BOM row can be imported, and if not, why.</summary>
public enum BomRowStatus
{
    /// <summary>Drawing found and every value set: the row is imported.</summary>
    Ready,

    /// <summary>Drawing found but the material is blank.</summary>
    NeedsMaterial,

    /// <summary>Drawing found but the thickness is blank, zero or negative.</summary>
    NeedsThickness,

    /// <summary>Drawing found but the quantity is below 1.</summary>
    NeedsQuantity,

    /// <summary>No drawing file matches the row's file name.</summary>
    NoDrawing,

    /// <summary>The BOM row has no file name.</summary>
    NoFileName,
}

/// <summary>
/// One BOM line as the import dialog shows and edits it: the values read
/// from the BOM, the drawing file it resolved to and whether it can be
/// imported. Changing Material, Thickness or Qty raises
/// <see cref="PropertyChanged"/> for that value and for the status.
/// </summary>
public class BomPartRow : INotifyPropertyChanged
{
    private string material;
    private double? thickness;
    private int? qty;

    public event PropertyChangedEventHandler PropertyChanged;

    public int? ItemNum { get; set; }

    public string FileName { get; set; }

    /// <summary>The quantity the BOM gave, unchanged by edits; null when blank.</summary>
    public int? BomQty { get; set; }

    /// <summary>
    /// The quantity to import. Set it only to a value accepted by
    /// <see cref="BomQuantity.TryParse"/>, or use <see cref="TrySetQuantity"/>.
    /// Setting it clears <see cref="QtyAssumed"/>.
    /// </summary>
    public int? Qty
    {
        get => qty;
        set
        {
            QtyAssumed = false;
            Set(ref qty, value);
        }
    }

    /// <summary>True while Qty is the 1 used for a blank BOM quantity.</summary>
    public bool QtyAssumed { get; set; }

    public string Description { get; set; }

    public string Material
    {
        get => material;
        set => Set(ref material, value);
    }

    public double? Thickness
    {
        get => thickness;
        set => Set(ref thickness, value);
    }

    /// <summary>The matched drawing file, or null when none was found.</summary>
    public string DxfPath { get; set; }

    public BomRowStatus Status
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FileName))
                return BomRowStatus.NoFileName;
            if (string.IsNullOrWhiteSpace(DxfPath))
                return BomRowStatus.NoDrawing;
            if (string.IsNullOrWhiteSpace(Material))
                return BomRowStatus.NeedsMaterial;
            if (Thickness is not double t || !double.IsFinite(t) || t <= 0)
                return BomRowStatus.NeedsThickness;
            if (Qty is not int q || q < 1)
                return BomRowStatus.NeedsQuantity;
            return BomRowStatus.Ready;
        }
    }

    public string StatusText => Describe(Status);

    /// <summary>
    /// True when the row has a drawing, so the operator can complete its
    /// values; rows without one can never be imported.
    /// </summary>
    public bool IsEditable => Status is not (BomRowStatus.NoFileName or BomRowStatus.NoDrawing);

    /// <summary>
    /// Sets Qty from typed text when <see cref="BomQuantity.TryParse"/>
    /// accepts it; otherwise leaves the row unchanged and returns false.
    /// </summary>
    public bool TrySetQuantity(string text)
    {
        if (!BomQuantity.TryParse(text, out var value))
            return false;

        Qty = value;
        return true;
    }

    public static string Describe(BomRowStatus status) =>
        status switch
        {
            BomRowStatus.Ready => "Ready",
            BomRowStatus.NeedsMaterial => "Needs material",
            BomRowStatus.NeedsThickness => "Needs thickness",
            BomRowStatus.NeedsQuantity => "Needs quantity",
            BomRowStatus.NoDrawing => "No drawing found",
            BomRowStatus.NoFileName => "No file name",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
