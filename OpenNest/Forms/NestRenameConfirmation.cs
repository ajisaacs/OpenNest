using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// Nest Info renames in Database mode: warns before taking a name another active server
    /// nest already has. A check that fails or takes over 3 seconds lets the rename through.
    /// </summary>
    internal static class NestRenameConfirmation
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Null when no other nest is known to have <paramref name="name"/>; otherwise whether
        /// the user chose to use the duplicate anyway.
        /// </summary>
        public static async Task<bool?> ConfirmAsync(Control owner, INestRepository repository, string name, Guid ownId)
        {
            bool? taken;
            owner.UseWaitCursor = true;
            try
            {
                using var timeout = new CancellationTokenSource(Timeout);
                taken = await NestNameConflicts.TryIsNameTakenAsync(repository, name, ownId, timeout.Token);
            }
            finally
            {
                owner.UseWaitCursor = false;
            }
            if (taken != true)
                return null;

            return MessageBox.Show(owner,
                $"A nest named \"{name}\" is already on the server. Use this name anyway?",
                "Nest Info", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }
}
