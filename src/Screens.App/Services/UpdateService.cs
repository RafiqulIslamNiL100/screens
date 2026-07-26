using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Screens.App.Services;

public sealed class UpdateInfo
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("mandatory")] public bool Mandatory { get; set; }
}

public sealed record UpdateCheckResult(bool UpdateAvailable, UpdateInfo? Info, string CurrentVersion);

/// <summary>
/// Update flow is: download the real installer, verify its hash, run it
/// silently, relaunch. Never a ZIP-extract-over-a-running-process updater
/// (spec section 11) — that fails on locked DLLs and trips antivirus.
/// </summary>
public sealed class UpdateService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly AppConfig _config;

    public DateTimeOffset? LastCheckedAt { get; private set; }

    public UpdateService(AppConfig config) => _config = config;

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public async Task<UpdateCheckResult> CheckAsync()
    {
        LastCheckedAt = DateTimeOffset.UtcNow;
        try
        {
            var info = await _http.GetFromJsonAsync<UpdateInfo>(_config.VersionJsonUrl);
            if (info is null)
                return new UpdateCheckResult(false, null, CurrentVersion);

            var isNewer = CompareSemver(info.Version, CurrentVersion) > 0;
            return new UpdateCheckResult(isNewer, info, CurrentVersion);
        }
        catch
        {
            // Non-blocking, silent failure per spec section 11 step 1.
            return new UpdateCheckResult(false, null, CurrentVersion);
        }
    }

    /// <summary>
    /// Downloads the installer, verifies SHA-256, launches it with /S, and
    /// writes a detached helper that waits for the installer PID then
    /// relaunches Screens.exe. Caller should exit the app immediately after
    /// this returns true so its files unlock for the silent install.
    /// </summary>
    public async Task<(bool Started, string? Error)> DownloadAndInstallAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        var tempInstaller = Path.Combine(Path.GetTempPath(), $"Screens-Update-{info.Version}.exe");

        try
        {
            using (var response = await _http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1L;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var dest = File.Create(tempInstaller);
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read));
                    readTotal += read;
                    if (total > 0)
                        progress?.Report((double)readTotal / total);
                }
            }

            var actualHash = ComputeSha256(tempInstaller);
            if (!string.Equals(actualHash, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempInstaller);
                return (false, "Downloaded installer failed signature verification (SHA-256 mismatch). Update aborted.");
            }

            var currentExe = Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(AppContext.BaseDirectory, "Screens.exe");
            var helperPath = Path.Combine(Path.GetTempPath(), "screens-update-relaunch.cmd");
            var currentPid = Environment.ProcessId;

            var script =
                $"@echo off\r\n" +
                $":wait\r\n" +
                $"tasklist /FI \"PID eq {currentPid}\" | find \"{currentPid}\" >nul\r\n" +
                $"if not errorlevel 1 (\r\n" +
                $"  timeout /t 1 /nobreak >nul\r\n" +
                $"  goto wait\r\n" +
                $")\r\n" +
                $"\"{tempInstaller}\" /S\r\n" +
                $":waitinstall\r\n" +
                $"tasklist /FI \"IMAGENAME eq {Path.GetFileName(tempInstaller)}\" | find /I \"{Path.GetFileName(tempInstaller)}\" >nul\r\n" +
                $"if not errorlevel 1 (\r\n" +
                $"  timeout /t 1 /nobreak >nul\r\n" +
                $"  goto waitinstall\r\n" +
                $")\r\n" +
                $"start \"\" \"{currentExe}\"\r\n" +
                $"del \"%~f0\"\r\n";

            await File.WriteAllTextAsync(helperPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{helperPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Update failed: {ex.Message}");
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>Proper semver comparison, not string comparison (spec section 11).</summary>
    internal static int CompareSemver(string a, string b)
    {
        static int[] Parse(string v)
        {
            var core = v.Split('-', '+')[0];
            var parts = core.Split('.');
            var result = new int[3];
            for (var i = 0; i < 3 && i < parts.Length; i++)
                int.TryParse(parts[i], out result[i]);
            return result;
        }

        var pa = Parse(a);
        var pb = Parse(b);
        for (var i = 0; i < 3; i++)
        {
            if (pa[i] != pb[i])
                return pa[i].CompareTo(pb[i]);
        }
        return 0;
    }
}
