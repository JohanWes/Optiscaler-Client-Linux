using OptiscalerClient.Models;
using OptiscalerClient.Views;
using System.IO;
using System.Runtime.Versioning;

namespace OptiscalerClient.Services;

[SupportedOSPlatform("windows")]
public class GameScannerService
{
    private readonly SteamScanner _steamScanner;
    private readonly EpicScanner _epicScanner;
    private readonly GogScanner _gogScanner;
    private readonly XboxScanner _xboxScanner;
    private readonly EaScanner _eaScanner;
    private readonly BattleNetScanner _battleNetScanner;
    private readonly UbisoftScanner _ubisoftScanner;
    private readonly ExclusionService _exclusions;

    public GameScannerService()
    {
        _steamScanner = new SteamScanner();
        _epicScanner = new EpicScanner();
        _gogScanner = new GogScanner();
        _xboxScanner = new XboxScanner();
        _eaScanner = new EaScanner();
        _battleNetScanner = new BattleNetScanner();
        _ubisoftScanner = new UbisoftScanner();

        // config.json lives next to the executable (copied by the build)
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        _exclusions = new ExclusionService(configPath);
    }

    public async Task<List<Game>> ScanAllGamesAsync()
    {
        return await Task.Run(() =>
        {
            var games = new List<Game>();
            var analyzer = new GameAnalyzerService();
            DebugWindow.Log("[Scanner] Executing global game scan across all platforms...");

            void ProcessGames(IEnumerable<Game> scannedGames)
            {
                foreach (var game in scannedGames)
                {
                    if (_exclusions.IsExcluded(game)) continue;
                    analyzer.AnalyzeGame(game);
                    games.Add(game);
                }
            }

            try
            {
                DebugWindow.Log("[Scanner] Scanning Steam library...");
                ProcessGames(_steamScanner.Scan());
            }
            catch (Exception ex) { DebugWindow.Log($"[Scanner] Steam scan error: {ex.Message}"); }

            try
            {
                DebugWindow.Log("[Scanner] Scanning Epic Games library...");
                ProcessGames(_epicScanner.Scan());
            }
            catch (Exception ex) { DebugWindow.Log($"[Scanner] Epic scan error: {ex.Message}"); }

            try
            {
                DebugWindow.Log("[Scanner] Scanning GOG library...");
                ProcessGames(_gogScanner.Scan());
            }
            catch (Exception ex) { DebugWindow.Log($"[Scanner] GOG scan error: {ex.Message}"); }

            try
            {
                DebugWindow.Log("[Scanner] Scanning Xbox library (MS Store)...");
                ProcessGames(_xboxScanner.Scan());
            }
            catch (Exception ex) { DebugWindow.Log($"[Scanner] Xbox scan error: {ex.Message}"); }

            try
            {
                DebugWindow.Log("[Scanner] Scanning EA App library...");
                ProcessGames(_eaScanner.Scan());
            }
            catch (Exception ex) { DebugWindow.Log($"[Scanner] EA scan error: {ex.Message}"); }

            try
            {
                DebugWindow.Log("[Scanner] Scanning Battle.net library...");
                ProcessGames(_battleNetScanner.Scan());
            }
            catch (Exception ex) { DebugWindow.Log($"[Scanner] Battle.net scan error: {ex.Message}"); }

            try
            {
                DebugWindow.Log("[Scanner] Scanning Ubisoft Connect library...");
                ProcessGames(_ubisoftScanner.Scan());
            }
            catch (Exception ex) { DebugWindow.Log($"[Scanner] Ubisoft scan error: {ex.Message}"); }

            DebugWindow.Log($"[Scanner] Scan completed. Found {games.Count} valid games.");

            return games.OrderBy(g => g.Platform).ThenBy(g => g.Name).ToList();
        });
    }
}
