using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OptiscalerClient.Helpers;
using OptiscalerClient.Models;

namespace OptiscalerClient.Services;

public class HeroicScanner : IGameScanner
{
    private const string HeroicRootDefault = ".config/heroic";
    private const string HeroicRootFlatpak = ".var/app/com.heroicgameslauncher.hgl/config/heroic";
    private const string SideloadLibraryFile = "sideload_apps/library.json";
    private const string GamesConfigFolder = "GamesConfig";
    private const string LegendaryInstalledFile = "legendaryConfig/legendary/installed.json";
    private const string LegendaryInstallInfoCache = "store_cache/legendary_install_info.json";
    private const string GogInstallInfoCache = "store_cache/gog_install_info.json";
    private const string NileInstallInfoCache = "store_cache/nile_install_info.json";
    private const string LegendaryLibraryCache = "store_cache/legendary_library.json";
    private const string GogLibraryCache = "store_cache/gog_library.json";
    private const string NileLibraryCache = "store_cache/nile_library.json";
    private const string ZoomLibraryCache = "store_cache/zoom-library.json";
    private const string StoreConfigFile = "store/config.json";
    private readonly string? _heroicRootOverride;

    public HeroicScanner(string? heroicRootOverride = null)
    {
        _heroicRootOverride = heroicRootOverride;
    }

    private sealed class HeroicGameCandidate
    {
        public string AppName { get; set; } = "";
        public string Name { get; set; } = "";
        public string? InstallPath { get; set; }
        public string? ExecutablePath { get; set; }
        public string? PrefixPath { get; set; }
        public bool IsInstalled { get; set; }
        public string? Runner { get; set; }
    }

    private sealed class HeroicDefaults
    {
        public string? DefaultInstallPath { get; set; }
        public string? DefaultWinePrefix { get; set; }
        public string? DefaultSteamPath { get; set; }
    }

    public List<Game> Scan()
    {
        var heroicRoot = FindHeroicRoot();
        if (string.IsNullOrEmpty(heroicRoot))
            return new List<Game>();

        var candidates = new Dictionary<string, HeroicGameCandidate>(StringComparer.OrdinalIgnoreCase);
        var defaults = LoadStoreConfig(heroicRoot, candidates);
        LoadGamesConfig(heroicRoot, candidates);

        LoadSideloadLibrary(heroicRoot, candidates);
        LoadLegendaryInstalled(heroicRoot, candidates);
        LoadStoreCaches(heroicRoot, candidates);
        LoadLegendaryMetadata(heroicRoot, candidates);

        return BuildGamesFromCandidates(candidates, defaults);
    }

    private string? FindHeroicRoot()
    {
        if (!string.IsNullOrWhiteSpace(_heroicRootOverride))
            return _heroicRootOverride;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var possibleRoots = new[]
        {
            Path.Combine(home, HeroicRootDefault),
            Path.Combine(home, HeroicRootFlatpak)
        };

        foreach (var root in possibleRoots)
        {
            if (!Directory.Exists(root))
                continue;

            if (File.Exists(Path.Combine(root, SideloadLibraryFile)) ||
                File.Exists(Path.Combine(root, StoreConfigFile)) ||
                Directory.Exists(Path.Combine(root, GamesConfigFolder)) ||
                Directory.Exists(Path.Combine(root, "legendaryConfig")) ||
                Directory.Exists(Path.Combine(root, "store_cache")))
            {
                return root;
            }
        }

        return null;
    }

    private HeroicDefaults LoadStoreConfig(string heroicRoot, Dictionary<string, HeroicGameCandidate> candidates)
    {
        var defaults = new HeroicDefaults();
        var path = Path.Combine(heroicRoot, StoreConfigFile);
        if (!File.Exists(path))
            return defaults;

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("settings", out var settings))
            {
                defaults.DefaultInstallPath = NormalizeOptionalPath(GetStringProperty(settings, "defaultInstallPath"));
                defaults.DefaultWinePrefix = NormalizeOptionalPath(GetStringProperty(settings, "defaultWinePrefix") ?? GetStringProperty(settings, "winePrefix"));
                defaults.DefaultSteamPath = NormalizeOptionalPath(GetStringProperty(settings, "defaultSteamPath"));
            }

