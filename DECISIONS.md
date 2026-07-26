# Decisions

Engineering decisions made while implementing the BUILD SPEC where the spec allowed discretion.

## Supabase client
Using plain `HttpClient` against the Supabase REST (PostgREST) and GoTrue (auth) endpoints rather than the official `supabase-csharp` client. Reason: fewer transitive dependencies for a self-contained single-file publish, and the REST surface we need (sign up/in, session refresh, one RPC call, one table read) is small enough that a thin typed wrapper is simpler to audit than a general-purpose SDK.

## Session persistence
Session (access token, refresh token, expiry) is persisted to `%APPDATA%\Screens\session.dat`, encrypted with Windows DPAPI (`ProtectedData.Protect`, `CurrentUser` scope). Never stored in plaintext.

## Supabase project credentials
`appsettings.json` is wired to the live project `https://gyedrlhxyzdjjanvbmvw.supabase.co` with its anon key committed (anon key is public by design). The `service_role` key was provided out-of-band and was deliberately **not** put in either repo or used by any committed code — per spec it must never appear in the desktop app or `admin.html`. This build environment's network policy blocks outbound access to `*.supabase.co`, so `db/schema.sql` could not be executed against the project from here; `docs/SETUP.md` has the one-time manual step (paste `schema.sql` into the Supabase SQL editor) to finish activating the backend. Everything downstream of that — the app's auth calls, `redeem_key` RPC, admin dashboard — is already pointed at the real project and will work as soon as the schema is applied.

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

## Icon set
Icons are hand-authored `Avalonia.Media.Geometry` path data (stroke-only, 1.5px weight, 2px corner radius) rather than an icon font or SVG asset pipeline, to avoid pulling in an SVG renderer dependency.

## Out of scope (v1) — see spec §15
Batch/CSV export, template designer UI, drag-to-reposition text, image upload into placeholders, cloud sync, multi-language UI. Noted here per spec instruction; not implemented.
