using TcpHardwareCheck.Models;
using TcpHardwareCheck.Services;

LoadDotEnv();

Console.WriteLine("TCP Hardware Verification Tool");
Console.WriteLine();

Console.Write("Are your headset and internet connection both hard-wired (not Wi-Fi/Bluetooth)? (y/n): ");
var isWired = (Console.ReadLine() ?? string.Empty).Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
if (!isWired)
{
    Console.WriteLine();
    Console.WriteLine("Warning: a wired headset and wired internet connection give the most reliable");
    Console.WriteLine("result. You can still continue, but your hardware check may fail if either one");
    Console.WriteLine("is running over Wi-Fi or Bluetooth instead.");
}

Console.WriteLine();

// The API key is the only thing that identifies who a submission belongs to — both submission
// paths (this RPC and tcp-hardware-check-api's routes/submit.js) look the applicant up purely by
// api_key and never read a name or email from the request. A name/email prompt here would just
// be decorative and, worse, misleading (it would look like it matters when it doesn't).
var apiKey = Environment.GetEnvironmentVariable("API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.Write("Enter your API key (from your applicant email): ");
    apiKey = Console.ReadLine() ?? string.Empty;
}

var apiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:3001/api";
var submitMode = Environment.GetEnvironmentVariable("SUBMIT_MODE") ?? "api";
var useSupabase = string.Equals(submitMode, "supabase", StringComparison.OrdinalIgnoreCase);

await RunAsync();

if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.Write("Press any key to exit...");
    Console.ReadKey();
}

async Task RunAsync()
{
    try
    {
        SupabaseSubmitter? supabaseSubmitter = null;
        ApiClient? apiClient = null;

        if (useSupabase)
        {
            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")
                ?? throw new InvalidOperationException("SUPABASE_URL must be set when SUBMIT_MODE=supabase");
            var supabaseAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY")
                ?? throw new InvalidOperationException("SUPABASE_ANON_KEY must be set when SUBMIT_MODE=supabase");
            supabaseSubmitter = new SupabaseSubmitter(supabaseUrl, supabaseAnonKey);
        }
        else
        {
            apiClient = new ApiClient(apiBaseUrl, apiKey);
        }

        // Check the key before running anything — a wrong key shouldn't cost the applicant a
        // ~20s hardware/speed scan just to find out at the very end that none of it could be
        // submitted.
        Console.WriteLine();
        Console.WriteLine("Checking your API key...");
        var (isValid, validationMessage) = useSupabase
            ? await supabaseSubmitter!.ValidateApiKeyAsync(apiKey)
            : await apiClient!.ValidateApiKeyAsync();
        if (!isValid)
        {
            Console.WriteLine($"API key rejected: {validationMessage}");
            return;
        }

        Console.WriteLine("Scanning your system...");

        var spec = new HardwareSpec();

        Console.WriteLine("  - Detecting OS and CPU...");
        HardwareDetector.DetectOsAndCpu(spec);

        Console.WriteLine("  - Detecting RAM and storage...");
        HardwareDetector.DetectRamAndStorage(spec);

        Console.WriteLine("  - Detecting screen resolution...");
        HardwareDetector.DetectScreenResolution(spec);

        Console.WriteLine("  - Detecting webcam and headset...");
        HardwareDetector.DetectPeripherals(spec);

        Console.WriteLine("  - Measuring internet speed (this can take 10-20 seconds)...");
        try
        {
            var (down, up) = await SpeedTestService.MeasureAsync();
            spec.InternetSpeedDown = down;
            spec.InternetSpeedUp = up;
        }
        catch (Exception ex)
        {
            // A network blip or Cloudflare rate limit here shouldn't cost the applicant the rest
            // of an already-completed hardware scan — leave the speed fields null (same as a
            // failed measurement in the extension's popup.js) and still submit everything else
            // collected.
            Console.WriteLine($"  (couldn't measure internet speed: {ex.Message} — continuing anyway)");
        }

        Console.WriteLine();
        Console.WriteLine($"OS:       {spec.OsVersion}");
        Console.WriteLine($"CPU:      {spec.CpuBrand} {spec.CpuModel} ({spec.CpuCores} cores)");
        Console.WriteLine($"RAM:      {spec.RamGb} GB");
        Console.WriteLine($"Storage:  {spec.StorageGb} GB ({spec.StorageType})");
        Console.WriteLine($"Screen:   {spec.ScreenResolution}");
        Console.WriteLine(
            $"Internet: {spec.InternetSpeedDown?.ToString() ?? "Unavailable"} Mbps down / "
            + $"{spec.InternetSpeedUp?.ToString() ?? "Unavailable"} Mbps up");
        Console.WriteLine($"Webcam:   {spec.WebcamPresent}");
        Console.WriteLine($"Headset:  {spec.HeadsetPresent}");
        Console.WriteLine();

        Console.WriteLine("Submitting...");
        var submitted = useSupabase
            ? await supabaseSubmitter!.SubmitAsync(spec, apiKey)
            : await apiClient!.SubmitAsync(spec);

        Console.WriteLine();
        Console.WriteLine(
            submitted
                ? "Result has been submitted. HR will review."
                : "Something went wrong submitting your result. Please take a screenshot of this "
                    + "window (including any error text above) and send it to HR.");
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"Error: {ex.Message}");
        Console.WriteLine(
            "Please take a screenshot of this window and send it to HR so they can look into it.");
    }
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
