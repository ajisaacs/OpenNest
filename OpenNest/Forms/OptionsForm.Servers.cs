using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Desktop;

namespace OpenNest.Forms
{
    // The Nest Server page: the saved servers in an editable grid and the one in use picked
    // from a dropdown of its rows. Nothing is written until the dialog's Save, which checks
    // every row first.
    public partial class OptionsForm
    {
        private const string ServerNameColumn = "ServerName";
        private const string ServerUrlColumn = "ServerUrl";
        private const string ServerStateColumn = "ServerState";

        // Tracked by row, not address, so editing the active server's address keeps it active.
        private DataGridViewRow activeServerRow;
        private bool syncingServers;
        private Font activeServerFont;

        private void BuildServerGrid()
        {
            serverGrid.AutoGenerateColumns = false;
            serverGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = ServerNameColumn,
                HeaderText = "Name",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 40,
                MinimumWidth = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
            serverGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = ServerUrlColumn,
                HeaderText = "URL",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 60,
                MinimumWidth = 140,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
            serverGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = ServerStateColumn,
                HeaderText = "",
                ReadOnly = true,
                Width = 64,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });

            serverGrid.CellValueChanged += (_, _) => RefreshActiveServerChoices();
            serverGrid.RowsAdded += (_, _) => RefreshActiveServerChoices();
            serverGrid.RowsRemoved += (_, _) => RefreshActiveServerChoices();
            serverGrid.UserDeletingRow += ServerGrid_UserDeletingRow;
            serverGrid.CellFormatting += ServerGrid_CellFormatting;
            activeServerCombo.SelectedIndexChanged += ActiveServerCombo_SelectedIndexChanged;
            Disposed += (_, _) => activeServerFont?.Dispose();
        }

        private void LoadServers()
        {
            var settings = NestStorage.Settings;
            syncingServers = true;
            serverGrid.Rows.Clear();
            activeServerRow = null;
            foreach (var server in settings.Servers)
            {
                var row = serverGrid.Rows[serverGrid.Rows.Add(server.Name, server.Url)];
                if (string.Equals(server.Url, settings.ServerUrl, StringComparison.OrdinalIgnoreCase))
                    activeServerRow = row;
            }
            syncingServers = false;
            RefreshActiveServerChoices();
        }

        /// <summary>Lists the grid's servers in the dropdown, keeping the active one selected.</summary>
        private void RefreshActiveServerChoices()
        {
            if (syncingServers)
                return;

            syncingServers = true;
            activeServerCombo.BeginUpdate();
            activeServerCombo.Items.Clear();
            foreach (DataGridViewRow row in serverGrid.Rows)
            {
                if (!row.IsNewRow && CellText(row, ServerUrlColumn).Length > 0)
                    activeServerCombo.Items.Add(new ServerChoice(row));
            }

            // A PC with servers always has one in use: with none yet, the first one added becomes it.
            // An active row whose address is being retyped stays active while it is blank.
            if (activeServerRow?.DataGridView == null)
                activeServerRow = (activeServerCombo.Items.Count > 0 ? (ServerChoice)activeServerCombo.Items[0] : null)?.Row;
            activeServerCombo.SelectedItem = activeServerCombo.Items.Cast<ServerChoice>()
                .FirstOrDefault(choice => choice.Row == activeServerRow);
            activeServerCombo.EndUpdate();
            syncingServers = false;
            serverGrid.Invalidate();
        }

        private void ActiveServerCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (syncingServers)
                return;
            activeServerRow = (activeServerCombo.SelectedItem as ServerChoice)?.Row;
            serverGrid.Invalidate();
        }

        private void ServerGrid_UserDeletingRow(object sender, DataGridViewRowCancelEventArgs e)
        {
            if (e.Row != activeServerRow)
                return;
            e.Cancel = true;
            ShowActiveServerKept();
        }

        private void RemoveServer_Click(object sender, EventArgs e)
        {
            var row = serverGrid.CurrentRow;
            if (row == null || row.IsNewRow)
                return;
            if (row == activeServerRow)
            {
                ShowActiveServerKept();
                return;
            }

            serverGrid.Rows.Remove(row);
        }

        private void ShowActiveServerKept() =>
            ShowNotice(new OptionNotice(NestServerListOption.Title,
                "This is the active server. Choose another active server before removing it.",
                OptionNoticeKind.Information));

        private void ServerGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || serverGrid.Columns[e.ColumnIndex].Name != ServerStateColumn)
                return;

            var row = serverGrid.Rows[e.RowIndex];
            if (row != activeServerRow)
            {
                e.Value = "";
                return;
            }

            activeServerFont ??= new Font(serverGrid.Font, FontStyle.Bold);
            e.Value = "Active";
            e.CellStyle.Font = activeServerFont;
            e.CellStyle.ForeColor = NavAccentColor;
            e.CellStyle.SelectionForeColor = NavAccentColor;
            e.FormattingApplied = true;
        }

        /// <summary>
        /// Checks the grid and saves it when it changed; false (after telling the operator) when a
        /// row is bad or the write failed, so the dialog stays open.
        /// </summary>
        private bool SaveServers()
        {
            serverGrid.EndEdit();
            var rows = new List<(string Name, string Url)>();
            var activeIndex = -1;
            foreach (DataGridViewRow row in serverGrid.Rows)
            {
                if (row.IsNewRow)
                    continue;
                if (row == activeServerRow)
                    activeIndex = rows.Count;
                rows.Add((CellText(row, ServerNameColumn), CellText(row, ServerUrlColumn)));
            }

            var (problem, servers, activeUrl) = NestServerListOption.Validate(rows, activeIndex);
            if (problem != null)
            {
                navList.SelectedIndex = Array.IndexOf(pages, serverPage);
                ShowNotice(problem);
                return false;
            }

            var settings = NestStorage.Settings;
            if (string.Equals(activeUrl, settings.ServerUrl, StringComparison.OrdinalIgnoreCase)
                && servers.SequenceEqual(settings.Servers))
                return true;

            try
            {
                NestStorage.SaveServers(servers, activeUrl);
                return true;
            }
            catch (Exception ex)
            {
                navList.SelectedIndex = Array.IndexOf(pages, serverPage);
                ShowNotice(new OptionNotice(NestServerListOption.Title,
                    $"Could not save the nest servers: {ex.Message}", OptionNoticeKind.Error));
                return false;
            }
        }

        private static string CellText(DataGridViewRow row, string column) =>
            (row.Cells[column].Value as string)?.Trim() ?? "";

        private sealed class ServerChoice
        {
            public ServerChoice(DataGridViewRow row) => Row = row;

            public DataGridViewRow Row { get; }

            public override string ToString() =>
                new SavedNestServer(CellText(Row, ServerNameColumn), CellText(Row, ServerUrlColumn)).ToString();
        }
    }
}
