using System.Management;
using System.Runtime.InteropServices;
using TcpHardwareCheck.Models;

namespace TcpHardwareCheck.Services;

public static class HardwareDetector
{
    private const int CxScreen = 0;
    private const int CyScreen = 1;

    public static void DetectScreenResolution(HardwareSpec spec)
    {
        spec.ScreenResolution = GetPhysicalScreenResolution();
    }

    public static void DetectOsAndCpu(HardwareSpec spec)
    {
        var build = Environment.OSVersion.Version.Build;
        spec.OsVersion = build >= 22000 ? "Windows 11" : "Windows 10";

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, Manufacturer, NumberOfCores FROM Win32_Processor");
        foreach (ManagementObject processor in searcher.Get())
        {
            spec.CpuModel = processor["Name"]?.ToString()?.Trim() ?? string.Empty;
            spec.CpuCores += Convert.ToInt32(processor["NumberOfCores"]);

            var manufacturer = processor["Manufacturer"]?.ToString() ?? string.Empty;
            spec.CpuBrand = manufacturer switch
            {
                "GenuineIntel" => "Intel",
                "AuthenticAMD" => "AMD",
                _ => manufacturer,
            };
        }
    }

    public static void DetectRamAndStorage(HardwareSpec spec)
    {
        using var ramSearcher = new ManagementObjectSearcher(
            "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
        foreach (ManagementObject system in ramSearcher.Get())
        {
            var bytes = Convert.ToInt64(system["TotalPhysicalMemory"]);
            spec.RamGb = (int)Math.Round(bytes / 1024.0 / 1024.0 / 1024.0);
        }

        // Win32_DiskDrive.MediaType reports "Fixed hard disk media" for both SSDs and HDDs on
        // many systems — MSFT_PhysicalDisk (same source Get-PhysicalDisk uses) distinguishes
        // them via a real enum: 3 = HDD, 4 = SSD.
        var storageScope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
        using var diskSearcher = new ManagementObjectSearcher(
            storageScope,
            new ObjectQuery("SELECT Size, MediaType FROM MSFT_PhysicalDisk"));
        var hasSsd = false;
        foreach (ManagementObject disk in diskSearcher.Get())
        {
            var bytes = Convert.ToInt64(disk["Size"]);
            spec.StorageGb += (int)Math.Round(bytes / 1024.0 / 1024.0 / 1024.0);
            hasSsd = hasSsd || Convert.ToInt32(disk["MediaType"]) == 4;
        }

        spec.StorageType = hasSsd ? "SSD" : "HDD";
    }

    public static void DetectPeripherals(HardwareSpec spec)
    {
        // {ca3e7ab9-b4c3-4ae6-8251-579ef933890f} is the modern Windows Camera device class.
        using var cameraSearcher = new ManagementObjectSearcher(
            "SELECT Name FROM Win32_PnPEntity " +
            "WHERE ClassGuid = \"{ca3e7ab9-b4c3-4ae6-8251-579ef933890f}\"");
        spec.WebcamPresent = cameraSearcher.Get().Count > 0;

        // "Headset or speakers required" (db-schema.sql) — any working sound output counts.
        using var audioSearcher = new ManagementObjectSearcher(
            "SELECT Name FROM Win32_SoundDevice WHERE Status = \"OK\"");
        spec.HeadsetPresent = audioSearcher.Get().Count > 0;
    }

    // GetSystemMetrics reports DPI-virtualized logical pixels for a non-DPI-aware process
    // (e.g. 1536x864 for a 1920x1080 display at 125% scaling), so the physical resolution is
    // read from Win32_VideoController instead; GetSystemMetrics is only the fallback, since
    // that WMI class's resolution fields are null on some drivers/VMs.
    private static string GetPhysicalScreenResolution()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");
            foreach (ManagementObject controller in searcher.Get())
            {
                var width = controller["CurrentHorizontalResolution"]?.ToString();
                var height = controller["CurrentVerticalResolution"]?.ToString();
                if (!string.IsNullOrEmpty(width) && width != "0" &&
                    !string.IsNullOrEmpty(height) && height != "0")
                {
                    return $"{width}x{height}";
                }
            }
        }
        catch (ManagementException)
        {
            // Fall through to GetSystemMetrics below.
        }

        return $"{GetSystemMetrics(CxScreen)}x{GetSystemMetrics(CyScreen)}";
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
