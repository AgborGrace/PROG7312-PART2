using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartX.Client.Services;

public class AuthException : Exception
{
    public AuthException(string message) : base(message) { }
}

public class AuthSession
{
    public string Email { get; set; } = string.Empty;
    public string LocalId { get; set; } = string.Empty;
    public string IdToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
}

/// <summary>
/// REAL authentication with Firebase Authentication (email + password) through its REST API — no hardcoded
/// usernames or passwords anywhere. The ID token returned here is sent to the Smart-X API as a bearer token,
/// and the API verifies it with Firebase again before serving any data.
/// </summary>
public class FirebaseAuthService : ITokenProvider
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly ClientSettings _settings;

    public FirebaseAuthService(ClientSettings settings) => _settings = settings;

    public AuthSession? Session { get; private set; }
    public bool IsSignedIn => Session is not null;
    public bool IsConfigured => _settings.IsFirebaseConfigured;

    public Task<AuthSession> SignInAsync(string email, string password) =>
        AuthenticateAsync("accounts:signInWithPassword", email, password);

    public Task<AuthSession> SignUpAsync(string email, string password) =>
        AuthenticateAsync("accounts:signUp", email, password);

    public void SignOut() => Session = null;

    private void EnsureConfigured()
    {
        if (!_settings.IsFirebaseConfigured)
            throw new AuthException("Firebase is not configured. Put your Web API key in client-settings.json (see README).");
    }

    private async Task<AuthSession> AuthenticateAsync(string method, string email, string password)
    {
        EnsureConfigured();
        var url = $"https://identitytoolkit.googleapis.com/v1/{method}?key={_settings.FirebaseApiKey}";

        try
        {
            using var response = await _http.PostAsJsonAsync(url, new { email, password, returnSecureToken = true });
            var text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new AuthException(Friendly(ReadFirebaseError(text)));

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var session = new AuthSession
            {
                Email = root.TryGetProperty("email", out var e) ? e.GetString() ?? email : email,
                LocalId = root.TryGetProperty("localId", out var l) ? l.GetString() ?? string.Empty : string.Empty,
                IdToken = root.GetProperty("idToken").GetString() ?? string.Empty,
                RefreshToken = root.GetProperty("refreshToken").GetString() ?? string.Empty,
                ExpiresUtc = DateTime.UtcNow.AddSeconds(ParseSeconds(root.GetProperty("expiresIn").GetString()))
            };

            Session = session;
            return session;
        }
        catch (HttpRequestException)
        {
            throw new AuthException("Could not reach Firebase. Check your internet connection.");
        }
        catch (TaskCanceledException)
        {
            throw new AuthException("Firebase did not respond in time. Please try again.");
        }
    }

    /// <summary>Sends Firebase's "reset your password" email.</summary>
    public async Task SendPasswordResetAsync(string email)
    {
        EnsureConfigured();
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={_settings.FirebaseApiKey}";

        try
        {
            using var response = await _http.PostAsJsonAsync(url, new { requestType = "PASSWORD_RESET", email });
            if (!response.IsSuccessStatusCode)
                throw new AuthException(Friendly(ReadFirebaseError(await response.Content.ReadAsStringAsync())));
        }
        catch (HttpRequestException)
        {
            throw new AuthException("Could not reach Firebase. Check your internet connection.");
        }
    }

    /// <summary>Returns a valid ID token, refreshing it with the refresh token when it is about to expire.</summary>
    public async Task<string> GetValidIdTokenAsync()
    {
        var session = Session ?? throw new AuthException("You are not signed in.");
        if (session.ExpiresUtc - DateTime.UtcNow > TimeSpan.FromMinutes(5)) return session.IdToken;

        await _refreshLock.WaitAsync();
        try
        {
            session = Session ?? throw new AuthException("You are not signed in.");
            if (session.ExpiresUtc - DateTime.UtcNow > TimeSpan.FromMinutes(5)) return session.IdToken;

            using var form = new FormUrlEncodedContent(new[]
            {
                KeyValuePair.Create("grant_type", "refresh_token"),
                KeyValuePair.Create("refresh_token", session.RefreshToken)
            });

            using var response = await _http.PostAsync(
                $"https://securetoken.googleapis.com/v1/token?key={_settings.FirebaseApiKey}", form);

            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Session = null;
                throw new AuthException("Your session expired. Please sign in again.");
            }

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            session.IdToken = root.GetProperty("id_token").GetString() ?? session.IdToken;
            session.RefreshToken = root.GetProperty("refresh_token").GetString() ?? session.RefreshToken;
            session.ExpiresUtc = DateTime.UtcNow.AddSeconds(ParseSeconds(root.GetProperty("expires_in").GetString()));
            return session.IdToken;
        }
        catch (HttpRequestException)
        {
            throw new AuthException("Could not refresh your session — check your internet connection.");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static double ParseSeconds(string? value) =>
        double.TryParse(value, out var seconds) ? seconds : 3600;

    private static string ReadFirebaseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
                return message.GetString() ?? "UNKNOWN";
        }
        catch (JsonException)
        {
            // fall through
        }

        return "UNKNOWN";
    }

    /// <summary>Turns Firebase's error codes into messages a user can act on.</summary>
    private static string Friendly(string code)
    {
        if (code.Contains("API key not valid", StringComparison.OrdinalIgnoreCase))
            return "The Firebase API key in client-settings.json is not valid.";

        var key = code.Split(' ')[0];
        return key switch
        {
            "EMAIL_EXISTS" => "An account with this email already exists. Try signing in instead.",
            "INVALID_LOGIN_CREDENTIALS" or "INVALID_PASSWORD" or "EMAIL_NOT_FOUND" => "Incorrect email or password.",
            "USER_DISABLED" => "This account has been disabled.",
            "WEAK_PASSWORD" => "Password must be at least 6 characters.",
            "INVALID_EMAIL" => "Please enter a valid email address.",
            "MISSING_PASSWORD" => "Please enter your password.",
            "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many attempts. Please wait a few minutes and try again.",
            "OPERATION_NOT_ALLOWED" => "Email/password sign-in is not enabled in your Firebase project (Authentication > Sign-in method).",
            _ => "Authentication failed (" + key + ")."
        };
    }
}
