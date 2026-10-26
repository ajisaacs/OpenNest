using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;
using Size = OpenNest.Geometry.Size;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>A main window with one nest window open, for undo tests.</summary>
internal sealed record UndoTestEnv(MainForm Host, EditNestForm Editor)
{
    public PlateView View => Editor.PlateView;

    public Nest Nest => Editor.Nest;

    /// <summary>
    /// Runs <paramref name="body"/> on an STA thread with a nest of four plates holding 2, 2, 0
    /// and 1 squares of drawing "square", plus the empty new-plate sentinel.
    /// </summary>
    public static void Run(Action<UndoTestEnv> body, Func<Nest>? nest = null) => StaTestThread.Run(() =>
    {
        using var host = new TestHost();
        host.Show();
        using var editor = new EditNestForm((nest ?? BuildNest)()) { MdiParent = host };
        editor.PlateView.SetOverlapAutoCheck(null);
        editor.Show();
        editor.Activate();
        body(new UndoTestEnv(host, editor));
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    /// <summary>
    /// Runs <paramref name="edit"/> and checks it recorded exactly one entry named
    /// <paramref name="label"/> that changed the nest, and that Undo puts the nest back.
    /// </summary>
    public void AssertOneUndoableEdit(string label, Action edit)
    {
        var before = Describe(Nest);
        var count = Editor.History.UndoCount;

        edit();

        Assert.Equal(count + 1, Editor.History.UndoCount);
        Assert.Equal(label, Editor.History.UndoLabel);
        var after = Describe(Nest);
        Assert.NotEqual(before, after);
        Assert.True(Host.TryUndo(Editor), "Undo was refused.");
        Assert.Equal(before, Describe(Nest));
        Assert.True(Host.TryRedo(Editor), "Redo was refused.");
        Assert.Equal(after, Describe(Nest));
    }

    /// <summary>
    /// Once a <typeparamref name="TForm"/> dialog is shown, runs <paramref name="setup"/> on it
    /// and clicks its accept button (or sets OK), so a command that shows it modally goes on.
    /// </summary>
    public static void AcceptWhenShown<TForm>(Action<TForm>? setup = null, Func<TForm, bool>? ready = null) where TForm : Form =>
        WhenShown<TForm>(form =>
        {
            setup?.Invoke(form);
            if (form.AcceptButton is { } accept)
                accept.PerformClick();
            else
                form.DialogResult = DialogResult.OK;
        }, ready);

    /// <summary>
    /// Runs <paramref name="act"/> on the first visible <typeparamref name="TForm"/> once
    /// <paramref name="ready"/> (if given) holds for it.
    /// </summary>
    public static void WhenShown<TForm>(Action<TForm> act, Func<TForm, bool>? ready = null) where TForm : Form
    {
        var timer = new System.Windows.Forms.Timer { Interval = 10 };
        var deadline = DateTime.UtcNow.AddSeconds(20);
        timer.Tick += (_, _) =>
        {
            var form = Application.OpenForms.OfType<TForm>().FirstOrDefault(f => f.Visible && ready?.Invoke(f) != false);
            if (form == null && DateTime.UtcNow < deadline)
                return;
            timer.Stop();
            timer.Dispose();
            if (form != null)
                act(form);
        };
        timer.Start();
    }

    public static Nest BuildNest()
    {
        var nest = new Nest("undo");
        var square = new Drawing("square", Square(10));
        nest.Drawings.Add(square);
        int[] partsPerPlate = { 2, 2, 0, 1 };
        foreach (var count in partsPerPlate)
        {
            var plate = nest.CreatePlate();
            plate.Size = new Size(100, 100);
            for (var i = 0; i < count; i++)
                plate.Parts.Add(new Part(square, new Vector(5 + i * 20, 5)));
        }
        return nest;
    }

    public static CNC.Program Square(double side)
    {
        var program = new CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(0, side);
        program.LineTo(side, side);
        program.LineTo(side, 0);
        program.LineTo(0, 0);
        return program;
    }

    /// <summary>
    /// What the nest holds, as text: plates with their stock, settings, parts, cut-offs and
    /// clamps, drawings with their quantities, and nest-level settings. Undo compares it.
    /// </summary>
    public static string Describe(Nest nest)
    {
        var text = new StringBuilder();
        void Line(FormattableString line) => text.AppendLine(line.ToString(CultureInfo.InvariantCulture));

        Line($"machine {nest.Machine?.Id} support {nest.SupportBed != null} units {nest.Units}");
        Line($"defaults {nest.PlateDefaults.Size} {nest.PlateDefaults.PartSpacing} {nest.PlateDefaults.ClampRail != null}");
        Line($"options {nest.PlateOptions.Count} salvage {nest.SalvageRate} remnant {nest.MinimumRemnantDimension}");
        foreach (var drawing in nest.Drawings.OrderBy(d => d.Name))
        {
            var box = drawing.Program.BoundingBox();
            Line($"drawing {drawing.Name} required {drawing.Quantity.Required} codes {drawing.Program.Length} box {box.Width:F4}x{box.Length:F4} priority {drawing.Priority}");
        }
        foreach (var plate in nest.Plates)
        {
            Line($"plate {plate.Size} x{plate.Quantity} spacing {plate.PartSpacing} edge {plate.EdgeSpacing.Left},{plate.EdgeSpacing.Bottom},{plate.EdgeSpacing.Right},{plate.EdgeSpacing.Top} quadrant {plate.Quadrant}");
            Line($"  params {(plate.CuttingParameters == null ? "none" : IO.CuttingParametersSerializer.Serialize(plate.CuttingParameters))}");
            var clamps = plate.ClampZones.Select(z => FormattableString.Invariant($"{z.Id}@{z.Bounds.X:F4},{z.Bounds.Y:F4},{z.Bounds.Width:F4},{z.Bounds.Length:F4}"));
            Line($"  clamps [{string.Join(";", clamps)}] removed {plate.RemovedClamps.Count} support {plate.SupportBed != null} stock {string.Join(",", plate.StockItemCodes)}");
            foreach (var part in plate.Parts)
            {
                // A cut-off's part is regenerated from its definition, listed below; only its place in the order counts.
                if (part.BaseDrawing.IsCutOff)
                    Line($"  cutoff part");
                else
                    Line($"  part {part.BaseDrawing.Name} {part.Location.X:F4},{part.Location.Y:F4} r{part.Rotation:F6} leads {part.HasManualLeadIns} locked {part.LeadInsLocked} codes {part.Program.Length}");
            }
            foreach (var cutOff in plate.CutOffs)
                Line($"  cutoff {cutOff.Axis} {cutOff.Position.X:F4},{cutOff.Position.Y:F4}");
        }
        return text.ToString();
    }

    public static ToolStripMenuItem MenuItem(MainForm host, string name) =>
        (ToolStripMenuItem)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    /// <summary>Calls a private or internal instance method of <paramref name="target"/>.</summary>
    public static object? Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(target, args);

    /// <summary>Calls a MainForm menu handler with no sender.</summary>
    public void Click(string handler) =>
        typeof(MainForm).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Host, new object?[] { null, EventArgs.Empty });

    public static void Mouse(PlateView view, string method, MouseButtons button, System.Drawing.Point location) =>
        typeof(PlateView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { new MouseEventArgs(button, 1, location.X, location.Y, 0) });

    public static void SetBackingField(object target, string property, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                continue;
            field.SetValue(target, value);
            return;
        }
        throw new MissingFieldException(target.GetType().Name, property);
    }

    private sealed class TestHost : MainForm
    {
        // No startup migrations/automatic new document, and no settings save at close.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }
    }
}
