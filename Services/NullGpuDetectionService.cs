namespace OptiscalerClient.Services;

public class NullGpuDetectionService : IGpuDetectionService
{
    public GpuInfo[] DetectGPUs() => [];

    public GpuInfo? GetPrimaryGPU() => null;

    public GpuInfo? GetDiscreteGPU() => null;

    public bool HasGPU(GpuVendor vendor) => false;

    public string GetGPUDescription() => "Unknown GPU";
}
