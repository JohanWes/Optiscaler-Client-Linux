using System;
using OptiscalerClient.Models;

namespace OptiscalerClient.Helpers;

public static class SteamCompatibilityToolDetector
{
    public static bool IsCompatibilityTool(string? name, string? installDir = null)
    {
        return IsCompatibilityName(name) || IsCompatibilityName(installDir);
    }

    public static bool IsCompatibilityTool(Game game)
    {
        return game.Platform == GamePlatform.Steam &&
               IsCompatibilityTool(game.Name, game.InstallPath);
    }

    private static bool IsCompatibilityName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("GE-Proton", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("GE Proton", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("pressure-vessel", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Bottles", StringComparison.OrdinalIgnoreCase);
    }
}