            if (doc.RootElement.TryGetProperty("games", out var gamesElement) &&
                gamesElement.TryGetProperty("recent", out var recentGames) &&
                recentGames.ValueKind == JsonValueKind.Array)
            {
                foreach (var recentGame in recentGames.EnumerateArray())
                {
                    MergeCandidate(candidates, recentGame, GetStringProperty(recentGame, "appName"), null);
                }
            }
        }
        catch
        {
        }

        return defaults;
    }

    private void LoadSideloadLibrary(string heroicRoot, Dictionary<string, HeroicGameCandidate> candidates)
    {
        var path = Path.Combine(heroicRoot, SideloadLibraryFile);
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("games", out var games))
                return;

            foreach (var gameElement in games.EnumerateArray())
            {
                var appName = GetStringProperty(gameElement, "app_name");
                if (string.IsNullOrEmpty(appName))
                    continue;

                if (!candidates.TryGetValue(appName, out var candidate))
                {
                    candidate = new HeroicGameCandidate { AppName = appName };
                    candidates[appName] = candidate;
                }

                candidate.Name = GetStringProperty(gameElement, "title") ?? candidate.Name;
                candidate.IsInstalled = GetBoolProperty(gameElement, "is_installed");

                if (gameElement.TryGetProperty("install", out var installElement))
                {
                    candidate.ExecutablePath = NormalizeOptionalPath(GetStringProperty(installElement, "executable")) ?? candidate.ExecutablePath;
                    var folderName = GetStringProperty(installElement, "folder_name");
                    if (!string.IsNullOrEmpty(folderName))
                        candidate.InstallPath = folderName;
                }

                candidate.InstallPath = GetStringProperty(gameElement, "folder_name") ?? candidate.InstallPath;

                candidate.Runner = "sideload";
            }
        }
        catch
        {
        }
    }

    private void LoadGamesConfig(string heroicRoot, Dictionary<string, HeroicGameCandidate> candidates)
    {
        var gamesConfigPath = Path.Combine(heroicRoot, GamesConfigFolder);
        if (!Directory.Exists(gamesConfigPath))
            return;

        try
        {
            foreach (var configFile in Directory.EnumerateFiles(gamesConfigPath, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(configFile);
                    using var doc = JsonDocument.Parse(json);

                    foreach (var property in doc.RootElement.EnumerateObject())
                    {
                        var configKey = property.Name;
                        if (string.IsNullOrEmpty(configKey) || configKey == "version" || configKey == "explicit")
                            continue;

                        if (!candidates.TryGetValue(configKey, out var candidate))
                        {
                            candidate = new HeroicGameCandidate { AppName = configKey };
                            candidates[configKey] = candidate;
                        }

                        string? normalizedPrefixPath = null;
                        if (property.Value.TryGetProperty("winePrefix", out var winePrefixElement))
                        {
                            normalizedPrefixPath = NormalizeOptionalPath(winePrefixElement.GetString());
                            candidate.PrefixPath = normalizedPrefixPath ?? candidate.PrefixPath;
                        }
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private void LoadLegendaryInstalled(string heroicRoot, Dictionary<string, HeroicGameCandidate> candidates)
    {
        var path = Path.Combine(heroicRoot, LegendaryInstalledFile);
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var appName = property.Name;
                if (!candidates.TryGetValue(appName, out var candidate))
                {
                    candidate = new HeroicGameCandidate { AppName = appName };
                    candidates[appName] = candidate;
                }

                var gameData = property.Value;
                candidate.Name = GetStringProperty(gameData, "title") ?? candidate.Name;
                candidate.IsInstalled = GetBoolProperty(gameData, "is_installed");

                var folderName = GetStringProperty(gameData, "folder_name");
                if (!string.IsNullOrEmpty(folderName))
                    candidate.InstallPath = folderName;

                candidate.Runner = "legendary";
            }
        }
        catch
        {
        }
    }

    private void LoadStoreCaches(string heroicRoot, Dictionary<string, HeroicGameCandidate> candidates)
    {
        LoadStoreCache(Path.Combine(heroicRoot, LegendaryLibraryCache), candidates, "legendary");
        LoadStoreCache(Path.Combine(heroicRoot, LegendaryInstallInfoCache), candidates, "legendary");
        LoadStoreCache(Path.Combine(heroicRoot, GogInstallInfoCache), candidates, "gog");
        LoadStoreCache(Path.Combine(heroicRoot, GogLibraryCache), candidates, "gog");
        LoadStoreCache(Path.Combine(heroicRoot, NileInstallInfoCache), candidates, "nile");
        LoadStoreCache(Path.Combine(heroicRoot, NileLibraryCache), candidates, "nile");
        LoadStoreCache(Path.Combine(heroicRoot, ZoomLibraryCache), candidates, "zoom");
    }

    private void LoadStoreCache(string path, Dictionary<string, HeroicGameCandidate> candidates, string runner)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("library", out var library))
            {
                MergeEntriesFromElement(library, candidates, runner);
                return;
            }

            MergeEntriesFromElement(doc.RootElement, candidates, runner);
        }
        catch
        {
        }
    }

    private void LoadLegendaryMetadata(string heroicRoot, Dictionary<string, HeroicGameCandidate> candidates)
    {
        var metadataPath = Path.Combine(heroicRoot, "legendaryConfig/legendary/metadata");
        if (!Directory.Exists(metadataPath))
            return;

        try
        {
            foreach (var metadataFile in Directory.EnumerateFiles(metadataPath, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(metadataFile);
                    using var doc = JsonDocument.Parse(json);
                    MergeCandidate(candidates, doc.RootElement, GetStringProperty(doc.RootElement, "app_name"), "legendary");
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private List<Game> BuildGamesFromCandidates(Dictionary<string, HeroicGameCandidate> candidates, HeroicDefaults defaults)
    {
        var games = new List<Game>();

        foreach (var kvp in candidates)
        {
            var candidate = kvp.Value;

            if (!candidate.IsInstalled && !HasUsableInstallPath(candidate, defaults))
                continue;

            // Ignore orphaned per-game prefix configs that have no title or install metadata.
            // Heroic can leave these behind, and scanning the whole prefix tends to pick a
            // random system executable from drive_c/windows instead of a real game.
            if (string.IsNullOrWhiteSpace(candidate.Name) &&
                string.IsNullOrWhiteSpace(candidate.InstallPath) &&
                string.IsNullOrWhiteSpace(candidate.ExecutablePath))
            {
                continue;
            }

            var game = new Game
            {
                AppId = candidate.AppName,
                Name = string.IsNullOrWhiteSpace(candidate.Name) ? candidate.AppName : candidate.Name,
                Platform = GamePlatform.Heroic,
                PrefixPath = candidate.PrefixPath
            };

            var resolved = false;

            if (!string.IsNullOrEmpty(candidate.ExecutablePath) && File.Exists(candidate.ExecutablePath))
            {
                game.ExecutablePath = LinuxPathHelper.NormalizePath(candidate.ExecutablePath);
                game.InstallPath = LinuxPathHelper.NormalizePath(Path.GetDirectoryName(candidate.ExecutablePath) ?? "");
                resolved = true;
            }

            if (!resolved)
            {
                var installPath = ResolveInstallPath(candidate.InstallPath, defaults);
                if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                {
                    game.InstallPath = installPath;
                    var exePath = GameExecutableLocator.FindBestExecutable(installPath, game.Name);
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        game.ExecutablePath = exePath;
                        game.InstallPath = LinuxPathHelper.NormalizePath(Path.GetDirectoryName(exePath) ?? installPath);
                    }

                    resolved = !string.IsNullOrEmpty(game.InstallPath) || !string.IsNullOrEmpty(game.ExecutablePath);
                }
            }

            if (!resolved && !string.IsNullOrEmpty(candidate.PrefixPath))
            {
                var normalizedPrefixPath = LinuxPathHelper.NormalizePath(candidate.PrefixPath);
                if (Directory.Exists(normalizedPrefixPath))
                {
                    game.PrefixPath = normalizedPrefixPath;
                    var searchRoot = GetPrefixSearchRoot(normalizedPrefixPath);
                    var exePath = GameExecutableLocator.FindBestExecutable(searchRoot, game.Name);
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        game.ExecutablePath = exePath;
                        game.InstallPath = LinuxPathHelper.NormalizePath(Path.GetDirectoryName(exePath) ?? "");
                        resolved = true;
                    }
                }
            }

            game.PrefixPath = ResolveGamePrefixPath(game, candidate);

            if (!resolved && string.IsNullOrEmpty(game.InstallPath) && string.IsNullOrEmpty(game.ExecutablePath))
                continue;

            if (!string.IsNullOrEmpty(game.InstallPath) || !string.IsNullOrEmpty(game.ExecutablePath))
                games.Add(game);
        }

        return games;
    }

    private void MergeEntriesFromElement(JsonElement element, Dictionary<string, HeroicGameCandidate> candidates, string runner)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    MergeCandidate(candidates, item, null, runner);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("__timestamp"))
                        continue;

                    if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                    {
                        MergeCandidate(candidates, property.Value, property.Name, runner);
                    }
                }
                break;
        }
    }

    private void MergeCandidate(Dictionary<string, HeroicGameCandidate> candidates, JsonElement element, string? fallbackAppName, string? defaultRunner)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        var appName = GetStringProperty(element, "app_name") ??
                      GetStringProperty(element, "appName") ??
                      fallbackAppName;

        if (string.IsNullOrWhiteSpace(appName) || appName == "version" || appName == "explicit")
            return;

        if (!candidates.TryGetValue(appName, out var candidate))
        {
            candidate = new HeroicGameCandidate { AppName = appName };
            candidates[appName] = candidate;
        }

        candidate.Name = FirstNonEmpty(
            GetStringProperty(element, "title"),
            GetStringProperty(element, "app_title"),
            candidate.Name) ?? candidate.Name;

        candidate.Runner = FirstNonEmpty(
            GetStringProperty(element, "runner"),
            defaultRunner,
            candidate.Runner);

        if (TryGetBoolProperty(element, "is_installed", out var isInstalled) ||
            TryGetBoolProperty(element, "installed", out isInstalled))
        {
            candidate.IsInstalled = isInstalled;
        }

        candidate.InstallPath = FirstNonEmpty(
            GetStringProperty(element, "folder_name"),
            GetStringProperty(element, "install_path"),
            GetStringProperty(element, "path"),
            candidate.InstallPath);

        candidate.ExecutablePath = FirstNonEmpty(
            NormalizeOptionalPath(GetStringProperty(element, "executable")),
            candidate.ExecutablePath);

        candidate.PrefixPath = FirstNonEmpty(
            NormalizeOptionalPath(GetStringProperty(element, "winePrefix")),
            NormalizeOptionalPath(GetStringProperty(element, "wine_prefix")),
            candidate.PrefixPath);

        if (element.TryGetProperty("install", out var installElement) && installElement.ValueKind == JsonValueKind.Object)
        {
            candidate.ExecutablePath = FirstNonEmpty(
                NormalizeOptionalPath(GetStringProperty(installElement, "executable")),
                candidate.ExecutablePath);

            candidate.InstallPath = FirstNonEmpty(
                GetStringProperty(installElement, "folder_name"),
                GetStringProperty(installElement, "install_path"),
                candidate.InstallPath);
        }

        if (element.TryGetProperty("metadata", out var metadataElement) && metadataElement.ValueKind == JsonValueKind.Object)
        {
            candidate.Name = FirstNonEmpty(
                GetStringProperty(metadataElement, "title"),
                GetStringProperty(metadataElement, "description"),
                candidate.Name) ?? candidate.Name;

            if (metadataElement.TryGetProperty("customAttributes", out var attributes) &&
                attributes.ValueKind == JsonValueKind.Object &&
                attributes.TryGetProperty("FolderName", out var folderAttribute) &&
                folderAttribute.ValueKind == JsonValueKind.Object)
            {
                candidate.InstallPath = FirstNonEmpty(
                    GetStringProperty(folderAttribute, "value"),
                    candidate.InstallPath);
            }
        }
    }

    private static bool HasUsableInstallPath(HeroicGameCandidate candidate, HeroicDefaults defaults)
    {
        if (!string.IsNullOrEmpty(candidate.ExecutablePath) && File.Exists(candidate.ExecutablePath))
            return true;

        var installPath = ResolveInstallPath(candidate.InstallPath, defaults);
        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            return true;

        return !string.IsNullOrEmpty(candidate.PrefixPath) && Directory.Exists(candidate.PrefixPath);
    }

    private static string? ResolveInstallPath(string? installPath, HeroicDefaults defaults)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return null;

        var trimmed = installPath.Trim();
        if (Path.IsPathRooted(trimmed))
            return NormalizeOptionalPath(trimmed);

        if (!string.IsNullOrEmpty(defaults.DefaultInstallPath))
        {
            var combined = NormalizeOptionalPath(Path.Combine(defaults.DefaultInstallPath, trimmed));
            if (!string.IsNullOrEmpty(combined) && Directory.Exists(combined))
                return combined;
        }

        return trimmed;
    }

    private static string GetPrefixSearchRoot(string prefixPath)
    {
        var driveCPath = Path.Combine(prefixPath, "drive_c");
        return Directory.Exists(driveCPath) ? driveCPath : prefixPath;
    }

    private static string? ResolveGamePrefixPath(Game game, HeroicGameCandidate candidate)
    {
        var candidatePrefix = NormalizeOptionalPath(candidate.PrefixPath);
        if (!string.IsNullOrWhiteSpace(candidatePrefix) && Directory.Exists(candidatePrefix))
            return candidatePrefix;

        var derivedPrefix = DerivePrefixPathFromGame(game);
        if (!string.IsNullOrWhiteSpace(derivedPrefix) && Directory.Exists(derivedPrefix))
            return derivedPrefix;

        return candidatePrefix ?? derivedPrefix;
    }

    private static string? DerivePrefixPathFromGame(Game game)
    {
        foreach (var path in new[] { game.ExecutablePath, game.InstallPath })
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var normalizedPath = path.Replace('\\', '/');
            var driveCIndex = normalizedPath.IndexOf("/drive_c/", StringComparison.OrdinalIgnoreCase);
            if (driveCIndex > 0)
            {
                return NormalizeOptionalPath(normalizedPath[..driveCIndex]);
            }
        }

        return null;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
    }

    private static bool GetBoolProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.GetBoolean();
    }

    private static bool TryGetBoolProperty(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out var prop))
            return false;

        if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
        {
            value = prop.GetBoolean();
            return true;
        }

        return false;
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return LinuxPathHelper.NormalizePath(path);
        }
        catch
        {
            return path;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
