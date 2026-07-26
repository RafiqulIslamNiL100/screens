# Setup

## Status: project connected, schema not yet applied

`appsettings.json` already points at the live project
`https://gyedrlhxyzdjjanvbmvw.supabase.co` with its anon key wired in — that
part is done and committed. What's **not** done, and can't be done from this
build environment (its network policy blocks `*.supabase.co` outbound), is
running the schema. **You need to do steps 1 and 2 below once, by hand, in
the Supabase dashboard** — everything else already works.

## 1. Run the schema

1. Open your project's SQL editor: https://supabase.com/dashboard/project/gyedrlhxyzdjjanvbmvw/sql/new
2. Paste in the full contents of [`/db/schema.sql`](../db/schema.sql) and run it.
   This creates `license_keys`, `admins`, RLS policies, and the `redeem_key`
   RPC. Safe to re-run — every statement uses `if not exists` / `create or
   replace`.
3. Under **Authentication → Providers**, email/password is enabled by
   default — no change needed. Decide whether to require email confirmation
   (Authentication → Settings); either setting works with the app's sign-up
   flow.

## 2. Make yourself an admin

1. Sign up once through the Screens app (or create the user directly under
   **Authentication → Users** in the dashboard) using the email you want to
   administer with.
2. Copy that user's UUID from the Users table, then in the SQL editor:
   ```sql
   insert into public.admins (user_id) values ('<your-user-uuid>');
   ```
3. You can now sign in to `admin/admin.html` with that account.

## Credentials already wired in

`src/Screens.App/appsettings.json`:
```json
{
  "Supabase": {
    "Url": "https://gyedrlhxyzdjjanvbmvw.supabase.co",
    "AnonKey": "eyJhbGciOi..."
  }
}
```
The anon key is safe to commit — it's public by design and only works within
the RLS policies in `schema.sql`. The `service_role` key was **not** put
anywhere in either repo, per spec — it must never appear in the desktop app
or in `admin.html`.

Until schema.sql has been run (step 1), sign-up/sign-in in the app will fail
with Postgres/GoTrue errors about missing tables — that's expected until you
complete step 1, not a bug.

## 3. Run the admin dashboard

`admin/admin.html` is a single self-contained file — no build step.

1. Download it (or open it straight from this repo checkout).
2. Double-click to open it in a browser.
3. On first run, paste in:
   - Project URL: `https://gyedrlhxyzdjjanvbmvw.supabase.co`
   - Anon key: see `appsettings.json` (same value, safe to reuse — it's the
     public key)
   - Your admin email/password (from step 2 above)
   These are saved in `localStorage` in that browser only — nothing is sent
   anywhere except your own Supabase project.
4. Generate keys, revoke/restore them, and see which users have redeemed
   one.

The same `admin.html` is also published to the release repo
(`screens-fnl-app`) so it can be downloaded without cloning this repo.

## 4. Generate a key and activate the app

1. In `admin.html`, generate at least one key.
2. Sign up in the Screens app (Sign Up on the auth screen).
3. On the Activation screen, type the key (auto-formats as
   `XXXX-XXXX-XXXX-XXXX`).
4. The app unlocks into the main window.

## Running the app locally (development)

```
cd src/Screens.App
dotnet run
```

Requires the .NET 8 SDK. On Linux/macOS this runs under the cross-platform
Avalonia backend for development; the shipped installer only targets
win-x64 (see `/docs/RELEASE.md`).
