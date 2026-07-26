# Decisions

Engineering decisions made while implementing the BUILD SPEC where the spec allowed discretion.

## Supabase client
Using plain `HttpClient` against the Supabase REST (PostgREST) and GoTrue (auth) endpoints rather than the official `supabase-csharp` client. Reason: fewer transitive dependencies for a self-contained single-file publish, and the REST surface we need (sign up/in, session refresh, one RPC call, one table read) is small enough that a thin typed wrapper is simpler to audit than a general-purpose SDK.

## Supabase project credentials
`appsettings.json` is wired to the live project `https://gyedrlhxyzdjjanvbmvw.supabase.co` with its anon key committed (anon key is public by design). The `service_role` key was provided out-of-band and was deliberately **not** put in either repo or used by any committed code. This build environment's network policy blocks outbound access to `*.supabase.co`, so `db/schema.sql` could not be executed against the project from here; `docs/SETUP.md` has the one-time manual step (paste `schema.sql` into the Supabase SQL editor) to finish activating the backend.

## Font licensing
Bundled fonts: **Inter** (OFL-1.1) for UI chrome, **Playfair Display** (OFL-1.1) for certificate/award templates, **Cabin** (OFL-1.1) for badges/flyers. License files committed under `/src/Screens.App/Assets/Fonts/licenses/`.

## Template artwork
All 8 bundled template background images are generated procedurally (gradients, geometric shapes, rules) in the app's purple palette — no stock photography, no third-party or brand assets. Generation script kept at `/src/Screens.App/Assets/Templates/generate_templates.py` (Pillow) for reproducibility; only the resulting PNGs ship in the app.

## Update checker HTTP client
`UpdateService` uses `HttpClient` with a pinned `User-Agent` and downloads to `%TEMP%\Screens-Update-<version>.exe`; SHA-256 is computed with `System.Security.Cryptography.SHA256` before launch.

## Rendering backend
`Program.cs` configures Avalonia's Win32 backend with `RenderingMode = [Software]`
instead of the default hardware/ANGLE path. During Wine+Xvfb acceptance
testing the default path produced a fully black window (CoreCLR and the
Win32 message loop both ran fine — DXGI/ANGLE just never actually painted
anything under Wine's software GL). Skia's CPU rasterizer is fast enough for
this app's 2D UI and fixes the black-window failure mode on any machine
whose GPU/driver can't satisfy the hardware path (Wine, some RDP sessions,
older integrated GPUs) without penalizing normal hardware — there is nothing
GPU-bound about this UI.

## Desktop app keeps Supabase Auth; admin.html does not (explicit user request)
The app's screen flow is the original spec's: Sign up/Sign in (any email + password ≥6 characters, no special allowlist) → Activation (enter a key) → Main. License keys are claimed via `redeem_key(p_key)`, a security-definer RPC keyed off `auth.uid()`, same as the original design.

