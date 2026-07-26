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
}
