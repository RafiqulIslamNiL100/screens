using System;

namespace Screens.App.Models;

public sealed class Session
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class LicenseState
{
    public bool IsActive { get; set; }
    public string? Key { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public string Status { get; set; } = "none"; // none | active | revoked
    /// <summary>Null = lifetime key, never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>Same shape and verification pattern as <see cref="LicenseState"/>, for the separate
/// "premium_templates"-type key that unlocks the admin-shipped template gallery — a user can hold
/// an app license, premium template access, both, or neither, independently of each other.</summary>
public sealed class PremiumAccessState
{
    public bool IsUnlocked { get; set; }
    public string? Key { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public string Status { get; set; } = "none"; // none | active | revoked
    public DateTimeOffset? ExpiresAt { get; set; }
}
