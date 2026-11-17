namespace OpenNest.Desktop.Interaction;

/// <summary>
/// The drawing list's pick-up, as WinForms arms it: a single left press on a drawing picks it
/// up, and the next time the plate view takes the keyboard (the pointer entering it focuses
/// it) the drawing then selected in the list starts the place tool. A drop of the dragged
/// drawing on the plate, a double-click and an Undo or Redo put it down.
/// </summary>
public sealed class DrawingPickup
{
    /// <summary>Whether a drawing is picked up.</summary>
    public bool IsArmed { get; private set; }

    /// <summary>A single left press on a drawing in the list.</summary>
    public void PickUp() => IsArmed = true;

    /// <summary>A drop on the plate, a double-click or an Undo: nothing left to place.</summary>
    public void PutDown() => IsArmed = false;

    /// <summary>
    /// The plate view took the keyboard: the drawing to place, the one selected in the list,
    /// or null. As in WinForms, a pick-up with nothing selected stays armed.
    /// </summary>
    public Drawing TakeOnPlateEnter(Drawing selected)
    {
        if (!IsArmed || selected == null)
            return null;
        IsArmed = false;
        return selected;
    }
}
