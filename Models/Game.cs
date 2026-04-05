namespace OptiscalerClient.Models;

public enum GamePlatform
{
    Steam,
    Heroic,
    Manual,
    Custom
}

public class Game
{
    public string Name { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public GamePlatform Platform { get; set; }
    public bool IsManual => Platform == GamePlatform.Manual;
    public string AppId { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string? PrefixPath { get; set; }

    public string? CoverImageUrl { get; set; }

    // Detected Technologies
    public string? DlssVersion { get; set; }
    public string? DlssPath { get; set; }

    public string? DlssFrameGenVersion { get; set; }
    public string? DlssFrameGenPath { get; set; }

    public string? FsrVersion { get; set; }
    public string? FsrPath { get; set; }

    public string? XessVersion { get; set; }
    public string? XessPath { get; set; }

    public bool HasDetectedUpscaler =>
        !string.IsNullOrWhiteSpace(DlssVersion) ||
        !string.IsNullOrWhiteSpace(FsrVersion) ||
        !string.IsNullOrWhiteSpace(XessVersion);

    public string? NoUpscalerWarningText =>
        HasDetectedUpscaler ? null : "No upscaler detected. OptiScaler will most likely not work.";

    public bool IsOptiscalerInstalled { get; set; }
    public string? OptiscalerVersion { get; set; }
    public string? Fsr4ExtraVersion { get; set; }
}
