using System.Diagnostics;
using System.Text.RegularExpressions;
using TcpHardwareCheck.Models;

namespace TcpHardwareCheck.Services;

// macOS has no WMI equivalent, so unlike the Windows exe (System.Management), this shells out to
// stable, well-documented command-line tools and parses their text output: sw_vers, sysctl,
// diskutil, and system_profiler. NOT yet verified against a real Mac (built on a Windows dev
// machine) — the exact text formats below are based on documented/widely-scripted-against output
// shapes, but should be treated as a first draft to validate on real hardware, the same way the
// Windows detector needed several rounds of real-machine fixes (see ../tcp-hardware-check-exe's
// own LESSONS.md history) before it was trustworthy.
public static class HardwareDetector
{
    public static void DetectOsAndCpu(HardwareSpec spec)
    {
        // sw_vers -productVersion prints just the version number (e.g. "14.6.1"), matching the
        // "macOS <version>" format the compliance RPC/API already expects from the extension's
        // own getOSLabel() — both check the major version number the same way regardless of
        // which client produced it.
        var version = RunCommand("sw_vers", "-productVersion").Trim();
        spec.OsVersion = string.IsNullOrEmpty(version) ? "macOS" : $"macOS {version}";

        spec.CpuCores = int.TryParse(RunCommand("sysctl", "-n hw.ncpu").Trim(), out var cores) ? cores : 0;

        // machdep.cpu.brand_string works for both Intel Macs ("Intel(R) Core(TM) i7-9750H CPU @
        // 2.60GHz") and Apple Silicon ("Apple M2 Pro") — Apple added sysctl compatibility shims
        // for this exact key when Apple Silicon shipped, specifically so existing scripts that
        // read it wouldn't break.
        var brandString = RunCommand("sysctl", "-n machdep.cpu.brand_string").Trim();
        spec.CpuModel = brandString;
        spec.CpuBrand = brandString.Contains("Apple", StringComparison.OrdinalIgnoreCase)
            ? "Apple"
            : brandString.Contains("Intel", StringComparison.OrdinalIgnoreCase)
                ? "Intel"
                : brandString;

        // Compliance only gates Mac CPUs by OS major version, not chip family (see
        // direct-submit-rpc.sql's 'cpu' case) — CpuBrand/CpuModel here are informational only,
        // same as on Windows.
    }

    public static void DetectRamAndStorage(HardwareSpec spec)
    {
        var memBytes = RunCommand("sysctl", "-n hw.memsize").Trim();
        if (long.TryParse(memBytes, out var bytes))
        {
            spec.RamGb = (int)Math.Round(bytes / 1024.0 / 1024.0 / 1024.0);
        }

        // diskutil info / reports the boot volume specifically — the internal drive that matters
        // for compliance, same scope as the Windows detector's "fixed drives only" filter
        // (external/removable drives excluded there too). Unlike Windows, this doesn't sum
        // multiple physical drives: virtually every consumer Mac has exactly one internal drive,
        // so enumerating physical disks the way MSFT_PhysicalDisk does on Windows would add
        // real complexity for a case that's vanishingly rare in practice.
        var diskInfo = RunCommand("diskutil", "info /");

        var sizeMatch = Regex.Match(diskInfo, @"Volume Total Space:.*\(([\d,]+)\s*Bytes\)");
        if (sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value.Replace(",", string.Empty), out var volumeBytes))
        {
            spec.StorageGb = (int)Math.Round(volumeBytes / 1024.0 / 1024.0 / 1024.0);
        }

        var ssdMatch = Regex.Match(diskInfo, @"Solid State:\s*(Yes|No)", RegexOptions.IgnoreCase);
        spec.StorageType = ssdMatch.Success && string.Equals(ssdMatch.Groups[1].Value, "Yes", StringComparison.OrdinalIgnoreCase)
            ? "SSD"
            : "HDD";
    }

    public static void DetectScreenResolution(HardwareSpec spec)
    {
        // SPDisplaysDataType's plain-text "Resolution: WIDTH x HEIGHT" line has been a stable,
        // widely-scripted-against format for years — safer to regex than to guess at the JSON
        // variant's exact key names (system_profiler's JSON schema for displays has shifted
        // field names across macOS versions in ways the text output hasn't).
        var displaysInfo = RunCommand("system_profiler", "SPDisplaysDataType");
        var matches = Regex.Matches(displaysInfo, @"Resolution:\s*(\d+)\s*x\s*(\d+)");

        spec.ScreenResolution = matches.Count > 0
            ? string.Join(", ", matches.Select(m => $"{m.Groups[1].Value}x{m.Groups[2].Value}"))
            : "Unknown";
    }

    public static void DetectPeripherals(HardwareSpec spec)
    {
        // SPCameraDataType prints per-device blocks (each with a "Model ID:"/"Unique ID:" line)
        // when a camera exists, and just the bare "Camera:" header with nothing else when it
        // doesn't — checking for either of those per-device lines distinguishes the two cases
        // without depending on exact device-name text.
        var cameraInfo = RunCommand("system_profiler", "SPCameraDataType");
        spec.WebcamPresent = cameraInfo.Contains("Model ID:") || cameraInfo.Contains("Unique ID:");

        // "Headset or speakers required" (db-schema.sql) — any working audio output counts, same
        // leniency as the Windows detector's "any working Win32_SoundDevice" check (neither
        // actually verifies it's specifically a headset). Every Mac has at least built-in
        // speakers, so this mirrors that same real-world leniency rather than introducing a
        // stricter check Windows doesn't have.
        var audioInfo = RunCommand("system_profiler", "SPAudioDataType");
        spec.HeadsetPresent = audioInfo.Contains("Manufacturer:");
    }

    private static string RunCommand(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
        catch (Exception)
        {
            // A missing/failed command shouldn't crash the whole scan — the caller's field just
            // stays at its default (0, empty string, etc.), same graceful-degradation approach
            // used throughout this tool for a failed speed test.
            return string.Empty;
        }
    }
}
