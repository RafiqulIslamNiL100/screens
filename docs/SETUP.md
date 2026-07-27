# Setup

## Status: project connected, schema not yet applied

`appsettings.json` already points at the live project
`https://gyedrlhxyzdjjanvbmvw.supabase.co` with its anon key wired in — that
part is done and committed. What's **not** done, and can't be done from this
build environment (its network policy blocks `*.supabase.co` outbound), is
running the schema. **You need to do step 1 below once, by hand, in the
Supabase dashboard** — everything else already works.

## 1. Run the schema

1. Open your project's SQL editor: https://supabase.com/dashboard/project/gyedrlhxyzdjjanvbmvw/sql/new
2. Paste in the full contents of [`/db/schema.sql`](../db/schema.sql) and run it.
   This creates `license_keys` (with duration/expiry columns, and a `type`
   column distinguishing an app-activation key from a `premium_templates`
   access key — same table, same `redeem_key` RPC, both key kinds), RLS
   (authenticated users read their own key; the `anon` role gets full access
   for `admin.html`), and the `redeem_key` RPC. Safe to re-run in full —
   every statement is idempotent. **If you ran this before the `type` column
   existed, re-run it once** to pick that up — every existing key defaults to
   `type = 'license'`, so nothing already issued changes behavior.
3. Under **Authentication → Providers**, email/password is enabled by
   default — no change needed. Decide whether to require email confirmation
   (Authentication → Settings); either setting works with the app's sign-up
   flow.

## 2. admin.html has no login (by request)

`admin.html` does **not** require a Supabase account or sign-in — it only
asks for the project URL and anon key. There is no admin account to create.

**Understand the tradeoff before relying on this**: the anon key is not a
secret — it ships inside the desktop app and anyone can extract it — so
anyone who has it can generate, view, and revoke every license key with no
audit trail. See `DECISIONS.md` for the full explanation.

The desktop app itself is unaffected by this — it still uses real Supabase
Auth accounts (any email + password, sign up freely) to gate the Activation
screen.

## Credentials already wired in

`src/Screens.App/appsettings.json`:
```json
{
  "Supabase": {
    "Url": "https://gyedrlhxyzdjjanvbmvw.supabase.co",
    "AnonKey": "eyJhbGciOi..."
  },
  "Update": {
    "VersionJsonUrl": "...",
    "RenewUrl": "https://github.com/RafiqulIslamNiL100/screens-fnl-app"
  }
}
```
`Update.RenewUrl` is where Settings' "Renew license" button (shown once a
license has 7 or fewer days left) sends the user — currently a placeholder
pointing at the release repo; point it at a real purchase/renewal page
whenever you have one.

The anon key is safe to commit — it's public by design and only works within
the RLS policies in `schema.sql`. The `service_role` key was **not** put
anywhere in either repo — it must never appear in the desktop app or in
`admin.html`.

Until schema.sql has been run (step 1), sign-up/sign-in in the app will fail
with Postgres/GoTrue errors about missing tables — that's expected until you
complete step 1, not a bug.

## 3. Run the admin dashboard

`admin/admin.html` is a single self-contained file — no build step, no login.

1. Download it (or open it straight from this repo checkout).
2. Double-click to open it in a browser.
3. On first run, paste in:
   - Project URL: `https://gyedrlhxyzdjjanvbmvw.supabase.co`
   - Anon key: see `appsettings.json` (same value)
   These are saved in `localStorage` in that browser only and it connects
   immediately.
4. Generate keys — pick a **duration** (1 week / 1 / 3 / 6 / 12 months /
   lifetime / custom day count). The countdown starts when a user redeems
   the key, not when you generate it. Revoke/restore keys, and see which
   users have redeemed one (with their expiry) under **Users**.

The same `admin.html` is also published to the release repo
(`screens-fnl-app`) so it can be downloaded without cloning this repo.

## 4. Generate a key and activate the app

1. In `admin.html`, generate at least one key.
2. Sign up in the Screens app (Sign Up on the auth screen — any email and a
   password of 6+ characters works, no allowlist).
3. On the Activation screen, type the key (auto-formats as
   `XXXX-XXXX-XXXX-XXXX`).
4. The app unlocks into the main window. It re-locks automatically if the
   key is revoked or its duration expires (checked every 5 minutes and on
   window focus).

## Running the app locally (development)

```
cd src/Screens.App
dotnet run
```

Requires the .NET 8 SDK. On Linux/macOS this runs under the cross-platform
Avalonia backend for development; the shipped installer only targets
win-x64 (see `/docs/RELEASE.md`).
