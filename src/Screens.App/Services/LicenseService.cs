using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Screens.App.Models;

namespace Screens.App.Services;

public enum RedeemOutcome { Success, InvalidKey, AlreadyRedeemed, Revoked, NoNetwork, Unknown }

/// <summary>
/// Polls license status every 5 minutes and on window focus. Offline grace
/// window is 72 hours from the last successful verification (spec section 9).
/// </summary>
public sealed class LicenseService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OfflineGrace = TimeSpan.FromHours(72);

    private readonly AuthService _auth;
    private readonly AppConfig _config;
    private readonly SettingsService _settings;
    private Timer? _timer;

    public LicenseState State { get; private set; } = new();
    public bool InOfflineGrace { get; private set; }

    public event Action<LicenseState>? StateChanged;

    public LicenseService(AuthService auth, AppConfig config, SettingsService settings)
    {
        _auth = auth;
        _config = config;
        _settings = settings;
    }

    public async Task InitializeAsync()
    {
        State = await _settings.LoadLicenseStateAsync() ?? new LicenseState();
        await RefreshAsync();
        _timer = new Timer(async _ => await RefreshAsync(), null, PollInterval, PollInterval);
    }

    public async Task<RedeemOutcome> RedeemAsync(string key)
    {
        if (!_config.IsSupabaseConfigured)
            return RedeemOutcome.NoNetwork;

        try
        {
            var response = await _auth.Http.PostAsJsonAsync(
                $"{_config.SupabaseUrl.TrimEnd('/')}/rest/v1/rpc/redeem_key",
                new { p_key = key });

            if (response.IsSuccessStatusCode)
            {
                State = new LicenseState { IsActive = true, Key = key, Status = "active", LastVerifiedAt = DateTimeOffset.UtcNow };
                await _settings.SaveLicenseStateAsync(State);
                StateChanged?.Invoke(State);
                return RedeemOutcome.Success;
            }

            var body = await response.Content.ReadAsStringAsync();
            if (body.Contains("already", StringComparison.OrdinalIgnoreCase))
                return RedeemOutcome.AlreadyRedeemed;
            if (body.Contains("revoked", StringComparison.OrdinalIgnoreCase))
                return RedeemOutcome.Revoked;
            if (body.Contains("not found", StringComparison.OrdinalIgnoreCase) || body.Contains("invalid", StringComparison.OrdinalIgnoreCase))
                return RedeemOutcome.InvalidKey;
            return RedeemOutcome.Unknown;
        }
        catch (HttpRequestException)
        {
            return RedeemOutcome.NoNetwork;
        }
    }

    public async Task RefreshAsync()
    {
        if (!_config.IsSupabaseConfigured || _auth.CurrentSession is null)
            return;

        try
        {
            var url = $"{_config.SupabaseUrl.TrimEnd('/')}/rest/v1/license_keys?assigned_user=eq.{_auth.CurrentSession.UserId}&select=key,status";
            var response = await _auth.Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("license lookup failed");

            var rows = await response.Content.ReadFromJsonAsync<LicenseRow[]>(JsonOpts);
            var row = rows is { Length: > 0 } ? rows[0] : null;

            InOfflineGrace = false;
            State = new LicenseState
            {
                IsActive = row?.Status == "active",
                Key = row?.Key,
                Status = row?.Status ?? "none",
                LastVerifiedAt = DateTimeOffset.UtcNow,
            };
            await _settings.SaveLicenseStateAsync(State);
            StateChanged?.Invoke(State);
        }
        catch (Exception) when (State.LastVerifiedAt is not null)
        {
            var elapsed = DateTimeOffset.UtcNow - State.LastVerifiedAt.Value;
            InOfflineGrace = elapsed <= OfflineGrace;
            if (!InOfflineGrace)
            {
                State.IsActive = false;
                State.Status = "none";
                await _settings.SaveLicenseStateAsync(State);
            }
            StateChanged?.Invoke(State);
        }
    }

    public void Dispose() => _timer?.Dispose();

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class LicenseRow
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
    }
}
