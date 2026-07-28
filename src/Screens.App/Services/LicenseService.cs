using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Screens.App.Models;

namespace Screens.App.Services;

public enum RedeemOutcome { Success, InvalidKey, AlreadyRedeemed, Revoked, NoNetwork, Unknown, WrongKeyType }

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
                var redeemed = await response.Content.ReadFromJsonAsync<LicenseRow>(JsonOpts);

                // The key is now claimed under the caller's account regardless of type — the
                // RPC has no notion of "which screen was this typed into." A premium_templates
                // key entered here is still validly redeemed (RefreshAsync's own type=eq.license
                // filter just won't ever surface it as an app license), but the app license
                // itself must not silently activate off the wrong kind of key.
                if (redeemed?.Type is not (null or "license"))
                    return RedeemOutcome.WrongKeyType;

                State = new LicenseState
                {
                    IsActive = true,
                    Key = key,
                    Status = "active",
                    ExpiresAt = redeemed?.ExpiresAt,
                    LastVerifiedAt = DateTimeOffset.UtcNow,
                };
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
        catch (Exception)
        {
            // Broad on purpose: a malformed response, timeout, or DNS
            // failure must surface as "couldn't reach the server," not
            // crash the RelayCommand that called this.
            return RedeemOutcome.NoNetwork;
        }
    }

    public async Task RefreshAsync()
    {
        if (!_config.IsSupabaseConfigured || _auth.CurrentSession is null)
            return;

        try
        {
            var url = $"{_config.SupabaseUrl.TrimEnd('/')}/rest/v1/license_keys?assigned_user=eq.{_auth.CurrentSession.UserId}&type=eq.license&select=key,status,expires_at";
            var response = await _auth.Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("license lookup failed");

            var rows = await response.Content.ReadFromJsonAsync<LicenseRow[]>(JsonOpts);
            var row = rows is { Length: > 0 } ? rows[0] : null;
            var expired = row?.ExpiresAt is not null && row.ExpiresAt.Value <= DateTimeOffset.UtcNow;

            InOfflineGrace = false;
            State = new LicenseState
            {
                IsActive = row?.Status == "active" && !expired,
                Key = row?.Key,
                Status = expired ? "expired" : (row?.Status ?? "none"),
                ExpiresAt = row?.ExpiresAt,
                LastVerifiedAt = DateTimeOffset.UtcNow,
            };
            await _settings.SaveLicenseStateAsync(State);
            StateChanged?.Invoke(State);
        }
        catch (Exception)
        {
            // Must never throw out of here: this runs during app startup
            // (ShellViewModel.InitializeAsync) and on a background timer, and
            // an uncaught exception on either path used to silently strand
            // the UI on the splash screen forever. Covers real network
            // failures (72h offline grace applies) and server-side errors
            // like a not-yet-applied schema (no prior verification to grace
            // against, so this always de-activates instead of throwing).
            if (State.LastVerifiedAt is not null)
            {
                var elapsed = DateTimeOffset.UtcNow - State.LastVerifiedAt.Value;
                InOfflineGrace = elapsed <= OfflineGrace;
            }
            else
            {
                InOfflineGrace = false;
            }

            if (!InOfflineGrace)
            {
                State.IsActive = false;
                State.Status = "none";
                await _settings.SaveLicenseStateAsync(State);
            }
            StateChanged?.Invoke(State);
        }
    }

    /// <summary>Clears in-memory and cached-on-disk license state and notifies subscribers —
    /// called on sign-out so a different account signing in next never briefly inherits this
    /// account's activation status. The signed-out account's key stays redeemed server-side
    /// (assigned_user is permanent); this only clears what's cached on this machine.</summary>
    public async Task ResetAsync()
    {
        State = new LicenseState();
        InOfflineGrace = false;
        await _settings.ClearLicenseCacheAsync();
        StateChanged?.Invoke(State);
    }

    public void Dispose() => _timer?.Dispose();

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class LicenseRow
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
    }
}
