using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

[Collection("Fill operation lifetime")]
public class EditNestFocusTraversalTests
{
    [Fact]
    public void NestedActiveContainersReturnTheirLeaf() => RunSta(() =>
    {
        using var form = CreateForm();
        var outer = new UserControl();
        var inner = new UserControl();
        var leaf = new TextBox();
        form.Controls.Add(outer);
        outer.Controls.Add(inner);
        inner.Controls.Add(leaf);
        inner.ActiveControl = leaf;
        outer.ActiveControl = inner;
        form.ActiveControl = outer;

        Assert.Same(outer, form.ActiveControl);
        Assert.Same(inner, outer.ActiveControl);
        Assert.Same(leaf, inner.ActiveControl);
        Assert.Same(leaf, GetFocusedControl(form));
    });

    [Fact]
    public void OrdinaryActiveControlIsReturnedUnchanged() => RunSta(() =>
    {
        using var form = CreateForm();
        var leaf = new TextBox();
        form.Controls.Add(leaf);
        form.ActiveControl = leaf;

        Assert.Same(leaf, form.ActiveControl);
        Assert.Same(leaf, GetFocusedControl(form));
    });

    [Fact]
    public void NoActiveControlReturnsTheForm() => RunSta(() =>
    {
        using var form = CreateForm();
        form.ActiveControl = null;

        Assert.Null(form.ActiveControl);
        Assert.Same(form, GetFocusedControl(form));
    });

    [Fact]
    public void NestedContainerWithoutActiveControlIsReturned() => RunSta(() =>
    {
        using var form = CreateForm();
        var outer = new UserControl();
        var inner = new UserControl();
        form.Controls.Add(outer);
        outer.Controls.Add(inner);
        inner.ActiveControl = null;
        outer.ActiveControl = inner;
        form.ActiveControl = outer;

        Assert.Same(outer, form.ActiveControl);
        Assert.Same(inner, outer.ActiveControl);
        Assert.Null(inner.ActiveControl);
        Assert.Same(inner, GetFocusedControl(form));
    });

    [Fact]
    public void PlateListRefreshRestoresTheOriginalNestedLeaf() => RunSta(() =>
    {
        using var form = CreateForm();
        form.Width = 1100;
        form.Height = 600;
        form.Show();
        var outer = new UserControl();
        var inner = new UserControl { Dock = DockStyle.Fill };
        var original = new TextBox { Top = 10 };
        var replacement = new TextBox { Top = 50 };
        inner.Controls.AddRange(new Control[] { original, replacement });
        outer.Controls.Add(inner);
        form.ShowSidePanel(outer, "Focus regression");
        var plates = PlateList(form);
        SelectPlatesTab(form, plates); // Drawings is the default tab; the list can't take focus while hidden.
        Assert.True(original.Focus());
        Assert.True(form.ContainsFocus);
        Assert.Same(original, inner.ActiveControl);

        // Force a different leaf to become active during the real refresh. Merely
        // focusing the old outer container would now restore this replacement.
        var focusWasDisplaced = false;
        plates.SelectedIndexChanged += (_, _) =>
        {
            Assert.True(replacement.Focus());
            focusWasDisplaced = true;
        };

        form.UpdatePlateList();

        Assert.True(focusWasDisplaced);
        Assert.True(original.Focused);
        Assert.False(replacement.Focused);
        Assert.Same(original, GetFocusedControl(form));
        Assert.Equal(form.Nest.Plates.Count, plates.Items.Count);
        Assert.Equal(form.PlateManager.CurrentIndex, Assert.Single(plates.SelectedIndices.Cast<int>()));
    });

    [Fact]
    public void PlateListRefreshKeepsFocusOnThePlateList() => RunSta(() =>
    {
        using var form = CreateForm();
        form.Show();
        var plates = PlateList(form);
        SelectPlatesTab(form, plates); // Drawings is the default tab; the list can't take focus while hidden.
        Assert.True(plates.Focus());

        form.UpdatePlateList();

        Assert.True(plates.Focused);
        Assert.Same(plates, GetFocusedControl(form));
        Assert.Equal(form.Nest.Plates.Count, plates.Items.Count);
    });

    private static EditNestForm CreateForm()
    {
        var form = new EditNestForm(new Nest("focus traversal"));
        form.PlateView.SetOverlapAutoCheck(null);
        return form;
    }

    private static Control GetFocusedControl(EditNestForm form)
    {
        var method = typeof(EditNestForm).GetMethod("GetFocusedControl", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Control>(method.Invoke(form, null));
    }

    private static ListView PlateList(EditNestForm form) =>
        Assert.IsType<ListView>(Assert.Single(form.Controls.Find("platesListView", true)));

    private static void SelectPlatesTab(EditNestForm form, ListView plates)
    {
        var tabControl = Assert.IsType<TabControl>(Assert.Single(form.Controls.Find("tabControl1", true)));
        tabControl.SelectedTab = Assert.IsType<TabPage>(plates.Parent);
    }

    private static void RunSta(System.Action testBody)
    {
        Exception? testFailure = null;
        var staThread = new Thread(() =>
        {
            try
            {
                testBody();
            }
            catch (Exception error)
            {
                testFailure = error;
            }
        })
        { IsBackground = true };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        Assert.True(staThread.Join(TimeSpan.FromSeconds(60)), "The focus traversal STA test did not complete.");
        if (testFailure != null)
            ExceptionDispatchInfo.Capture(testFailure).Throw();
    }
}
