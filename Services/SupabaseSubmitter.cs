using System.Net.Http.Json;
using TcpHardwareCheck.Models;

namespace TcpHardwareCheck.Services;

public class SupabaseSubmitter
{
    private readonly HttpClient http = new HttpClient();
    private readonly string supabaseUrl;
    private readonly string anonKey;

    public SupabaseSubmitter(string supabaseUrl, string anonKey)
    {
        this.supabaseUrl = supabaseUrl.TrimEnd('/');
        this.anonKey = anonKey;
    }

    public async Task<bool> SubmitAsync(HardwareSpec spec, string apiKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{supabaseUrl}/rest/v1/rpc/submit_hardware_check_direct");
        request.Headers.Add("apikey", anonKey);
        request.Headers.Add("Authorization", $"Bearer {anonKey}");
        request.Content = JsonContent.Create(new
        {
            p_api_key = apiKey,
            p_os_version = spec.OsVersion,
            p_cpu_cores = spec.CpuCores,
            p_cpu_brand = spec.CpuBrand,
            p_cpu_model = spec.CpuModel,
            p_ram_gb = spec.RamGb,
            p_storage_gb = spec.StorageGb,
            p_storage_type = spec.StorageType,
            p_screen_resolution = spec.ScreenResolution,
            p_internet_speed_down = spec.InternetSpeedDown,
            p_internet_speed_up = spec.InternetSpeedUp,
            p_webcam_present = spec.WebcamPresent,
            p_headset_present = spec.HeadsetPresent,
        });

        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Console.WriteLine(
            response.IsSuccessStatusCode
                ? $"Submitted successfully: {body}"
                : $"Submission failed ({(int)response.StatusCode}): {body}");

        return response.IsSuccessStatusCode;
    }
}
