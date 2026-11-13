using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartX.Api.Services;

/// <summary>
/// Protects every /api endpoint. The client signs in with Firebase Authentication and sends the resulting
/// ID token as "Authorization: Bearer ...". This middleware verifies the token with Firebase itself
/// (Identity Toolkit "accounts:lookup"), so there are NO hardcoded usernames or passwords anywhere.
/// Verified tokens are cached for a few minutes so the dashboard's 1-second polling does not call Google every time.
/// </summary>
public class FirebaseAuthMiddleware
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, (string Email, DateTime ExpiresUtc)> Cache = new();

    private readonly RequestDelegate _next;
    private readonly string _apiKey;
    private readonly ILogger<FirebaseAuthMiddleware> _logger;

    public FirebaseAuthMiddleware(RequestDelegate next, IConfiguration config, ILogger<FirebaseAuthMiddleware> logger)
    {
        _next = next;
        _apiKey = config["Firebase:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only the API is protected (the root status page stays open), and CORS pre-flights pass through.
        if (!context.Request.Path.StartsWithSegments("/api") || HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.StartsWith("PASTE_", StringComparison.OrdinalIgnoreCase))
        {
            await Reject(context, StatusCodes.Status500InternalServerError,
                "Firebase is not configured on the API. Set Firebase:ApiKey in appsettings.json (see README).");
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "Sign in required: no bearer token supplied.");
            return;
        }

        var token = header["Bearer ".Length..].Trim();

        if (Cache.TryGetValue(token, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
        {
            context.Items["UserEmail"] = cached.Email;
            await _next(context);
            return;
        }

        try
        {
            using var response = await Http.PostAsJsonAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:lookup?key={_apiKey}",
                new { idToken = token });

            if (!response.IsSuccessStatusCode)
            {
                await Reject(context, StatusCodes.Status401Unauthorized, "Your session is invalid or has expired. Please sign in again.");
                return;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var user = doc.RootElement.GetProperty("users")[0];
            var email = user.TryGetProperty("email", out var e) ? e.GetString() ?? "unknown" : "unknown";

            Cache[token] = (email, DateTime.UtcNow.AddMinutes(5));
            if (Cache.Count > 500)
            {
                foreach (var kv in Cache.Where(kv => kv.Value.ExpiresUtc < DateTime.UtcNow).ToList())
                    Cache.TryRemove(kv.Key, out _);
            }

            context.Items["UserEmail"] = email;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not verify the Firebase token.");
            await Reject(context, StatusCodes.Status503ServiceUnavailable, "Could not verify your session with Firebase. Check the internet connection.");
            return;
        }

        await _next(context);
    }

    private static Task Reject(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { message });
    }
}
