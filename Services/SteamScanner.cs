using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using OptiscalerClient.Helpers;
using OptiscalerClient.Models;

namespace OptiscalerClient.Services;

public class SteamScanner : IGameScanner
{
    private const string SteamRootDefault = ".local/share/Steam";
    private const string SteamRootAlt1 = ".steam/root";
    private const string SteamRootAlt2 = ".steam/steam";
    private const string LibraryFoldersFile = "steamapps/libraryfolders.vdf";
    private const string ManifestPattern = "appmanifest_*.acf";
    private readonly string? _steamRootOverride;

    public SteamScanner(string? steamRootOverride = null)
    {
        _steamRootOverride = steamRootOverride;
    }

    public List<Game> Scan()
    {
        var games = new List<Game>();
        var steamRoot = FindSteamRoot();

        if (string.IsNullOrEmpty(steamRoot))
            return games;

        var libraryPaths = GetLibraryPaths(steamRoot);

        foreach (var libraryPath in libraryPaths)
        {
            var steamappsPath = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(steamappsPath))
                continue;

            foreach (var manifestPath in Directory.EnumerateFiles(steamappsPath, ManifestPattern))
            {
                var game = ParseManifest(manifestPath, libraryPath);
                if (game != null)
                    games.Add(game);
            }
        }

        return games;
    }

    private string? FindSteamRoot()
    {
        if (!string.IsNullOrWhiteSpace(_steamRootOverride))
            return _steamRootOverride;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var possibleRoots = new[]
        {
            Path.Combine(home, SteamRootDefault),
            Path.Combine(home, SteamRootAlt1),
            Path.Combine(home, SteamRootAlt2)
        };

        foreach (var root in possibleRoots)
        {
            if (Directory.Exists(root) && File.Exists(Path.Combine(root, "steamapps", "libraryfolders.vdf")))
                return root;
        }

        return null;
    }

    private List<string> GetLibraryPaths(string steamRoot)
    {
        var paths = new List<string> { steamRoot };
        var libraryFoldersPath = Path.Combine(steamRoot, LibraryFoldersFile);

        if (!File.Exists(libraryFoldersPath))
            return paths;

        try
        {
            var content = File.ReadAllText(libraryFoldersPath);
            var matches = Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\"");
            foreach (Match match in matches)
            {
                var path = match.Groups[1].Value;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    paths.Add(path);
            }
        }
        catch
        {
        }

        return paths;
    }

    private Game? ParseManifest(string manifestPath, string libraryPath)
    {
        try
        {
            var content = File.ReadAllText(manifestPath);

            var appIdMatch = Regex.Match(content, "\"appid\"\\s*\"(\\d+)\"");
            var nameMatch = Regex.Match(content, "\"name\"\\s*\"([^\"]+)\"");
            var installDirMatch = Regex.Match(content, "\"installdir\"\\s*\"([^\"]+)\"");

            if (!appIdMatch.Success || !nameMatch.Success || !installDirMatch.Success)
                return null;

            var appId = appIdMatch.Groups[1].Value;
            var name = nameMatch.Groups[1].Value;
            var installDir = installDirMatch.Groups[1].Value;

            if (SteamCompatibilityToolDetector.IsCompatibilityTool(name, installDir))
                return null;

            var installPath = Path.Combine(libraryPath, "steamapps", "common", installDir);
            var prefixPath = Path.Combine(libraryPath, "steamapps", "compatdata", appId, "pfx");

            if (!Directory.Exists(installPath) || !Directory.Exists(prefixPath))
                return null;

            var executablePath = GameExecutableLocator.FindBestExecutable(installPath, name);

            return new Game
            {
                AppId = appId,
                Name = name,
                InstallPath = LinuxPathHelper.NormalizePath(installPath),
                ExecutablePath = executablePath ?? string.Empty,
                PrefixPath = LinuxPathHelper.NormalizePath(prefixPath),
                Platform = GamePlatform.Steam
            };
        }
        catch
        {
            return null;
        }
    }
}
