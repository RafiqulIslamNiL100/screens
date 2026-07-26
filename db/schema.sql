-- Screens database schema (Supabase / Postgres).
-- Run in the Supabase SQL editor for a fresh project. See docs/SETUP.md.

create table if not exists public.license_keys (
  key           text primary key,          -- XXXX-XXXX-XXXX-XXXX
  assigned_user uuid references auth.users(id),
  status        text not null default 'active'
                check (status in ('active','revoked')),
  created_at    timestamptz not null default now(),
  redeemed_at   timestamptz,
  note          text
);

create table if not exists public.admins (
  user_id uuid primary key references auth.users(id)
);

alter table public.license_keys enable row level security;
alter table public.admins enable row level security;

-- ---------------------------------------------------------------------
-- Helper: is the current JWT subject an admin?
-- ---------------------------------------------------------------------
create or replace function public.is_admin()
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (select 1 from public.admins a where a.user_id = auth.uid());
$$;

-- ---------------------------------------------------------------------
-- license_keys policies
-- ---------------------------------------------------------------------

-- A user can read only the row assigned to them.
drop policy if exists "user reads own license" on public.license_keys;
create policy "user reads own license"
  on public.license_keys
  for select
  using (assigned_user = auth.uid());

-- Admins can do everything.
drop policy if exists "admin full access to license_keys" on public.license_keys;
create policy "admin full access to license_keys"
  on public.license_keys
  for all
  using (public.is_admin())
  with check (public.is_admin());

-- No direct INSERT/UPDATE/DELETE policy is granted to plain users: redemption
-- only happens through the redeem_key() RPC below (security definer), so two
-- users can never race to claim the same key and a user can never edit their
-- own row's status.

-- ---------------------------------------------------------------------
-- ANON ADMIN ACCESS (opt-in, insecure by design)
-- ---------------------------------------------------------------------
-- admin.html was changed to skip Supabase Auth entirely and talk to
-- license_keys using only the project URL + anon key, so it needs the
-- unauthenticated `anon` role to have full access to this table.
--
-- This means: anyone who has your anon key (which also ships inside the
-- Screens desktop app and is not a secret) can generate, view, and revoke
-- every license key. There is no login gate on that access anymore. Only
-- keep this policy if you're comfortable with that — remove it and restore
-- an authenticated admin flow in admin.html if you ever need real access
-- control on key management.
drop policy if exists "anon full access to license_keys (admin.html, no login)" on public.license_keys;
create policy "anon full access to license_keys (admin.html, no login)"
  on public.license_keys
  for all
  to anon
  using (true)
  with check (true);

grant select, insert, update, delete on public.license_keys to anon;

-- ---------------------------------------------------------------------
-- admins policies
-- ---------------------------------------------------------------------

drop policy if exists "admin reads admins table" on public.admins;
create policy "admin reads admins table"
  on public.admins
  for select
  using (public.is_admin());

drop policy if exists "admin manages admins table" on public.admins;
create policy "admin manages admins table"
  on public.admins
  for all
  using (public.is_admin())
  with check (public.is_admin());

-- ---------------------------------------------------------------------
-- redeem_key(p_key): atomically claim an unassigned, active key for the
-- calling user. security definer so it can update a row the caller has no
-- direct UPDATE grant on; the WHERE clause on the UPDATE is what makes the
-- claim atomic and race-free (only one concurrent caller's UPDATE will
-- affect the row before assigned_user becomes non-null).
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
         redeemed_at = now()
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
grant execute on function public.is_admin() to authenticated;
