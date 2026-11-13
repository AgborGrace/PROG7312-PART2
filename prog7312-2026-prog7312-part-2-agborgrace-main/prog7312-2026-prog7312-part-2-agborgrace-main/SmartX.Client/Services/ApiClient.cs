using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SmartX.Client.Models;

namespace SmartX.Client.Services;

/// <summary>Result wrapper so the UI can show a friendly error instead of catching exceptions everywhere.</summary>
public class ApiResponse<T>
{
    public bool IsSuccess { get; init; }
    public T? Data { get; init; }
    public string Error { get; init; } = string.Empty;
    public int StatusCode { get; init; }

    public static ApiResponse<T> Ok(T? data, int statusCode) => new() { IsSuccess = true, Data = data, StatusCode = statusCode };
    public static ApiResponse<T> Fail(string error, int statusCode = 0) => new() { IsSuccess = false, Error = error, StatusCode = statusCode };
}

/// <summary>
/// Every call to the Smart-X API goes through here. All methods are async (the UI never freezes) and each request
/// carries the user's Firebase ID token as a bearer token.
/// </summary>
public class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    private readonly ITokenProvider _tokens;

    public ApiClient(ClientSettings settings, ITokenProvider tokens)
    {
        _tokens = tokens;
        var baseUrl = settings.ApiBaseUrl.EndsWith('/') ? settings.ApiBaseUrl : settings.ApiBaseUrl + "/";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
    }

    // ------------------------------------------------------------------ core request helper

    private async Task<ApiResponse<T>> SendAsync<T>(HttpMethod method, string path, object? body = null, HttpContent? content = null)
    {
        try
        {
            var token = await _tokens.GetValidIdTokenAsync();

            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (content is not null) request.Content = content;
            else if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), null, Json);

            using var response = await _http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                T? data = string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, Json);
                return ApiResponse<T>.Ok(data, (int)response.StatusCode);
            }

            return ApiResponse<T>.Fail(ExtractMessage(text) ?? response.ReasonPhrase ?? "Request failed.", (int)response.StatusCode);
        }
        catch (AuthException ex)
        {
            return ApiResponse<T>.Fail(ex.Message, 401);
        }
        catch (HttpRequestException)
        {
            return ApiResponse<T>.Fail("Could not reach the Smart-X API. Is SmartX.Api running?");
        }
        catch (TaskCanceledException)
        {
            return ApiResponse<T>.Fail("The API did not respond in time.");
        }
        catch (JsonException ex)
        {
            return ApiResponse<T>.Fail("The API returned an unexpected response: " + ex.Message);
        }
    }

    private static string? ExtractMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) return m.GetString();
                if (doc.RootElement.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();
            }
        }
        catch (JsonException)
        {
            // not JSON: fall through
        }

        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Length > 200 ? text[..200] : text;
    }

    // ------------------------------------------------------------------ endpoints

    /// <summary>Is the API reachable? (The root status page needs no sign-in.)</summary>
    public async Task<bool> PingAsync()
    {
        try
        {
            using var response = await _http.GetAsync(string.Empty);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    public Task<ApiResponse<List<DeviceDto>>> GetDevicesAsync() =>
        SendAsync<List<DeviceDto>>(HttpMethod.Get, "api/devices");

    public Task<ApiResponse<StreamSnapshotDto>> GetSnapshotAsync() =>
        SendAsync<StreamSnapshotDto>(HttpMethod.Get, "api/stream/snapshot");

    public Task<ApiResponse<DeviceDto>> RegisterDeviceAsync(RegistrationDto registration) =>
        SendAsync<DeviceDto>(HttpMethod.Post, "api/sensors/register", registration);

    /// <summary>Uploads a .txt device list; the API reads it, registers the devices and returns a summary.</summary>
    public async Task<ApiResponse<ImportResultDto>> ImportDevicesAsync(string filePath)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", Path.GetFileName(filePath) } };
            return await SendAsync<ImportResultDto>(HttpMethod.Post, "api/devices/import", content: form);
        }
        catch (IOException ex)
        {
            return ApiResponse<ImportResultDto>.Fail("Could not read the file: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiResponse<ImportResultDto>.Fail("Could not read the file: " + ex.Message);
        }
    }

    /// <summary>Attaches a configuration file, log file or deployment photo to a device.</summary>
    public async Task<ApiResponse<JsonElement>> UploadAttachmentAsync(string mac, string filePath)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", Path.GetFileName(filePath) } };
            return await SendAsync<JsonElement>(HttpMethod.Post, $"api/sensors/{Uri.EscapeDataString(mac)}/upload", content: form);
        }
        catch (IOException ex)
        {
            return ApiResponse<JsonElement>.Fail("Could not read the file: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiResponse<JsonElement>.Fail("Could not read the file: " + ex.Message);
        }
    }

    /// <summary>Sends one telemetry packet exactly like a real device would.</summary>
    public Task<ApiResponse<IngestResultDto>> IngestAsync(string mac, double value, string unit) =>
        SendAsync<IngestResultDto>(HttpMethod.Post, "api/telemetry/ingest",
            new IngestRequestDto { DeviceMac = mac, Value = value, Unit = unit });

    public Task<ApiResponse<DeviceCommandDto>> SendCommandAsync(string mac, CommandAction action) =>
        SendAsync<DeviceCommandDto>(HttpMethod.Post, "api/commands", new { mac, action });

    public Task<ApiResponse<UndoResultDto>> UndoAsync() =>
        SendAsync<UndoResultDto>(HttpMethod.Post, "api/commands/undo");

    public Task<ApiResponse<DisconnectReportDto>> ReportDisconnectAsync(string mac) =>
        SendAsync<DisconnectReportDto>(HttpMethod.Post, $"api/alerts/disconnect/{Uri.EscapeDataString(mac)}");

    /// <summary>Records a submitted search so the predictive engine can learn from it.</summary>
    public Task<ApiResponse<JsonElement>> RecordSearchAsync(string term) =>
        SendAsync<JsonElement>(HttpMethod.Post, "api/insights/search", new { term });
}
