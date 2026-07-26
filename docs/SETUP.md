# Setup

## 1. Create the Supabase project

Screens ships with **no live backend configured** — `appsettings.json` and
`admin.html` both need to point at a Supabase project you create yourself.

1. Go to https://supabase.com and create a new project (free tier is fine).
   Use a **new** project — do not reuse another app's project.
2. In the SQL editor, run the contents of [`/db/schema.sql`](../db/schema.sql).
   This creates `license_keys`, `admins`, RLS policies, and the `redeem_key`
   RPC.
3. In **Project Settings → API**, copy:
   - **Project URL** (e.g. `https://xxxx.supabase.co`)
   - **anon / public key** — this is safe to embed in the desktop app and
     `admin.html`; it only works within the RLS policies in `schema.sql`.
   - **Do not** copy the `service_role` key anywhere in this repo. It is
     never used by the desktop app or the admin dashboard.
4. In **Authentication → Providers**, email/password should already be
   enabled by default. Decide whether to require email confirmation
   (Authentication → Settings) — either works with the app's sign-up flow.

## 2. Wire the desktop app to your project

Edit `src/Screens.App/appsettings.json`:

```json
{
  "Supabase": {
    "Url": "https://xxxx.supabase.co",
    "AnonKey": "<your anon key>"
  }
}
```

Until this is filled in, the app runs but shows "Screens is not connected to
a Supabase project yet" on sign-in instead of crashing.

## 3. Make yourself an admin

After signing up once through the app (or through Supabase's own dashboard
under **Authentication → Users**), find your user's UUID and run in the SQL
editor:

```sql
insert into public.admins (user_id) values ('<your-user-uuid>');
```

## 4. Run the admin dashboard

`admin/admin.html` is a single self-contained file — no build step.

1. Download it (or open it straight from this repo checkout).
2. Double-click to open it in a browser.
3. On first run, paste your Supabase **Project URL** and **anon key** into
   the two fields on the sign-in card, along with your admin email/password.
   These are saved in `localStorage` in that browser only — nothing is sent
   anywhere except your own Supabase project.
4. Generate keys, revoke/restore them, and see which users have redeemed
   one.

The same `admin.html` is also published to the release repo
(`screens-fnl-app`) so it can be downloaded without cloning this repo.

## 5. Generate a key and activate the app

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
