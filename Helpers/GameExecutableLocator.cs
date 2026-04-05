using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OptiscalerClient.Helpers;

public static class GameExecutableLocator
{
    private const long MinExecutableSizeBytes = 5 * 1024 * 1024;

    private static readonly string[] LauncherExePatterns = new[]
    {
        "unins", "setup", "crash", "redist", "unrealcefsubprocess", "launcher"
    };

    private static readonly string[] IgnoredPathFragments = new[]
    {
        "/windows/",
        "/program files/common files/",
        "/program files/electronic arts/ea desktop/",
        "/program files (x86)/electronic arts/ea desktop/"
    };

    private static readonly string[] UpscalerDllNames = new[]
    {
        "nvngx", "dlss", "xess", "amd", "sl.interposer"
    };

    public static string? FindBestExecutable(string rootPath, string gameName)
    {
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            return null;

        var candidates = new List<(string Path, int Score)>();

        foreach (var exePath in Directory.EnumerateFiles(rootPath, "*.exe", SearchOption.AllDirectories))
        {
            var score = ScoreExecutable(exePath, gameName, rootPath);
            if (score > 0)
            {
                candidates.Add((exePath, score));
            }
        }

        return candidates.OrderByDescending(c => c.Score).FirstOrDefault().Path;
    }

    private static int ScoreExecutable(string exePath, string gameName, string rootPath)
    {
        var normalizedPath = exePath.Replace('\\', '/');
        var normalizedLower = normalizedPath.ToLowerInvariant();
        var fileName = Path.GetFileName(exePath);

        foreach (var ignoredFragment in IgnoredPathFragments)
        {
            if (normalizedLower.Contains(ignoredFragment, StringComparison.Ordinal))
            {
                return 0;
            }
        }

        if (IsLauncherExe(fileName))
        {
            return 0;
        }

        var score = 0;

        var nameNormalized = gameName.Replace(" ", "").ToLowerInvariant();
        var fileNameNormalized = fileName.Replace(" ", "").Replace(".exe", "").ToLowerInvariant();

        if (fileNameNormalized.Contains(nameNormalized) || nameNormalized.Contains(fileNameNormalized))
        {
            score += 20;
        }

        if (normalizedPath.Contains("/Binaries/Win64/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("/Binaries/Win32/", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        try
        {
            var fileInfo = new FileInfo(exePath);
            if (fileInfo.Length >= MinExecutableSizeBytes)
            {
                score += 10;
            }
        }
        catch
        {
        }

        if (HasUpscalerDllNearby(exePath, rootPath))
        {
            score += 15;
        }

        return score;
    }

    private static bool IsLauncherExe(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        foreach (var pattern in LauncherExePatterns)
        {
            if (lower.Contains(pattern))
                return true;
        }
        return false;
    }

    private static bool HasUpscalerDllNearby(string exePath, string rootPath)
    {
        var exeDir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(exeDir))
            return false;

        try
        {
            foreach (var dllPath in Directory.EnumerateFiles(exeDir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var dllName = Path.GetFileName(dllPath).ToLowerInvariant();
                foreach (var upscalerName in UpscalerDllNames)
                {
                    if (dllName.Contains(upscalerName))
                        return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }
}
