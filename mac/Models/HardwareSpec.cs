namespace TcpHardwareCheck.Models;

public class HardwareSpec
{
    public string OsVersion { get; set; } = string.Empty;

    public int CpuCores { get; set; }

    public string CpuBrand { get; set; } = string.Empty;

    public string CpuModel { get; set; } = string.Empty;

    public int RamGb { get; set; }

    public int StorageGb { get; set; }

    public string StorageType { get; set; } = string.Empty;

    public string ScreenResolution { get; set; } = string.Empty;

    // Nullable: a failed speed test (network blip, Cloudflare rate limit, etc.) shouldn't block
    // the rest of the submission — see Program.cs, which leaves these null rather than aborting
    // the whole run. Matches the extension's own popup.js, which submits null on the same failure.
    public double? InternetSpeedDown { get; set; }

    public double? InternetSpeedUp { get; set; }

    public bool WebcamPresent { get; set; }

    public bool HeadsetPresent { get; set; }
}
