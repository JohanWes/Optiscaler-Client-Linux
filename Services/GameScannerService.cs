using OptiscalerClient.Helpers;
using OptiscalerClient.Models;
using OptiscalerClient.Views;
using System.IO;

namespace OptiscalerClient.Services;

public class GameScannerService
{
    private readonly IGameScanner _steamScanner;
    private readonly IGameScanner _heroicScanner;
    private readonly ExclusionService _exclusions;

    public GameScannerService() : this(null, null, null)
    {
    }

    public GameScannerService(IGameScanner? steamScanner, IGameScanner? heroicScanner, string? configPath)
    {
        _steamScanner = steamScanner ?? new SteamScanner();
        _heroicScanner = heroicScanner ?? new HeroicScanner();

        var effectiveConfigPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "config.json");
        _exclusions = new ExclusionService(effectiveConfigPath);
    }

    public async Task<List<Game>> ScanAllGamesAsync(ScanSourcesConfig? scanConfig = null)
    {
        return await Task.Run(() =>
        {
            var games = new List<Game>();
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            var analyzer = new GameAnalyzerService();
            DebugWindow.Log("[Scanner] Executing Linux game scan...");

            if (scanConfig == null)
            {
                scanConfig = new ScanSourcesConfig();
            }

            void ProcessGames(IEnumerable<Game> scannedGames)
            {
                foreach (var game in scannedGames)
                {
                    if (_exclusions.IsExcluded(game)) continue;

                    var normalizedPath = !string.IsNullOrEmpty(game.ExecutablePath)
                        ? LinuxPathHelper.NormalizePath(game.ExecutablePath)
                        : LinuxPathHelper.NormalizePath(game.InstallPath);

                    if (string.IsNullOrEmpty(normalizedPath) || seenPaths.Contains(normalizedPath))
                        continue;

                    seenPaths.Add(normalizedPath);
                    analyzer.AnalyzeGame(game);
                    games.Add(game);
                }
            }

            if (scanConfig.ScanSteam)
            {
                try
                {
                    DebugWindow.Log("[Scanner] Scanning Steam library...");
                    ProcessGames(_steamScanner.Scan());
                }
                catch (Exception ex) { DebugWindow.Log($"[Scanner] Steam scan error: {ex.Message}"); }
            }

            if (scanConfig.ScanHeroic)
            {
                try
                {
                    DebugWindow.Log("[Scanner] Scanning Heroic library...");
                    ProcessGames(_heroicScanner.Scan());
                }
                catch (Exception ex) { DebugWindow.Log($"[Scanner] Heroic scan error: {ex.Message}"); }
            }

            if (scanConfig.CustomFolders != null && scanConfig.CustomFolders.Count > 0)
            {
                DebugWindow.Log($"[Scanner] Scanning {scanConfig.CustomFolders.Count} custom folder(s)...");
                foreach (var customFolder in scanConfig.CustomFolders)
                {
                    try
                    {
                        var customGames = ScanCustomFolder(customFolder);
                        DebugWindow.Log($"[Scanner] Found {customGames.Count} games in '{customFolder}'");
                        ProcessGames(customGames);
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.Log($"[Scanner] Error scanning custom folder '{customFolder}': {ex.Message}");
                    }
                }
            }

            DebugWindow.Log($"[Scanner] Scan completed. Found {games.Count} valid games.");

            return games.OrderBy(g => g.Platform).ThenBy(g => g.Name).ToList();
        });
    }

    private List<Game> ScanCustomFolder(string rootFolder)
    {
        var games = new List<Game>();

        if (!Directory.Exists(rootFolder))
        {
            DebugWindow.Log($"[Scanner] Custom folder does not exist: {rootFolder}");
            return games;
        }

        try
        {
            var gameFolders = Directory.GetDirectories(rootFolder);

            foreach (var gameFolder in gameFolders)
            {
                try
                {
                    var exeFiles = Directory.GetFiles(gameFolder, "*.exe", SearchOption.AllDirectories);

                    foreach (var exePath in exeFiles)
                    {
                        var gameName = Path.GetFileName(gameFolder);

                        var exeName = Path.GetFileNameWithoutExtension(exePath).ToLower();
                        if (exeName.Contains("unins") || exeName.Contains("setup") ||
                            exeName.Contains("installer") || exeName.Contains("crash") ||
                            exeName.Contains("launcher") && !exeName.Contains("game"))
                        {
                            continue;
                        }

                        var game = new Game
                        {
                            Name = gameName,
                            ExecutablePath = exePath,
                            InstallPath = LinuxPathHelper.NormalizePath(gameFolder),
                            Platform = GamePlatform.Custom,
                            AppId = "Custom_" + Path.GetFileName(gameFolder)
                        };

                        games.Add(game);
                        DebugWindow.Log($"[Scanner] Found custom game: {gameName} ({Path.GetFileName(exePath)})");

                        break;
                    }
                }
                catch (Exception ex)
                {
                    DebugWindow.Log($"[Scanner] Error scanning game folder '{gameFolder}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.Log($"[Scanner] Error accessing custom folder '{rootFolder}': {ex.Message}");
        }

        return games;
    }
}
