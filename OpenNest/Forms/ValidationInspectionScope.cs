using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.Forms;

/// <summary>Keep live validation inputs stable while allowing navigation of the main canvas.</summary>
internal sealed class ValidationInspectionScope : IDisposable
{
    private readonly List<(Control Control, bool Enabled)> controls = new();
    private readonly List<(Form Form, bool AllowDrop)> hosts = new();
    private bool disposed;
    private readonly PlateView view;
    private readonly Control inspector;
    private readonly Control header;
    private readonly Action close;
    private Form closingHost;
    private readonly bool previousInspection;

    public ValidationInspectionScope(EditNestForm editor, Control inspector, Control header, Action close)
    {
        view = editor.PlateView;
        this.inspector = inspector;
        this.header = header;
        this.close = close;
        previousInspection = view.InspectionOnly;
        view.InspectionOnly = true;
        var root = editor.MdiParent ?? editor;
        DisableExceptCanvas(root);
        hosts.Add((root, root.AllowDrop));
        if (!ReferenceEquals(root, editor))
            hosts.Add((editor, editor.AllowDrop));
        foreach (var host in hosts)
        {
            host.Form.AllowDrop = false;
            host.Form.FormClosing += HostClosing;
        }
    }

    private void DisableExceptCanvas(Control parent)
    {
        foreach (var child in parent.Controls.Cast<Control>())
        {
            if (ReferenceEquals(child, view) || ReferenceEquals(child, inspector) || ReferenceEquals(child, header))
                continue;
            if (child.Contains(view) || child.Contains(inspector) || header != null && child.Contains(header))
                DisableExceptCanvas(child);
            else
            {
                controls.Add((child, child.Enabled));
                child.Enabled = false;
            }
        }
    }

    private void HostClosing(object sender, FormClosingEventArgs e)
    {
        // A worker may still read the nest. Finish closing the inspector first.
        e.Cancel = true;
        closingHost = (Form)sender;
        close();
    }

    internal void ResumeHostClose()
    {
        var host = closingHost;
        closingHost = null;
        if (disposed && host is { IsDisposed: false })
        {
            if (host.IsHandleCreated)
                host.BeginInvoke(new Action(host.Close));
            else
                host.Close();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        foreach (var host in hosts)
        {
            host.Form.FormClosing -= HostClosing;
            if (!host.Form.IsDisposed)
                host.Form.AllowDrop = host.AllowDrop;
        }
        foreach (var saved in controls.Where(saved => !saved.Control.IsDisposed))
            saved.Control.Enabled = saved.Enabled;
        if (!view.IsDisposed)
            view.InspectionOnly = previousInspection;
    }
}
