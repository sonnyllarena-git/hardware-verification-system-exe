using System.Net.Http.Json;
using System.Text.Json;
using TcpHardwareCheck.Models;

namespace TcpHardwareCheck.Services;

public class ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient http = new HttpClient();
    private readonly string apiBaseUrl;
    private readonly string apiKey;

    public ApiClient(string apiBaseUrl, string apiKey)
    {
        this.apiBaseUrl = apiBaseUrl;
        this.apiKey = apiKey;
    }

    // Calls GET /submit-hardware-check/validate instead of the full submit endpoint — lets a
    // wrong key be rejected before the caller wastes ~20s on a hardware/speed scan and before
    // anything is written to submission_results.
    public async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{apiBaseUrl}/submit-hardware-check/validate");
        request.Headers.Add("X-API-Key", apiKey);

        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.IsSuccessStatusCode, body);
    }

    public async Task<bool> SubmitAsync(HardwareSpec spec)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{apiBaseUrl}/submit-hardware-check");
        request.Headers.Add("X-API-Key", apiKey);
        request.Content = JsonContent.Create(spec, options: JsonOptions);

        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Console.WriteLine(
            response.IsSuccessStatusCode
                ? $"Submitted successfully: {body}"
                : $"Submission failed ({(int)response.StatusCode}): {body}");

        return response.IsSuccessStatusCode;
    }
}
