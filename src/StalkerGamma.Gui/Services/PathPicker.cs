using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace StalkerGamma.Gui.Services;

/// <summary>
/// Native folder/file pickers for the path fields, plus "reveal in file manager". Static like
/// <see cref="ConfirmDialog"/>: the pickers need the main window and there is only ever one.
/// </summary>
public static class PathPicker
{
    /// <summary>Returns the chosen directory, or null if the user cancelled.</summary>
    public static async Task<string?> PickFolderAsync(string title, string? startAt)
    {
        if (MainWindow() is not { } window)
        {
            return null;
        }
        var picked = await window.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = await NearestExistingFolderAsync(window, startAt),
            }
        );
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    /// <summary>Returns the chosen file, or null if the user cancelled.</summary>
    public static async Task<string?> PickFileAsync(
        string title,
        string? startAt,
        params string[] extensions
    )
    {
        if (MainWindow() is not { } window)
        {
            return null;
        }
        var types = new List<FilePickerFileType>();
        if (extensions.Length > 0)
        {
            types.Add(
                new FilePickerFileType(string.Join(", ", extensions))
                {
                    Patterns = Array.ConvertAll(extensions, e => $"*{e}"),
                }
            );
        }
        types.Add(FilePickerFileTypes.All);
        var picked = await window.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = types,
                SuggestedStartLocation = await NearestExistingFolderAsync(
                    window,
                    string.IsNullOrWhiteSpace(startAt) ? null : Path.GetDirectoryName(startAt)
                ),
            }
        );
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Opens a directory (or a file's directory) in the desktop file manager. Returns false with
    /// a reason when there is nothing to open — callers surface that instead of failing silently.
    /// </summary>
    public static (bool Ok, string Message) Reveal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return (false, "No path set.");
        }
        var target = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
        {
            return (false, $"{path} does not exist yet.");
        }
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = target, UseShellExecute = true }
            );
            return (true, target);
        }
        catch (Exception e)
        {
            return (false, $"Could not open {target}: {e.Message}");
        }
    }

    /// <summary>
    /// Target directories usually do not exist yet (that is the point of installing), so walk up
    /// to the closest ancestor that does — otherwise the picker opens at an arbitrary default.
    /// </summary>
    private static async Task<IStorageFolder?> NearestExistingFolderAsync(
        Window window,
        string? startAt
    )
    {
        if (string.IsNullOrWhiteSpace(startAt) || !Path.IsPathRooted(startAt))
        {
            return null;
        }
        for (var dir = startAt; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            if (Directory.Exists(dir))
            {
                return await window.StorageProvider.TryGetFolderFromPathAsync(dir);
            }
        }
        return null;
    }

    private static Window? MainWindow() =>
        Avalonia.Application.Current?.ApplicationLifetime
        is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
