namespace ClaimSeeker.Engine;

/// <summary>
/// Removes everything the engine has stored, on request. The command is deliberately two-step: the
/// first run prints the folder it would delete and a phrase naming that folder, and only a second
/// run carrying that exact phrase deletes anything. It will not delete a folder that is not a
/// ClaimSeeker workspace, a drive root, or a home folder, whatever it is told.
/// </summary>
internal static class DataDeletion
{
    public static string ConfirmationPhrase(string workspace) => $"delete {Path.GetFullPath(workspace)}";

    /// <summary>Null when the folder is safe to delete; otherwise why it is not.</summary>
    public static string? Refusal(string workspace)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        if (!Directory.Exists(full)) return "There is no workspace at that path, so there is nothing to delete.";
        if (!File.Exists(Path.Combine(full, EnginePaths.Marker)))
            return "That folder is not a ClaimSeeker workspace (it has no workspace marker), so it will not be deleted.";

        var root = Path.GetPathRoot(full);
        if (root is not null && SamePath(Path.TrimEndingDirectorySeparator(root), full)) return "That is a drive root.";

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
        {
            var homeFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
            if (SamePath(homeFull, full) || homeFull.StartsWith(full + Path.DirectorySeparatorChar, PathComparison))
                return "That folder is, or contains, your home folder.";
        }

        if (new DirectoryInfo(full).LinkTarget is not null) return "That path is a link to another folder.";
        return null;
    }

    /// <summary>Deletes the workspace and reports whether it is gone.</summary>
    public static bool Delete(string workspace)
    {
        var full = Path.GetFullPath(workspace);
        DeleteTree(new DirectoryInfo(full));
        return !Directory.Exists(full);
    }

    // Walks the tree by hand so that a link inside the workspace is removed as a link. What it points
    // at is someone else's data and is never followed.
    private static void DeleteTree(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child && child.LinkTarget is null)
            {
                DeleteTree(child);
                continue;
            }

            // A read-only file refuses deletion on Windows. A link's attributes are left alone: changing
            // them would reach through to whatever it points at.
            if (entry is FileInfo && entry.LinkTarget is null) entry.Attributes = FileAttributes.Normal;
            entry.Delete();
        }

        directory.Delete(recursive: false);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool SamePath(string a, string b) => string.Equals(a, b, PathComparison);
}
