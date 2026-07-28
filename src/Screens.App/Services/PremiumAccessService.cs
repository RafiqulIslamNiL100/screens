using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Screens.App.Models;

namespace Screens.App.Services;

/// <summary>
/// Same shape and verification pattern as <see cref="LicenseService"/>, for the separate
/// "premium_templates"-type key that unlocks the admin-shipped template gallery. Reuses the same
/// license_keys table and redeem_key() RPC — the type column (set once, at key generation time in
/// admin.html) is what distinguishes "activates the app" from "unlocks premium templates," never
/// the client. Deliberately its own small service rather than folded into LicenseService: the two
/// unlock independently of each other (a user can hold either, both, or neither), and keeping them
/// separate means a bug in one genuinely can't affect the other's state.
/// </summary>
public sealed class PremiumAccessService
{
    private readonly AuthService _auth;
    private readonly AppConfig _config;
    private readonly SettingsService _settings;

    public PremiumAccessState State { get; private set; } = new();

    public event Action<PremiumAccessState>? StateChanged;

    public PremiumAccessService(AuthService auth, AppConfig config, SettingsService settings)
    {
        _auth = auth;
        _config = config;
        _settings = settings;
    }

    public async Task InitializeAsync()
    {
        State = await _settings.LoadPremiumAccessStateAsync() ?? new PremiumAccessState();
        await RefreshAsync();
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
                var redeemed = await response.Content.ReadFromJsonAsync<PremiumRow>(JsonOpts);

                // Claimed under the caller's account either way (see LicenseService's identical
                // comment) — a license-type key entered here is validly theirs now, just not for
                // this screen; RefreshAsync's own type filter will never surface it as unlocked.
                if (redeemed?.Type != "premium_templates")
                    return RedeemOutcome.WrongKeyType;

                State = new PremiumAccessState
                {
                    IsUnlocked = true,
                    Key = key,
                    Status = "active",
                    ExpiresAt = redeemed.ExpiresAt,
                    LastVerifiedAt = DateTimeOffset.UtcNow,
                };
                await _settings.SavePremiumAccessStateAsync(State);
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
            return RedeemOutcome.NoNetwork;
        }
    }

    public async Task RefreshAsync()
    {
        if (!_config.IsSupabaseConfigured || _auth.CurrentSession is null)
            return;

        try
        {
            var url = $"{_config.SupabaseUrl.TrimEnd('/')}/rest/v1/license_keys?assigned_user=eq.{_auth.CurrentSession.UserId}&type=eq.premium_templates&select=key,status,expires_at";
            var response = await _auth.Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("premium access lookup failed");

            var rows = await response.Content.ReadFromJsonAsync<PremiumRow[]>(JsonOpts);
            var row = rows is { Length: > 0 } ? rows[0] : null;
            var expired = row?.ExpiresAt is not null && row.ExpiresAt.Value <= DateTimeOffset.UtcNow;

            State = new PremiumAccessState
            {
                IsUnlocked = row?.Status == "active" && !expired,
                Key = row?.Key,
                Status = expired ? "expired" : (row?.Status ?? "none"),
                ExpiresAt = row?.ExpiresAt,
                LastVerifiedAt = DateTimeOffset.UtcNow,
            };
            await _settings.SavePremiumAccessStateAsync(State);
            StateChanged?.Invoke(State);
        }
        catch (Exception)
        {
            // Never throw out of here (same reasoning as LicenseService.RefreshAsync): this runs
            // during startup and must not strand the UI. Unlike the app license there's no
            // offline grace to extend for premium templates — losing connectivity just means the
            // gallery can't be reached until it's back, which is already the natural behavior of
            // "reuses last-cached local state" without needing a separate grace timer.
        }
    }

    /// <summary>Clears in-memory and cached-on-disk premium-access state and notifies
    /// subscribers — called on sign-out so a different account signing in next never briefly
    /// inherits this account's unlock status. The signed-out account's key stays redeemed
    /// server-side (assigned_user is permanent); this only clears what's cached on this machine.</summary>
    public async Task ResetAsync()
    {
        State = new PremiumAccessState();
        await _settings.ClearPremiumAccessCacheAsync();
        StateChanged?.Invoke(State);
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}

file sealed class PremiumRow
{
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}
