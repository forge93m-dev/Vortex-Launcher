using System.IO;

internal static class FixturePaths
{
    // Exercise security guards on real directories, not system aliases such as
    // macOS /var -> /private/var. Tests for hostile links create their own links.
    internal static string TempRoot()
    {
        string full = Path.GetFullPath(Path.GetTempPath());
        string root = Path.GetPathRoot(full)!;
        foreach (string segment in full[root.Length..].Split(Path.DirectorySeparatorChar, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(root, segment));
            root = directory.Exists && directory.LinkTarget != null ? directory.ResolveLinkTarget(true)!.FullName : directory.FullName;
        }
        return root;
    }
}
