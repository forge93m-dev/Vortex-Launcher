using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VortexLauncher.Core.Diagnostics;

namespace VortexLauncher.Views;

internal static class DiagnosticReportExport
{
    internal static async Task<string> SaveAsync(Window owner, string? instance, IReadOnlyList<string>? lines = null)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save diagnostic report (local ZIP; nothing is uploaded)",
            SuggestedFileName = "Vortex-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip",
            DefaultExtension = "zip", FileTypeChoices = new[] { new FilePickerFileType("Diagnostic ZIP") { Patterns = new[] { "*.zip" } } }
        });
        if (file == null) return "Report export cancelled.";
        string path = file.TryGetLocalPath() ?? throw new InvalidOperationException("Choose a local folder.");
        await Task.Run(() => DiagnosticReport.CreateAsync(path, instance, lines));
        return "Report saved: " + path + ". Review before sharing; nothing was uploaded.";
    }
}
