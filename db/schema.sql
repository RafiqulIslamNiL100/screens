-- Screens database schema (Supabase / Postgres).
-- Run in the Supabase SQL editor for a fresh project. See docs/SETUP.md.
-- Safe to re-run in full on an existing project — every statement is
-- idempotent (create-if-not-exists tables/columns, create-or-replace
-- functions, drop-then-create policies).

-- Clean up artifacts from earlier iterations of this schema that used a
-- device-id-based (no-account) redemption model:
drop function if exists public.redeem_key(text, text);

create table if not exists public.license_keys (
  key           text primary key,          -- XXXX-XXXX-XXXX-XXXX
  assigned_user uuid references auth.users(id),
  status        text not null default 'active'
                check (status in ('active','revoked')),
  created_at    timestamptz not null default now(),
  redeemed_at   timestamptz,
  -- Duration selected at generation time in admin.html (7/30/90/180/365
  -- days, or a custom number of days). Null = lifetime / no expiry.
  duration_days integer,
  -- Computed at redemption time as redeemed_at + duration_days. Null until
  -- redeemed, and stays null forever for a lifetime key.
  expires_at    timestamptz,
  note          text
);

alter table public.license_keys add column if not exists duration_days integer;
alter table public.license_keys add column if not exists expires_at timestamptz;

