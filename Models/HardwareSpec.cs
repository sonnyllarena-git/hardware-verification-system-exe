namespace TcpHardwareCheck.Models;

public class HardwareSpec
{
    public string ApplicantName { get; set; } = string.Empty;

    public string ApplicantEmail { get; set; } = string.Empty;

    public string OsVersion { get; set; } = string.Empty;

    public int CpuCores { get; set; }

    public string CpuBrand { get; set; } = string.Empty;

    public string CpuModel { get; set; } = string.Empty;

    public int RamGb { get; set; }

    public int StorageGb { get; set; }

    public string StorageType { get; set; } = string.Empty;

    public string ScreenResolution { get; set; } = string.Empty;

    public double InternetSpeedDown { get; set; }

    public double InternetSpeedUp { get; set; }

    public bool WebcamPresent { get; set; }

    public bool HeadsetPresent { get; set; }
}
