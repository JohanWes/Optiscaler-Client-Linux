using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OptiscalerClient.Services;

public class LinuxGpuDetectionService : IGpuDetectionService
{
    private const string DrmRoot = "/sys/class/drm";
    private readonly Lazy<List<DetectedGpu>> _detectedGpus;

    public LinuxGpuDetectionService()
    {
        _detectedGpus = new Lazy<List<DetectedGpu>>(DetectGpuEntries);
    }

    public GpuInfo[] DetectGPUs() => _detectedGpus.Value.Select(gpu => gpu.Info).ToArray();

    public GpuInfo? GetPrimaryGPU()
    {
        return _detectedGpus.Value
            .OrderByDescending(gpu => gpu.IsBootVga)
            .ThenBy(gpu => gpu.CardIndex)
            .Select(gpu => gpu.Info)
            .FirstOrDefault();
    }

    public GpuInfo? GetDiscreteGPU()
    {
        var gpus = _detectedGpus.Value;

        if (gpus.Count == 0)
            return null;

        var discreteGpu = gpus
            .OrderByDescending(gpu => gpu.IsDiscreteCandidate)
            .ThenByDescending(gpu => gpu.Info.VideoMemoryBytes)
            .ThenBy(gpu => gpu.CardIndex)
            .Select(gpu => gpu.Info)
            .FirstOrDefault();

        return discreteGpu ?? GetPrimaryGPU();
    }

    public bool HasGPU(GpuVendor vendor) => _detectedGpus.Value.Any(gpu => gpu.Info.Vendor == vendor);

    public string GetGPUDescription()
    {
        var gpu = GetDiscreteGPU() ?? GetPrimaryGPU();
        return gpu?.Name ?? "Unknown GPU";
    }

    private static List<DetectedGpu> DetectGpuEntries()
    {
        var gpus = new List<DetectedGpu>();

        if (!Directory.Exists(DrmRoot))
            return gpus;

        foreach (var cardPath in Directory.EnumerateDirectories(DrmRoot, "card*"))
        {
            var cardName = Path.GetFileName(cardPath);
            if (string.IsNullOrEmpty(cardName) || !cardName.StartsWith("card", StringComparison.Ordinal))
                continue;

            if (!int.TryParse(cardName.AsSpan(4), out var cardIndex))
                continue;

            var devicePath = Path.Combine(cardPath, "device");
            if (!Directory.Exists(devicePath))
                continue;

            var vendorId = ReadTrimmedFile(Path.Combine(devicePath, "vendor"));
            var deviceId = ReadTrimmedFile(Path.Combine(devicePath, "device"));
            if (string.IsNullOrEmpty(vendorId))
                continue;

            var slotName = ReadUeventValue(Path.Combine(devicePath, "uevent"), "PCI_SLOT_NAME");
            var driverName = ReadUeventValue(Path.Combine(devicePath, "uevent"), "DRIVER");
            var vramBytes = ReadUInt64File(Path.Combine(devicePath, "mem_info_vram_total"));
            var bootVga = ReadTrimmedFile(Path.Combine(devicePath, "boot_vga")) == "1";

            var vendor = ParseVendor(vendorId);
            var name = ResolveGpuName(slotName, vendor, deviceId);
            var driverVersion = ResolveDriverVersion(driverName);

            gpus.Add(new DetectedGpu(
                cardIndex,
                bootVga,
                IsDiscreteCandidate(vendor, vramBytes, bootVga, slotName, gpus.Count == 0),
                new GpuInfo
                {
                    Name = name,
                    Vendor = vendor,
                    DriverVersion = driverVersion,
                    VideoMemoryBytes = vramBytes
                }));
        }

        return gpus;
    }

    private static bool IsDiscreteCandidate(GpuVendor vendor, ulong vramBytes, bool bootVga, string? slotName, bool isFirstGpu)
    {
        if (vendor == GpuVendor.NVIDIA)
            return true;

        if (vramBytes >= 2UL * 1024 * 1024 * 1024)
            return true;

        if (!bootVga && !string.IsNullOrEmpty(slotName))
            return true;

        return !bootVga && !isFirstGpu;
    }

    private static GpuVendor ParseVendor(string vendorId)
    {
        return vendorId.ToLowerInvariant() switch
        {
            "0x10de" => GpuVendor.NVIDIA,
            "0x1002" or "0x1022" => GpuVendor.AMD,
            "0x8086" => GpuVendor.Intel,
            _ => GpuVendor.Unknown
        };
    }

    private static string ResolveGpuName(string? slotName, GpuVendor vendor, string? deviceId)
    {
        var lspciName = TryReadGpuNameFromLspci(slotName);
        if (!string.IsNullOrWhiteSpace(lspciName))
            return lspciName;

        var vendorName = vendor switch
        {
            GpuVendor.NVIDIA => "NVIDIA",
            GpuVendor.AMD => "AMD",
            GpuVendor.Intel => "Intel",
            _ => "Unknown"
        };

        return string.IsNullOrWhiteSpace(deviceId)
            ? $"{vendorName} GPU"
            : $"{vendorName} GPU ({deviceId})";
    }

    private static string ResolveDriverVersion(string? driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName))
            return "Unknown";

        var versionPath = Path.Combine("/sys/module", driverName, "version");
        var version = ReadTrimmedFile(versionPath);
        return string.IsNullOrWhiteSpace(version) ? driverName : $"{driverName} {version}";
    }

    private static string? TryReadGpuNameFromLspci(string? slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName))
            return null;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "lspci",
                    Arguments = $"-D -s {slotName} -nn",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(1000);

            if (string.IsNullOrWhiteSpace(output) || process.ExitCode != 0)
                return null;

            var descriptionStart = output.IndexOf(": ", StringComparison.Ordinal);
            if (descriptionStart < 0)
                return null;

            var description = output[(descriptionStart + 2)..].Trim();
            return string.IsNullOrWhiteSpace(description) ? null : description;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadUeventValue(string ueventPath, string key)
    {
        if (!File.Exists(ueventPath))
            return null;

        try
        {
            foreach (var line in File.ReadLines(ueventPath))
            {
                if (line.StartsWith($"{key}=", StringComparison.Ordinal))
                    return line[(key.Length + 1)..].Trim();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? ReadTrimmedFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static ulong ReadUInt64File(string path)
    {
        var content = ReadTrimmedFile(path);
        return ulong.TryParse(content, out var value) ? value : 0;
    }

    private sealed record DetectedGpu(int CardIndex, bool IsBootVga, bool IsDiscreteCandidate, GpuInfo Info);
}
