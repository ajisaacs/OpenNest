using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Forms.Commands
{
    /// <summary>
    /// Posts a nest: prepares and configures the post, runs pre-post verification, asks for
    /// the output path and confirms multi-file overwrites before writing. The caller keeps the
    /// nest stable (busy and cutting-inspection guards) for the command's whole lifetime.
    /// </summary>
    internal sealed class PostCommand
    {
        private readonly IWin32Window owner;
        private readonly Nest nest;
        private readonly IPostProcessor postProcessor;

        public PostCommand(IWin32Window owner, Nest nest, IPostProcessor postProcessor)
        {
            this.owner = owner;
            this.nest = nest;
            this.postProcessor = postProcessor;
        }

        public void Run()
        {
            if (postProcessor is IPostProcessorNestAware nestAware)
                nestAware.PrepareForNest(nest);

            if (postProcessor is IConfigurablePostProcessor configurable)
            {
                using var configForm = new PostProcessorConfigForm(configurable);
                if (configForm.ShowDialog(owner) != DialogResult.OK)
                    return;
            }

            // Verification runs fresh on every attempt; its consent never outlives this call.
            using (var verification = new PostVerificationForm(nest, postProcessor))
            {
                if (verification.ShowDialog(owner) != DialogResult.OK)
                    return;
            }

            using var dialog = new SaveFileDialog();
            dialog.Filter = "CNC File (*.cnc) | *.cnc";
            dialog.FileName = nest.Name;

            if (dialog.ShowDialog(owner) != DialogResult.OK)
                return;

            var path = dialog.FileName;
            if (postProcessor is IMultiFilePostProcessor multiFile)
            {
                PostMultiFile(multiFile, path);
                return;
            }

            postProcessor.Post(nest, path);
        }

        private void PostMultiFile(IMultiFilePostProcessor multiFile, string path)
        {
            var files = multiFile.GetOutputFiles(nest, path);
            if (!ConfirmOverwrite(files, path))
                return;

            try
            {
                postProcessor.Post(nest, path);
            }
            catch (Exception ex)
                when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(
                    owner,
                    ex.Message,
                    postProcessor.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            if (files.Count > 1)
                MessageBox.Show(
                    owner,
                    $"Saved {files.Count} programs, one per sheet:\n\n"
                        + string.Join("\n", files.Select(Path.GetFileName))
                        + $"\n\nin {Path.GetDirectoryName(path)}",
                    postProcessor.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
        }

        /// <summary>
        /// The save dialog only checks the chosen name; ask before replacing any
        /// other existing file a multi-file post will write.
        /// </summary>
        private bool ConfirmOverwrite(IReadOnlyList<string> files, string chosenPath)
        {
            var existing = files
                .Where(f => File.Exists(f) && !string.Equals(f, chosenPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (existing.Count == 0)
                return true;

            var answer = MessageBox.Show(
                owner,
                "These files already exist and will be replaced:\n\n"
                    + string.Join("\n", existing.Select(Path.GetFileName))
                    + "\n\nReplace them?",
                "Confirm Save",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );
            return answer == DialogResult.Yes;
        }
    }
}
