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
var submitMode = Environment.GetEnvironmentVariable("SUBMIT_MODE") ?? "api";

try
{
    Console.WriteLine();
    Console.WriteLine("Scanning your system...");

    var spec = new HardwareSpec { ApplicantName = name, ApplicantEmail = email };

    Console.WriteLine("  - Detecting OS and CPU...");
    HardwareDetector.DetectOsAndCpu(spec);

    Console.WriteLine("  - Detecting RAM and storage...");
    HardwareDetector.DetectRamAndStorage(spec);

    Console.WriteLine("  - Detecting screen resolution...");
    HardwareDetector.DetectScreenResolution(spec);

    Console.WriteLine("  - Detecting webcam and headset...");
    HardwareDetector.DetectPeripherals(spec);

    Console.WriteLine("  - Measuring internet speed (this can take 10-20 seconds)...");
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
    if (string.Equals(submitMode, "supabase", StringComparison.OrdinalIgnoreCase))
    {
        var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")
            ?? throw new InvalidOperationException("SUPABASE_URL must be set when SUBMIT_MODE=supabase");
        var supabaseAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY")
            ?? throw new InvalidOperationException("SUPABASE_ANON_KEY must be set when SUBMIT_MODE=supabase");
        var supabaseSubmitter = new SupabaseSubmitter(supabaseUrl, supabaseAnonKey);
        await supabaseSubmitter.SubmitAsync(spec, apiKey);
    }
    else
    {
        var apiClient = new ApiClient(apiBaseUrl, apiKey);
        await apiClient.SubmitAsync(spec);
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"Error: {ex.Message}");
}

if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.Write("Press any key to exit...");
    Console.ReadKey();
}

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
