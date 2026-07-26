# Decisions

Engineering decisions made while implementing the BUILD SPEC where the spec allowed discretion.

## Supabase client
Using plain `HttpClient` against the Supabase REST (PostgREST) and GoTrue (auth) endpoints rather than the official `supabase-csharp` client. Reason: fewer transitive dependencies for a self-contained single-file publish, and the REST surface we need (sign up/in, session refresh, one RPC call, one table read) is small enough that a thin typed wrapper is simpler to audit than a general-purpose SDK.

## Session persistence
Session (access token, refresh token, expiry) is persisted to `%APPDATA%\Screens\session.dat`, encrypted with Windows DPAPI (`ProtectedData.Protect`, `CurrentUser` scope). Never stored in plaintext.

## Supabase project credentials
No live Supabase project exists yet — creating one requires a Supabase account, which is out of scope for this repo. `appsettings.json` ships with placeholder `SupabaseUrl` / `SupabaseAnonKey` values and the app will surface a clear "not configured" error on the sign-in screen instead of crashing. `docs/SETUP.md` documents the exact steps to create the project, run `db/schema.sql`, and fill in the real values. This does not block anything else in the Definition of Done other than a live end-to-end auth test against a real backend.

## Font licensing
Bundled fonts: **Inter** (OFL-1.1) for UI chrome, **Playfair Display** (OFL-1.1) for certificate/award templates, **Cabin** (OFL-1.1) for badges/flyers. License files committed under `/src/Screens.App/Assets/Fonts/licenses/`.

## Template artwork
All 8 bundled template background images are generated procedurally (gradients, geometric shapes, rules) in the app's purple palette — no stock photography, no third-party or brand assets. Generation script kept at `/src/Screens.App/Assets/Templates/generate_templates.py` (Pillow) for reproducibility; only the resulting PNGs ship in the app.

## Update checker HTTP client
`UpdateService` uses `HttpClient` with a pinned `User-Agent` and downloads to `%TEMP%\Screens-Update-<version>.exe`; SHA-256 is computed with `System.Security.Cryptography.SHA256` before launch.

## Icon set
Icons are hand-authored `Avalonia.Media.Geometry` path data (stroke-only, 1.5px weight, 2px corner radius) rather than an icon font or SVG asset pipeline, to avoid pulling in an SVG renderer dependency.

## Out of scope (v1) — see spec §15
Batch/CSV export, template designer UI, drag-to-reposition text, image upload into placeholders, cloud sync, multi-language UI. Noted here per spec instruction; not implemented.
