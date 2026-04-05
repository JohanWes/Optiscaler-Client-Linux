using System;
using System.IO;

namespace OptiscalerClient.Helpers;

public static class LinuxPathHelper
{
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(expanded)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool PathsEqual(string path1, string path2)
    {
        if (string.IsNullOrEmpty(path1) && string.IsNullOrEmpty(path2))
            return true;
        if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2))
            return false;

        return string.Equals(NormalizePath(path1), NormalizePath(path2), StringComparison.Ordinal);
    }

    public static string NormalizePathForComparison(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/');
    }

    public static bool IsSubpathOf(string path, string parentPath)
    {
        var normalizedPath = NormalizePathForComparison(NormalizePath(path));
        var normalizedParent = NormalizePathForComparison(NormalizePath(parentPath));

        if (!normalizedParent.EndsWith('/'))
            normalizedParent += '/';

        return normalizedPath.StartsWith(normalizedParent, StringComparison.Ordinal);
    }
}
