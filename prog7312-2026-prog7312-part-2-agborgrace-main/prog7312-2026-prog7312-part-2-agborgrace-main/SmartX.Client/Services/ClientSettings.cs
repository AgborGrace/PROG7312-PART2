using System;
using System.IO;
using System.Text.Json;

namespace SmartX.Client.Services;

/// <summary>Settings read from client-settings.json (next to the .exe): Firebase Web API key + API base URL.</summary>
public class ClientSettings
{
    public string FirebaseApiKey { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "http://localhost:5177/";

    public bool IsFirebaseConfigured =>
        !string.IsNullOrWhiteSpace(FirebaseApiKey) &&
        !FirebaseApiKey.StartsWith("PASTE_", StringComparison.OrdinalIgnoreCase);

    public static ClientSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "client-settings.json");
        if (!File.Exists(path)) return new ClientSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ClientSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new ClientSettings();
        }
        catch (JsonException)
        {
            return new ClientSettings();
        }
    }
}
