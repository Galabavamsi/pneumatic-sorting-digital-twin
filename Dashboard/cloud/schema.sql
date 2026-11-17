-- Cloud relay for the rig dashboard (Supabase / Postgres).
--
-- The gateway PC calls ingest_machine_state() with a per-machine token; the app
-- reads machine_state with the publishable key. Nothing here can reach the PLC:
-- the data only ever flows from the machine outwards.

create table if not exists public.machine_state (
    id          text primary key,              -- machine id, e.g. 'rig-1'
    snapshot    jsonb not null,                -- latest snapshot from Tools/PlcGateway
    updated_at  timestamptz not null default now()
);

-- One row per machine that is allowed to publish. Only the SHA-256 of the token
-- is stored. No policies: not readable or writable through the API.
create table if not exists public.machine_ingest_keys (
    machine_id  text primary key,
    token_hash  text not null
);

alter table public.machine_state enable row level security;
alter table public.machine_ingest_keys enable row level security;

drop policy if exists "machine_state is readable" on public.machine_state;
create policy "machine_state is readable"
    on public.machine_state for select
    to anon, authenticated
    using (true);

create or replace function public.ingest_machine_state(p_machine text, p_token text, p_snapshot jsonb)
returns void
language plpgsql
security definer
set search_path = ''
as $$
begin
    if not exists (
        select 1
        from public.machine_ingest_keys k
        where k.machine_id = p_machine
          and k.token_hash = encode(sha256(convert_to(p_token, 'UTF8')), 'hex')
    ) then
        raise exception 'not allowed' using errcode = '42501';
    end if;

    insert into public.machine_state (id, snapshot, updated_at)
    values (p_machine, p_snapshot, now())
    on conflict (id) do update
        set snapshot = excluded.snapshot,
            updated_at = now();
end;
$$;

revoke all on function public.ingest_machine_state(text, text, jsonb) from public;
grant execute on function public.ingest_machine_state(text, text, jsonb) to anon, authenticated;

-- Push changes to the app as they happen.
do $$
begin
    if not exists (
        select 1 from pg_publication_tables
        where pubname = 'supabase_realtime' and schemaname = 'public' and tablename = 'machine_state'
    ) then
        alter publication supabase_realtime add table public.machine_state;
    end if;
end;
$$;

-- To allow a machine to publish (run once, with your own long random token):
--   insert into public.machine_ingest_keys (machine_id, token_hash)
--   values ('rig-1', encode(sha256(convert_to('<token>', 'UTF8')), 'hex'));
