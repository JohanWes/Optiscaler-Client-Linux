using System.Text.Json;
using OptiscalerClient.Models;
using OptiscalerClient.Services;

namespace OptiscalerClient.IntegrationTests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            using var fixture = new IntegrationFixture();

            await RunScanAndInstallWorkflowAsync(fixture);

            Console.WriteLine("Integration tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static async Task RunScanAndInstallWorkflowAsync(IntegrationFixture fixture)
    {
        var scannerService = new GameScannerService(
            new SteamScanner(fixture.SteamRoot),
            new HeroicScanner(fixture.HeroicRoot),
            fixture.ConfigPath);

        var scanResults = await scannerService.ScanAllGamesAsync(new ScanSourcesConfig
        {
            ScanSteam = true,
            ScanHeroic = true
        });

        Assert.Equal(2, scanResults.Count, "Expected Steam and Heroic fixtures to be discovered.");

        var steamGame = scanResults.SingleOrDefault(game => game.Platform == GamePlatform.Steam && game.AppId == fixture.SteamAppId);
        var heroicGame = scanResults.SingleOrDefault(game => game.Platform == GamePlatform.Heroic && game.AppId == fixture.HeroicAppId);

        Assert.NotNull(steamGame, "Steam fixture game was not discovered.");
        Assert.NotNull(heroicGame, "Heroic fixture game was not discovered.");

        Assert.Equal(fixture.SteamGameExecutable, steamGame!.ExecutablePath, "Steam executable path did not match the sandbox fixture.");
        Assert.Equal(fixture.SteamPrefixPath, steamGame.PrefixPath, "Steam prefix path did not match the sandbox fixture.");
        Assert.Equal(fixture.HeroicGameExecutable, heroicGame!.ExecutablePath, "Heroic executable path did not match the sandbox fixture.");
        Assert.Equal(fixture.HeroicPrefixPath, heroicGame.PrefixPath, "Heroic prefix path did not match the sandbox fixture.");

        var installer = new GameInstallationService();

        installer.InstallOptiScaler(steamGame, fixture.OptiScalerCachePath, injectionDllName: "dxgi.dll", optiscalerVersion: "test-steam");
        VerifyInstallOutcome(
            fixture.SteamInstallDirectory,
            "dxgi.dll",
            fixture.OriginalSteamDxgiContents,
            "test-steam",
            expectBackup: true);

        installer.UninstallOptiScaler(steamGame);
        Assert.Equal(fixture.OriginalSteamDxgiContents, File.ReadAllText(Path.Combine(fixture.SteamInstallDirectory, "dxgi.dll")), "Steam uninstall should restore the original dxgi.dll.");
        Assert.False(Directory.Exists(Path.Combine(fixture.SteamInstallDirectory, "OptiScalerBackup")), "Steam uninstall should remove the backup directory.");

        installer.InstallOptiScaler(heroicGame, fixture.OptiScalerCachePath, injectionDllName: "version.dll", optiscalerVersion: "test-heroic");
        VerifyInstallOutcome(
            fixture.HeroicInstallDirectory,
            "version.dll",
            originalDllContents: null,
            "test-heroic",
            expectBackup: false);
    }

    private static void VerifyInstallOutcome(string installDir, string injectionDllName, string? originalDllContents, string expectedVersion, bool expectBackup)
    {
        var injectionDllPath = Path.Combine(installDir, injectionDllName);
        var backupDir = Path.Combine(installDir, "OptiScalerBackup");
        var manifestPath = Path.Combine(backupDir, "optiscaler_manifest.json");
        var backupDllPath = Path.Combine(backupDir, injectionDllName);

        Assert.True(File.Exists(injectionDllPath), $"Expected {injectionDllName} to be installed.");
        Assert.Equal("fixture-optiscaler-main", File.ReadAllText(injectionDllPath), "Installed injection DLL content did not come from the OptiScaler cache.");
        Assert.True(File.Exists(Path.Combine(installDir, "fakenvapi.dll")), "Expected fakenvapi.dll to be installed.");
        Assert.True(File.Exists(Path.Combine(installDir, "nvapi64.dll")), "Expected nvapi64.dll alias to be installed.");
        Assert.True(File.Exists(Path.Combine(installDir, "nested", "support.txt")), "Expected nested support file to be copied.");

        var optiScalerIni = File.ReadAllText(Path.Combine(installDir, "OptiScaler.ini"));
        Assert.Contains("OverrideNvapiDll=true", optiScalerIni, "OptiScaler.ini should enable OverrideNvapiDll when fakenvapi is packaged.");

        Assert.True(File.Exists(manifestPath), "Expected installation manifest to be written.");

        using (var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath)))
        {
            var root = manifestDocument.RootElement;
            Assert.Equal(expectedVersion, root.GetProperty("OptiscalerVersion").GetString(), "Manifest should record the installed OptiScaler version.");
            Assert.Equal(injectionDllName, root.GetProperty("InjectionMethod").GetString(), "Manifest should record the selected injection DLL.");

            var installedFiles = root.GetProperty("InstalledFiles").EnumerateArray().Select(entry => entry.GetString()).Where(entry => entry != null).ToList();
            Assert.Contains(injectionDllName, installedFiles!, "Manifest should include the injection DLL.");
            Assert.Contains("fakenvapi.dll", installedFiles!, "Manifest should include fakenvapi.dll.");
            Assert.Contains("nvapi64.dll", installedFiles!, "Manifest should include the nvapi64.dll alias.");
        }

        if (expectBackup)
        {
            Assert.True(File.Exists(backupDllPath), $"Expected backup for {injectionDllName} to exist.");
            Assert.Equal(originalDllContents, File.ReadAllText(backupDllPath), $"Backup for {injectionDllName} did not preserve the original contents.");
        }
        else
        {
            Assert.False(File.Exists(backupDllPath), $"Did not expect a backup for {injectionDllName}.");
        }
    }

    private sealed class IntegrationFixture : IDisposable
    {
        private readonly string _rootPath;

        public IntegrationFixture()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), $"optiscaler-client-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootPath);

            var homePath = Path.Combine(_rootPath, "home");
            Directory.CreateDirectory(homePath);

            ConfigPath = Path.Combine(_rootPath, "config.json");
            File.WriteAllText(ConfigPath, "{ \"ScanExclusions\": [] }");

            SteamRoot = Path.Combine(homePath, ".local", "share", "Steam");
            SteamAppId = "424242";
            SteamPrefixPath = Path.Combine(SteamRoot, "steamapps", "compatdata", SteamAppId, "pfx");
            SteamInstallDirectory = Path.Combine(SteamRoot, "steamapps", "common", "FixtureSteamGame", "Binaries", "Win64");
            SteamGameExecutable = Path.Combine(SteamInstallDirectory, "FixtureSteamGame.exe");
            OriginalSteamDxgiContents = "original-steam-dxgi";

            HeroicRoot = Path.Combine(homePath, ".config", "heroic");
            HeroicAppId = "fixture-heroic-app";
            HeroicPrefixPath = Path.Combine(homePath, "Games", "Heroic", "Prefixes", "default", "FixtureHeroicGame");
            HeroicInstallDirectory = Path.Combine(HeroicPrefixPath, "drive_c", "Program Files", "Fixture Heroic Game");
            HeroicGameExecutable = Path.Combine(HeroicInstallDirectory, "FixtureHeroicGame.exe");

            OptiScalerCachePath = Path.Combine(_rootPath, "optiscaler-cache");

            CreateSteamFixture();
            CreateHeroicFixture();
            CreateOptiScalerCacheFixture();
        }

        public string ConfigPath { get; }
        public string SteamRoot { get; }
        public string SteamAppId { get; }
        public string SteamPrefixPath { get; }
        public string SteamInstallDirectory { get; }
        public string SteamGameExecutable { get; }
        public string OriginalSteamDxgiContents { get; }
        public string HeroicRoot { get; }
        public string HeroicAppId { get; }
        public string HeroicPrefixPath { get; }
        public string HeroicInstallDirectory { get; }
        public string HeroicGameExecutable { get; }
        public string OptiScalerCachePath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_rootPath))
                    Directory.Delete(_rootPath, true);
            }
            catch
            {
            }
        }

        private void CreateSteamFixture()
        {
            Directory.CreateDirectory(Path.Combine(SteamRoot, "steamapps"));
            Directory.CreateDirectory(SteamPrefixPath);
            Directory.CreateDirectory(SteamInstallDirectory);

            var libraryFoldersPath = Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf");
            var appManifestPath = Path.Combine(SteamRoot, "steamapps", $"appmanifest_{SteamAppId}.acf");

            File.WriteAllText(libraryFoldersPath, $$"""
                "libraryfolders"
                {
                    "0"
                    {
                        "path" "{{SteamRoot}}"
                    }
                }
                """);

            File.WriteAllText(appManifestPath, $$"""
                "AppState"
                {
                    "appid" "{{SteamAppId}}"
                    "name" "Fixture Steam Game"
                    "installdir" "FixtureSteamGame"
                }
                """);

            CreateSizedBinary(SteamGameExecutable, 6 * 1024 * 1024);
            CreateSizedBinary(Path.Combine(SteamInstallDirectory, "FixtureSteamGameLauncher.exe"), 6 * 1024 * 1024);
            File.WriteAllText(Path.Combine(SteamInstallDirectory, "dxgi.dll"), OriginalSteamDxgiContents);
            File.WriteAllText(Path.Combine(SteamInstallDirectory, "nvngx_dlss.dll"), "steam-dlss");
        }

        private void CreateHeroicFixture()
        {
            var gamesConfigPath = Path.Combine(HeroicRoot, "GamesConfig");
            var sideloadAppsPath = Path.Combine(HeroicRoot, "sideload_apps");
            var storePath = Path.Combine(HeroicRoot, "store");

            Directory.CreateDirectory(gamesConfigPath);
            Directory.CreateDirectory(sideloadAppsPath);
            Directory.CreateDirectory(storePath);
            Directory.CreateDirectory(HeroicInstallDirectory);

            File.WriteAllText(Path.Combine(HeroicRoot, "GamesConfig", $"{HeroicAppId}.json"), $$"""
                {
                  "{{HeroicAppId}}": {
                    "winePrefix": "{{HeroicPrefixPath}}"
                  },
                  "version": "v0",
                  "explicit": true
                }
                """);

            File.WriteAllText(Path.Combine(sideloadAppsPath, "library.json"), $$"""
                {
                  "games": [
                    {
                      "runner": "sideload",
                      "app_name": "{{HeroicAppId}}",
                      "title": "Fixture Heroic Game",
                      "install": {
                        "executable": "{{HeroicGameExecutable}}",
                        "platform": "Windows"
                      },
                      "folder_name": "{{HeroicInstallDirectory}}",
                      "is_installed": true
                    }
                  ]
                }
                """);

            File.WriteAllText(Path.Combine(storePath, "config.json"), $$"""
                {
                  "settings": {
                    "defaultInstallPath": "{{Path.GetDirectoryName(HeroicInstallDirectory)}}",
                    "defaultWinePrefix": "{{Path.GetDirectoryName(HeroicPrefixPath)}}"
                  }
                }
                """);

            CreateSizedBinary(HeroicGameExecutable, 6 * 1024 * 1024);
            CreateSizedBinary(Path.Combine(HeroicInstallDirectory, "HeroicLauncher.exe"), 6 * 1024 * 1024);
            File.WriteAllText(Path.Combine(HeroicInstallDirectory, "nvngx_dlss.dll"), "heroic-dlss");
        }

        private void CreateOptiScalerCacheFixture()
        {
            Directory.CreateDirectory(OptiScalerCachePath);
            Directory.CreateDirectory(Path.Combine(OptiScalerCachePath, "nested"));

            File.WriteAllText(Path.Combine(OptiScalerCachePath, "OptiScaler.dll"), "fixture-optiscaler-main");
            File.WriteAllText(Path.Combine(OptiScalerCachePath, "OptiScaler.ini"), "[NvApi]\nOverrideNvapiDll=false\n");
            File.WriteAllText(Path.Combine(OptiScalerCachePath, "fakenvapi.dll"), "fixture-fakenvapi");
            File.WriteAllText(Path.Combine(OptiScalerCachePath, "nested", "support.txt"), "fixture-support");
        }

        private static void CreateSizedBinary(string path, int sizeBytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var stream = File.Create(path);
            stream.SetLength(sizeBytes);
        }
    }

    private static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        public static void False(bool condition, string message)
        {
            if (condition)
                throw new InvalidOperationException(message);
        }

        public static void Equal(string? expected, string? actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
        }

        public static void Equal(int expected, int actual, string message)
        {
            if (expected != actual)
                throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
        }

        public static void NotNull<T>(T? value, string message) where T : class
        {
            if (value == null)
                throw new InvalidOperationException(message);
        }

        public static void Contains(string expected, string actual, string message)
        {
            if (!actual.Contains(expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"{message} Missing '{expected}'.");
        }

        public static void Contains(string expected, IEnumerable<string> values, string message)
        {
            if (!values.Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"{message} Missing '{expected}'.");
        }
    }
}
