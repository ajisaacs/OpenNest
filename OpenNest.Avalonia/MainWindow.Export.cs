using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenNest.Avalonia.Rendering;
using OpenNest.Data;
using OpenNest.Desktop;

namespace OpenNest.Avalonia
{
    /// <summary>
    /// File &gt; Export Plate, Export All Plates, Nest Report and Export Nest Report, and Plate &gt;
    /// View in CAD, as the WinForms app has them: the same suggested file names and writers
    /// (<see cref="PlateExport"/>, <see cref="NestReportExport"/>). A viewed report opens in the
    /// system's PDF viewer, which saves and prints it; View in CAD opens the plate's DXF in the
    /// program the system associates with .dxf.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly FilePickerFileType DxfFiles =
            new("DXF file") { Patterns = new[] { "*.dxf" } };

        private static readonly FilePickerFileType ImageFiles =
            new("Image as displayed") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } };

        private static readonly FilePickerFileType PlacementFiles =
            new("Locations and rotations") { Patterns = new[] { "*.txt" } };

        private static readonly FilePickerFileType SvgFiles =
            new("SVG file") { Patterns = new[] { "*.svg" } };

        private static readonly FilePickerFileType PdfFiles =
            new("PDF file") { Patterns = new[] { "*.pdf" } };

        private void UpdateExportCommands(bool hasNest)
        {
            ExportPlateMenuItem.IsEnabled = hasNest;
            ExportAllPlatesMenuItem.IsEnabled = hasNest;
            ViewNestReportMenuItem.IsEnabled = hasNest;
            ExportNestReportMenuItem.IsEnabled = hasNest;
            ViewInCadMenuItem.IsEnabled = hasNest;
        }

        private async void OnExportPlate(object sender, RoutedEventArgs e)
        {
            var view = ActiveView;
            if (view == null || !CanExport())
                return;
            writingFile = true;
            try
            {
                await ExportPlateAsync(view, view.PlateIndex);
            }
            finally
            {
                writingFile = false;
            }
        }

        /// <summary>
        /// As WinForms: Export Plate once per plate, first to last, each plate shown while its
        /// file is chosen; cancelling or a failure stops the rest.
        /// </summary>
        private async void OnExportAllPlates(object sender, RoutedEventArgs e)
        {
            var view = ActiveView;
            if (view == null || !CanExport())
                return;
            writingFile = true;
            try
            {
                var count = view.PlateCount;
                var exported = await PlateExport.ExportAllAsync(count, i => ExportPlateAsync(view, i));
                if (exported < count)
                    ShowNotice($"Exported {exported} of {count} plates of {view.Tab.Name}");
                else
                    ShowNotice($"Exported all {count} plates of {view.Tab.Name}");
            }
            finally
            {
                writingFile = false;
            }
        }

        // One export at a time; nesting does not block it, as its result is kept on this thread.
        private bool CanExport()
        {
            if (!writingFile)
                return true;
            ShowNotice("A nest is already being saved or exported. Finish that before exporting.");
            return false;
        }

        /// <summary>
        /// Shows plate <paramref name="index"/>, asks for its file and writes it in the format the
        /// file's extension names. True when it was written.
        /// </summary>
        private async Task<bool> ExportPlateAsync(NestView view, int index)
        {
            if (index < 0 || index >= view.PlateCount)
                return false;
            view.ShowPlate(index);
            var plate = view.PlateView.Plate;
            if (plate == null)
                return false;
            var nest = view.Tab.Document.Nest;
            var number = index + 1;
            var target = await PickPlateExportFileAsync(view.Tab, PlateExport.SuggestedFileName(nest.Name, number));
            if (target == null)
                return false;

            var (path, format) = target.Value;
            try
            {
                // The image is of the plate as shown, so show it again if another was picked meanwhile.
                if (format == PlateExportFormat.Image && !ReferenceEquals(view.PlateView.Plate, plate))
                    view.ShowPlate(nest.Plates.IndexOf(plate));
                PlateExport.Write(plate, nest.Units, path, format, file => ViewImage.Save(view.PlateView, file));
            }
            catch (Exception ex)
            {
                await ConfirmDialog.ShowAsync(this, "Export Plate",
                    $"Could not export plate to '{path}': {ex.Message}");
                return false;
            }
            ShowNotice($"Exported plate {number} of {view.Tab.Name} to {path}");
            return true;
        }

        // The file to export a plate to and its format, or null when cancelled or refused.
        private async Task<(string Path, PlateExportFormat Format)?> PickPlateExportFileAsync(
            NestTab tab, string suggestedFileName)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Plate",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "dxf",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { DxfFiles, ImageFiles, PlacementFiles, SvgFiles },
                SuggestedStartLocation = await NestFolderAsync(tab),
            });
            if (file == null)
                return null;
            var path = file.TryGetLocalPath();
            if (path == null)
            {
                ShowNotice("Plates can only be exported to a folder on this computer.");
                return null;
            }

            // A name typed without an extension is the dialog's default type, DXF.
            if (Path.GetExtension(path).Length == 0)
            {
                path += ".dxf";
                if (File.Exists(path) && !await ConfirmDialog.AskAsync(this, "Export Plate",
                        $"{Path.GetFileName(path)} already exists. Replace it?", "Replace"))
                    return null;
            }
            if (PlateExport.FromPath(path) is not { } format)
            {
                await ConfirmDialog.ShowAsync(this, "Export Plate",
                    $"A plate cannot be exported as '{Path.GetFileName(path)}'. Choose a file name "
                    + "ending in .dxf, .png, .jpg, .txt or .svg.");
                return null;
            }
            return (path, format);
        }

        /// <summary>Writes the shown plate as a DXF in the temp folder and opens it in the system's CAD program.</summary>
        private async void OnViewInCad(object sender, RoutedEventArgs e)
        {
            var view = ActiveView;
            var plate = view?.PlateView.Plate;
            if (plate == null)
                return;
            var nest = view.Tab.Document.Nest;
            var path = PlateExport.ExternalCadPath(nest.Name, view.PlateIndex + 1);
            Task<string> opened;
            try
            {
                PlateExport.Write(plate, nest.Units, path, PlateExportFormat.Dxf, null);
                opened = ExternalFileLauncher.OpenAsync(path);
            }
            catch (Exception ex)
            {
                await ConfirmDialog.ShowAsync(this, "View in CAD", $"Could not open '{path}' in CAD: {ex.Message}");
                return;
            }
            ShowNotice($"Opening {path}");
            await ReportOpenFailureAsync(opened, "View in CAD", $"Could not open '{path}' in CAD");
        }

        /// <summary>
        /// Writes the nest report to a private temporary folder and opens it in the system's PDF
        /// viewer, which saves and prints it. Viewed reports older than a day are removed.
        /// </summary>
        private async void OnViewNestReport(object sender, RoutedEventArgs e)
        {
            var tab = tabs.Active;
            if (tab == null || !TryReportFileName(tab, out var fileName))
                return;
            if (!CanWrite(tab, "creating the report"))
                return;
            writingFile = true;
            string path = null;
            try
            {
                await MachinesLoaded;
                // Auto Nest may have started on it while the machines were read.
                if (IsNesting(tab, "creating the report"))
                    return;
                path = NestReportExport.CreateViewPath(fileName);
                await WriteReportAsync(tab, path);
                await Task.Run(() => NestReportExport.DeleteViewedReportsBefore(
                    DateTime.UtcNow - NestReportExport.ViewedReportLifetime));
            }
            catch (Exception ex) when (IsReportFailure(ex))
            {
                if (path != null)
                    NestReportExport.DeleteReportFolder(path);
                await ReportFailureAsync(tab, ex);
                return;
            }
            finally
            {
                writingFile = false;
            }

            Task<string> opened;
            try
            {
                opened = ExternalFileLauncher.OpenAsync(path);
            }
            catch (Exception ex)
            {
                await ConfirmDialog.ShowAsync(this, "Nest Report",
                    $"The nest report was written to '{path}', but it could not be opened: {ex.Message}");
                return;
            }
            ShowNotice($"Opening the nest report for {tab.Name}");
            await ReportOpenFailureAsync(opened, "Nest Report",
                $"The nest report was written to '{path}', but it could not be opened");
        }

        /// <summary>Saves the nest report as a PDF file the user picks.</summary>
        private async void OnExportNestReport(object sender, RoutedEventArgs e)
        {
            var tab = tabs.Active;
            if (tab == null || !TryReportFileName(tab, out var fileName))
                return;
            if (!CanWrite(tab, "exporting the report"))
                return;
            writingFile = true;
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Nest Report",
                    SuggestedFileName = fileName,
                    DefaultExtension = "pdf",
                    ShowOverwritePrompt = true,
                    FileTypeChoices = new[] { PdfFiles },
                    SuggestedStartLocation = await NestFolderAsync(tab),
                });
                if (file == null)
                    return;
                var path = file.TryGetLocalPath();
                if (path == null)
                {
                    ShowNotice("The nest report can only be saved to a folder on this computer.");
                    return;
                }
                await MachinesLoaded;
                // Auto Nest may have started on it while the file picker was open.
                if (IsNesting(tab, "exporting the report"))
                    return;
                await WriteReportAsync(tab, path);
                ShowNotice($"Saved nest report to {path}");
            }
            catch (Exception ex) when (IsReportFailure(ex))
            {
                await ReportFailureAsync(tab, ex);
            }
            finally
            {
                writingFile = false;
            }
        }

        // The report's file name, or false (after saying why) when the nest's name cannot be one.
        private bool TryReportFileName(NestTab tab, out string fileName)
        {
            try
            {
                fileName = NestReportExport.ReportFileName(tab.Document.Nest);
                return true;
            }
            catch (InvalidOperationException ex)
            {
                fileName = null;
                _ = ConfirmDialog.ShowAsync(this, "Nest Report", $"Could not create the nest report: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Captures the report on this thread, so the nest is read as it stands and is left
        /// unchanged, then writes the detached snapshot off it.
        /// </summary>
        private static Task WriteReportAsync(NestTab tab, string path)
        {
            var snapshot = NestReportExport.Capture(
                tab.Document.Nest,
                DateTimeOffset.Now,
                DesktopSettings.MachineConfigs,
                UserSettingsFile.Read(UserSettingsFile.DefaultPath));
            return Task.Run(() => NestReportExport.Write(snapshot, path));
        }

        private static bool IsReportFailure(Exception ex) =>
            ex is InvalidOperationException
                or NotSupportedException
                or InvalidDataException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException;

        private Task ReportFailureAsync(NestTab tab, Exception error) =>
            ConfirmDialog.ShowAsync(this, "Nest Report",
                $"Could not create the nest report for '{tab.Name}': {error.Message}");

        // Says why the system could not open a file, once its opener has reported back.
        private async Task ReportOpenFailureAsync(Task<string> opened, string title, string what)
        {
            string failure;
            try
            {
                failure = await opened;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
            if (failure != null)
                await ConfirmDialog.ShowAsync(this, title, $"{what}: {failure}");
        }

        private async Task<IStorageFolder> NestFolderAsync(NestTab tab)
        {
            var folder = tab.Path != null ? Path.GetDirectoryName(tab.Path) : null;
            return folder != null ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null;
        }
    }
}
