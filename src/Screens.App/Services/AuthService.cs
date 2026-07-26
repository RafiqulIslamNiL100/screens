using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Screens.App.Models;

namespace Screens.App.Services;

public sealed class AuthResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Session? Session { get; init; }

    public static AuthResult Ok(Session session) => new() { Success = true, Session = session };
    public static AuthResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Thin wrapper over Supabase GoTrue (auth) REST endpoints. See DECISIONS.md
/// for why we use HttpClient directly instead of the supabase-csharp SDK.
/// </summary>
public sealed class AuthService
{
    private readonly HttpClient _http;
    private readonly AppConfig _config;
    private readonly SettingsService _settings;

    public Session? CurrentSession { get; private set; }

    public AuthService(AppConfig config, SettingsService settings)
    {
        _config = config;
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (_config.IsSupabaseConfigured)
        {
            _http.BaseAddress = new Uri(_config.SupabaseUrl.TrimEnd('/') + "/auth/v1/");
            _http.DefaultRequestHeaders.Add("apikey", _config.SupabaseAnonKey);
            // Supabase's gateway rejects requests with only `apikey` and no
            // Authorization header (401, generic body) — for unauthenticated
            // calls (signup/signin/refresh) that Authorization is just the
            // anon key. ParseAuthResponse overwrites this with the real
            // session token once one exists.
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.SupabaseAnonKey);
        }
    }

    public async Task<AuthResult> SignUpAsync(string email, string password)
    {
        if (!_config.IsSupabaseConfigured)
            return AuthResult.Fail("Screens is not connected to a Supabase project yet. See docs/SETUP.md.");

        var response = await _http.PostAsJsonAsync("signup", new { email, password });
        return await ParseAuthResponse(response, email);
    }

    public async Task<AuthResult> SignInAsync(string email, string password)
    {
        if (!_config.IsSupabaseConfigured)
            return AuthResult.Fail("Screens is not connected to a Supabase project yet. See docs/SETUP.md.");

        var response = await _http.PostAsJsonAsync("token?grant_type=password", new { email, password });
        return await ParseAuthResponse(response, email);
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        var stored = await _settings.LoadSessionAsync();
        if (stored is null)
            return false;

        if (stored.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            CurrentSession = stored;
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", stored.AccessToken);
            return true;
        }

        // Refresh.
        var response = await _http.PostAsJsonAsync("token?grant_type=refresh_token", new { refresh_token = stored.RefreshToken });
        var result = await ParseAuthResponse(response, stored.Email);
        return result.Success;
    }

    public async Task SignOutAsync()
    {
        CurrentSession = null;
        // Reset to the anon key, not null — subsequent signup/signin calls
        // still need an Authorization header (see constructor comment).
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.SupabaseAnonKey);
        await _settings.ClearSessionAsync();
    }

    private async Task<AuthResult> ParseAuthResponse(HttpResponseMessage response, string email)
    {
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return AuthResult.Fail(FriendlyError(response.StatusCode.ToString(), body));

        var payload = JsonSerializer.Deserialize<GoTrueTokenResponse>(body, JsonOpts);
        if (payload?.AccessToken is null || payload.User?.Id is null)
            return AuthResult.Fail("Sign-in succeeded but no session was returned. Check that email confirmation is not required, or confirm your email and try again.");

        var session = new Session
        {
            AccessToken = payload.AccessToken,
            RefreshToken = payload.RefreshToken ?? "",
            UserId = payload.User.Id,
            Email = payload.User.Email ?? email,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn > 0 ? payload.ExpiresIn : 3600),
        };

        CurrentSession = session;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        await _settings.SaveSessionAsync(session);
        return AuthResult.Ok(session);
    }

    private static string FriendlyError(string statusCode, string body)
    {
        if (body.Contains("already registered", StringComparison.OrdinalIgnoreCase))
            return "An account with this email already exists.";
        if (body.Contains("Invalid login credentials", StringComparison.OrdinalIgnoreCase))
            return "Incorrect email or password.";
        if (body.Contains("Email not confirmed", StringComparison.OrdinalIgnoreCase))
            return "Please confirm your email address before signing in.";
        if (body.Contains("Password should be", StringComparison.OrdinalIgnoreCase))
            return "Password doesn't meet the project's requirements (check length/strength).";
        if (statusCode == "0" || body.Length == 0)
            return "Could not reach the server. Check your internet connection.";
        return $"Something went wrong ({statusCode}). {Truncate(body, 200)}";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    internal HttpClient Http => _http;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class GoTrueTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("user")] public GoTrueUser? User { get; set; }
    }

    private sealed class GoTrueUser
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
    }
}