-- 'license' unlocks the app itself (the original, only key type); 'premium_templates'
-- unlocks the admin-shipped template gallery. Same table/redeem flow for both — the type is
-- decided when the key is generated in admin.html and never changes after that. A user can
-- hold both kinds of key redemption at once (they're different rows).
alter table public.license_keys add column if not exists type text not null default 'license'
  check (type in ('license', 'premium_templates'));

alter table public.license_keys enable row level security;

-- ---------------------------------------------------------------------
-- license_keys policies
-- ---------------------------------------------------------------------

-- A signed-in user can read only the row assigned to them.
drop policy if exists "user reads own license" on public.license_keys;
create policy "user reads own license"
  on public.license_keys
  for select
  using (assigned_user = auth.uid());

-- No direct INSERT/UPDATE/DELETE policy is granted to signed-in users:
-- redemption only happens through the redeem_key() RPC below (security
-- definer), so two users can never race to claim the same key and a user
-- can never edit their own row's status or expiry.

-- admin.html has no login (by request — see DECISIONS.md) and talks to this
-- table using only the anon key, so the unauthenticated `anon` role needs
-- full access. This means the anon key is effectively a master key for
-- license management: anyone who has it (including anyone who extracts it
-- from the desktop app's appsettings.json, since it ships in the .exe) can
-- generate, view, and revoke every license key, with no audit trail.
drop policy if exists "anon full access to license_keys" on public.license_keys;
create policy "anon full access to license_keys"
  on public.license_keys
  for all
  to anon
  using (true)
  with check (true);

grant select, insert, update, delete on public.license_keys to anon;

-- ---------------------------------------------------------------------
-- redeem_key(p_key): atomically claim an unassigned, active, non-expired
-- key for the calling (authenticated) user, and compute its expiry from
-- duration_days. security definer so it can update a row the caller has no
-- direct UPDATE grant on; the WHERE clause on the UPDATE is what makes the
-- claim atomic and race-free.
-- ---------------------------------------------------------------------
create or replace function public.redeem_key(p_key text)
returns public.license_keys
language plpgsql
security definer
set search_path = public
as $$
declare
  v_row public.license_keys;
begin
  if auth.uid() is null then
    raise exception 'not authenticated';
  end if;

  update public.license_keys
     set assigned_user = auth.uid(),
         redeemed_at = now(),
         expires_at = case when duration_days is null then null else now() + (duration_days || ' days')::interval end
   where key = p_key
     and status = 'active'
     and assigned_user is null
  returning * into v_row;

  if v_row.key is null then
    -- Distinguish "doesn't exist / already claimed" from "revoked" for a
    -- friendlier client-side error message.
    if exists (select 1 from public.license_keys where key = p_key and status = 'revoked') then
      raise exception 'key revoked';
    elsif exists (select 1 from public.license_keys where key = p_key and assigned_user is not null) then
      raise exception 'key already redeemed';
    else
      raise exception 'key not found';
    end if;
  end if;

  return v_row;
end;
$$;

grant execute on function public.redeem_key(text) to authenticated;

-- ---------------------------------------------------------------------
-- Admins: who can publish Premium Templates directly from the desktop app
-- (Settings → "Publish as Premium Template…"), instead of the old
-- export-file-then-hand-commit-to-GitHub workflow.
-- ---------------------------------------------------------------------
create table if not exists public.admins (
  user_id uuid primary key references auth.users(id) on delete cascade
);

alter table public.admins enable row level security;

-- A signed-in user can check only their own membership (the app uses this to
-- decide whether to show the publish button) — never the whole list, and
-- never write access through the API. Membership is granted only by
-- running an INSERT by hand in the Supabase SQL editor (see docs/SETUP.md)
-- using the project owner's own credentials, which bypass RLS there — so
-- there is no self-service path to becoming an admin, unlike the anon-key
-- tables above where that tradeoff was already accepted for admin.html.
drop policy if exists "user checks own admin status" on public.admins;
create policy "user checks own admin status"
  on public.admins
  for select
  to authenticated
  using (user_id = auth.uid());

grant select on public.admins to authenticated;

create or replace function public.is_admin()
returns boolean
language sql
security definer
stable
set search_path = public
as $$
  select exists (select 1 from public.admins where user_id = auth.uid());
$$;

grant execute on function public.is_admin() to authenticated;

-- ---------------------------------------------------------------------
-- Premium Templates catalog. Replaces the old premium-templates/index.json
-- file hosted in the screens-fnl-app GitHub repo — same shape, same "public,
-- unauthenticated read" posture (none of this is sensitive, it's the exact
-- content that used to sit in a public repo), but now writable directly by
-- an admin from inside the app instead of requiring a git commit.
-- ---------------------------------------------------------------------
create table if not exists public.premium_templates_catalog (
  id            text primary key,
  name          text not null,
  category      text not null default '',
  manifest_file text not null,
  image_file    text not null,
  version       integer not null default 1,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

alter table public.premium_templates_catalog enable row level security;

drop policy if exists "anyone reads catalog" on public.premium_templates_catalog;
create policy "anyone reads catalog"
  on public.premium_templates_catalog
  for select
  to anon, authenticated
  using (true);

drop policy if exists "admins write catalog" on public.premium_templates_catalog;
create policy "admins write catalog"
  on public.premium_templates_catalog
  for all
  to authenticated
  using (is_admin())
  with check (is_admin());

grant select on public.premium_templates_catalog to anon, authenticated;
grant insert, update, delete on public.premium_templates_catalog to authenticated;

-- ---------------------------------------------------------------------
-- Storage bucket for the actual template files (manifest JSON + background
-- image) that premium_templates_catalog.manifest_file/image_file name.
-- Public read (fetched the same way version.json/the installer always
-- were — plain HTTPS, no auth), admin-only write.
-- ---------------------------------------------------------------------
insert into storage.buckets (id, name, public)
values ('premium-templates', 'premium-templates', true)
on conflict (id) do nothing;

drop policy if exists "anyone reads premium-templates bucket" on storage.objects;
create policy "anyone reads premium-templates bucket"
  on storage.objects
  for select
  to anon, authenticated
  using (bucket_id = 'premium-templates');

drop policy if exists "admins write premium-templates bucket" on storage.objects;
create policy "admins write premium-templates bucket"
  on storage.objects
  for all
  to authenticated
  using (bucket_id = 'premium-templates' and is_admin())
  with check (bucket_id = 'premium-templates' and is_admin());
