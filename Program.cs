using TcpHardwareCheck.Models;
using TcpHardwareCheck.Services;

LoadDotEnv();

Console.WriteLine("TCP Hardware Verification Tool");
Console.WriteLine();

Console.Write("Enter your name: ");
var name = Console.ReadLine() ?? string.Empty;

Console.Write("Enter your email: ");
var email = Console.ReadLine() ?? string.Empty;

var apiKey = Environment.GetEnvironmentVariable("API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.Write("Enter your API key: ");
    apiKey = Console.ReadLine() ?? string.Empty;
}

var apiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:3001/api";

Console.WriteLine();
Console.WriteLine("Scanning your system...");

var spec = new HardwareSpec { ApplicantName = name, ApplicantEmail = email };
HardwareDetector.DetectOsAndCpu(spec);
HardwareDetector.DetectRamAndStorage(spec);
HardwareDetector.DetectScreenResolution(spec);
HardwareDetector.DetectPeripherals(spec);

var (down, up) = await SpeedTestService.MeasureAsync();
spec.InternetSpeedDown = down;
spec.InternetSpeedUp = up;

Console.WriteLine();
Console.WriteLine($"OS:       {spec.OsVersion}");
Console.WriteLine($"CPU:      {spec.CpuBrand} {spec.CpuModel} ({spec.CpuCores} cores)");
Console.WriteLine($"RAM:      {spec.RamGb} GB");
Console.WriteLine($"Storage:  {spec.StorageGb} GB ({spec.StorageType})");
Console.WriteLine($"Screen:   {spec.ScreenResolution}");
Console.WriteLine($"Internet: {down} Mbps down / {up} Mbps up");
Console.WriteLine($"Webcam:   {spec.WebcamPresent}");
Console.WriteLine($"Headset:  {spec.HeadsetPresent}");
Console.WriteLine();

Console.WriteLine("Submitting...");
var apiClient = new ApiClient(apiBaseUrl, apiKey);
await apiClient.SubmitAsync(spec);

static void LoadDotEnv()
{
    var path = Path.Combine(AppContext.BaseDirectory, ".env");
    if (!File.Exists(path))
    {
        return;
    }

    foreach (var line in File.ReadAllLines(path))
    {
        var parts = line.Split('=', 2);
        if (parts.Length == 2 && Environment.GetEnvironmentVariable(parts[0]) is null)
        {
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}
