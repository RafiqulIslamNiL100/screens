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

## Icon set
Icons are hand-authored `Avalonia.Media.Geometry` path data (stroke-only, 1.5px weight, 2px corner radius) rather than an icon font or SVG asset pipeline, to avoid pulling in an SVG renderer dependency.

## Out of scope (v1) — see spec §15
Batch/CSV export, template designer UI, drag-to-reposition text, image upload into placeholders, cloud sync, multi-language UI. Noted here per spec instruction; not implemented.