`admin.html` is the one place with no login — that was a separate, explicit, earlier decision (kept from a prior iteration): it only asks for the Supabase project URL and anon key, and `db/schema.sql` grants the unauthenticated `anon` role full `select/insert/update/delete` on `license_keys` via a permissive RLS policy. Practical effect: **the anon key is a master key for license management** — anyone who has it (including anyone who extracts it from the desktop app's `appsettings.json`, since it ships in the .exe) can generate, view, and revoke every license key, with no audit trail of who did it. There is no `admins` table gating this — it was dropped as unused. Restoring access control on `admin.html` would mean adding a login flow (Supabase Auth + an admin allowlist) back into it from scratch.

(An earlier pass briefly removed Supabase Auth from the desktop app too, replacing accounts with a per-device id. That was reverted — the app keeps real accounts. `db/schema.sql`'s first two statements clean up the now-unused `redeem_key(text, text)` device-based function signature from that reverted iteration, in case it was already run against a project.)

## Time-limited license keys
`license_keys` has `duration_days` (set at generation time in `admin.html`: 7/30/90/180/365 days, a custom day count, or left null for a lifetime key) and `expires_at` (computed by `redeem_key` as `redeemed_at + duration_days`, so the clock starts on activation, not on generation). `LicenseService.RefreshAsync` treats a key whose `expires_at` has passed as inactive (surfaced as `Status = "expired"`) even though its `status` column still reads `'active'` server-side — expiry is time-based, not a manual revoke. `admin.html`'s key table and Users list both compute the same "Expired" badge client-side from `expires_at`.

## GoTrue requires both `apikey` and `Authorization` headers
`AuthService`'s unauthenticated calls (signup, signin, refresh) were only sending the `apikey` header, not `Authorization: Bearer <token>`. Supabase's API gateway rejects that combination with a generic 401 that carried no matchable text, so it fell through to a useless "Something went wrong" message — reported by the user as sign-up failing outright. Fixed by always setting `Authorization: Bearer <anon key>` by default (GoTrue accepts the anon key here for unauthenticated calls), which `ParseAuthResponse` then overwrites with the real session token once one exists, and which `SignOutAsync` resets back to the anon key rather than clearing to null. Also widened `FriendlyError`'s fallback to include the actual status code and response body (truncated) instead of a bare "try again," since a second silent-failure mode elsewhere would otherwise be just as hard to diagnose from a screenshot.

## Fixed: app frozen on splash screen after sign-up
`LicenseService.RefreshAsync`'s catch block had `when (State.LastVerifiedAt is not null)` — meant to only apply the 72h offline-grace logic when there was a prior successful check to grace against. But that guard meant the catch didn't fire at all on the very first check (fresh `LicenseState`, `LastVerifiedAt == null`), which is exactly what runs right after a user signs up. Any failure there (network error, or — very likely what the user hit — `db/schema.sql` not yet applied to their project, so `license_keys` doesn't exist and the lookup 404s) went uncaught. Because this all runs inside `ShellViewModel.InitializeAsync()`, called fire-and-forget (`_ = shellVm.InitializeAsync();`) from `App.axaml.cs`, the exception had nowhere to go — it silently vanished, and `Screen` never advanced past its default `Splash` value. The window stayed open showing only "Screens" centered on a purple background, forever.

Fixed at three layers, deliberately redundant: `RefreshAsync` and `RedeemAsync` now catch unconditionally (verified with a standalone harness forcing a lookup failure on a first-ever check — see conversation), `ShellViewModel.InitializeAsync()` wraps its whole body in try/catch and falls back to the Auth screen on anything unexpected, and the `AuthViewModel.Authenticated` handler (itself an async-void event subscription, same silent-failure shape) does too. Reasoning: `LicenseService` should never throw here, full stop — but given how easily an async-void call site swallows one, `ShellViewModel` doesn't get to assume that stays true forever.

## Icon set
Icons are hand-authored `Avalonia.Media.Geometry` path data (stroke-only, 1.5px weight, 2px corner radius) rather than an icon font or SVG asset pipeline, to avoid pulling in an SVG renderer dependency.

## v1.1.0 feature batch (user-requested, 20+ items)
User asked for a large batch of "modern, user-friendly" features plus English/Chinese localization, all at once. Two were deliberately **not** built, with reasons given at the time: **multi-select templates** (batch-applying one set of field values across templates with different field schemas has no clean data model — the value doesn't justify the complexity) and **seasonal template packs** (a content/distribution question, not code — the existing `%APPDATA%\Screens\Templates` drop-in mechanism already covers it). Everything else shipped:

- **Localization**: `LocalizationService` is a flat key/value table per language (no .resx/satellite-assembly pipeline — keeps the self-contained single-file publish simple), English default + Chinese. ViewModels expose computed string properties (`HeadingText`, `FieldsLabel`, etc.) that call it and refresh via `OnPropertyChanged(string.Empty)` on a `LanguageChanged` event — same manual-refresh pattern `AuthViewModel` already used for its sign-up/sign-in text toggle, just driven by language instead of mode. UI chrome (menus, labels) relies on the OS's own CJK font fallback for Chinese glyphs — nothing bundled, since Windows ships one by default; this is separate from `FontRegistry`, which only serves canvas-rendered template text.
- **License countdown / renewal**: `LicenseState.ExpiresAt` (already added for time-limited keys) surfaces as a computed "N days remaining" string in Settings; a "Renew" link appears once ≤7 days remain, opening `RenewUrl` from `appsettings.json` (`Update.RenewUrl`, currently a placeholder pointing at the release repo).
- **Field editor additions**: `WillShrink` is a *heuristic* (chars-per-line estimate from box width ÷ average glyph width), not the render path itself — `RenderService` remains the single source of truth for actual shrinking; this is only an early warning before export. `userEditableColor`/`userEditableSize` (defined in the manifest schema since v1.0.0 but never wired to UI) now show a color textbox / size slider per field when set; the resulting override lives on `TemplateField.RuntimeColorOverride`/`RuntimeSizeOverride` — `[JsonIgnore]`, session-only, never written back to the manifest file.
- **Template search, duplicate & customize**: search filters the in-memory template list client-side (no index). Duplicate clones the manifest JSON + image into `%APPDATA%\Screens\Templates` (the same drop-in folder `TemplateService` already scans) with a new id and `userEditableColor`/`userEditableSize` forced on for every field, so the clone is immediately customizable without hand-editing JSON.
- **Multi-format export & presets**: `ExportFormat` gained `Pdf`, rendered via SkiaSharp's own `SKDocument.CreatePdf` (no new dependency) at a fixed 150→72 DPI scale-down so PDF page size stays print-reasonable; "All formats" fires PNG+JPG+PDF export back-to-back. Presets are just `(name, format, folder)` tuples in `AppSettings.ExportPresets`.
- **Watermark toggle**: applied inside `RenderService.Render` itself (not bolted on after) specifically so the live preview keeps matching the export exactly when enabled — spec's pixel-parity requirement extends to this new optional feature too.
- **Clipboard export**: Avalonia's cross-platform `IClipboard` only supports text, so this is a small direct Win32 P/Invoke (`OpenClipboard`/`SetClipboardData` with `CF_DIB`) gated behind `OperatingSystem.IsWindows()` — acceptable since win-x64 is the only shipping target.
- **Autosave drafts & recent templates**: `AppSettings.Drafts` is `Dictionary<templateId, Dictionary<fieldId, value>>`, written on every field change (same debounce-adjacent path as re-render) and restored when a template is reselected. `RecentTemplateIds` is a capped-at-8 MRU list, updated on every selection.
- **Per-field opacity/shadow + QR fields**: `TemplateField.Opacity` multiplies into the paint's alpha channel; `Shadow` draws a second, darker, slightly-offset pass before the real text. A new `Type: "qr"` field (via QRCoder, pure-managed, no native dep) renders a QR code sized to the field's box instead of text, and is excluded from the shrink-warning/color/size UI (none of that applies to an image). Added a live example to `flyer-event.json` (a ticket/info link) rather than leaving the feature undemonstrated.
- **Drag-to-reposition + snap-to-grid**: the canvas preview's `Image` and a transparent per-field hit-box overlay both live inside one `Viewbox`, so they scale together at any zoom level without separate pointer-to-canvas math in the common case. Dragging mutates `TemplateField.Box.X/Y` directly (in-memory only, never written back to the manifest — "Reset" reloads the template from disk specifically to undo this too) and snaps to a 10-canvas-pixel grid when enabled. `FieldBox` doesn't implement `INotifyPropertyChanged`, so the dragged overlay's on-screen position is updated imperatively in code-behind (`Canvas.SetLeft/SetTop`) rather than via binding — the initial position still comes from the one-time binding evaluation at template load.
- **Command palette (Ctrl+K)**: a fixed list of `(label, Action)` pairs, filtered client-side by substring match on the label. No fuzzy matching — the list is short enough that it doesn't need it.
- **System tray icon**: `Avalonia.Controls.TrayIcon` with a two-item native menu (Show / Export current template). Reuses `MainViewModel.ExportCommand` rather than duplicating export logic.
- **admin.html usage stats**: a 14-day generated-vs-redeemed bar chart computed client-side from data the page already loads (`created_at`/`redeemed_at` on `license_keys`) — deliberately no new table or write path, so this adds zero additional tracking surface.

## Out of scope (v1) — see spec §15
Batch/CSV export, template designer UI, image upload into placeholders, cloud sync. Multi-select templates and seasonal template packs from the v1.1.0 batch above also remain out of scope, for the reasons given there. Everything else originally deferred (drag-to-reposition, multi-language UI) shipped in v1.1.0.
