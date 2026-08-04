# Spike S0.9 — SQLite/EF Core concurrency + backup drill

**Date**: 2026-08-04 · **Status**: PASS

## Objective

Validate the storage model — one SQLite file, WAL, worker-only writes, API read-only connections, EF Core migrations, backup/restore — and produce the exact connection recipe Phase 1 will use. Packages: `Microsoft.EntityFrameworkCore.Sqlite` + `Microsoft.Data.Sqlite` **10.0.10**, `SQLitePCLRaw.bundle_e_sqlite3` **3.0.5** (pinned — see finding 2), `dotnet-ef` 10.0.9.

## Method

`spikes/S09-sqlite/` (net10). Schema mirroring the planned stores (`datasets`, `satellite_elements`, `audit`). EF Core migrations generated and applied (`InitialCreate` → `V2AddSource` = real `ALTER TABLE ADD COLUMN` on a populated DB). Modes: `init`, `writer` (EF inserts, 1k rows/80 ms — worker-like), `reader` (raw `Mode=ReadOnly` connection, SELECT loop — API-like), `migrate`, `backup`/`restore` (`BackupDatabase` + file replace incl. corruption recovery), `enforce`, `test` (full drill), plus a **cross-process** run (writer + reader as separate processes).

## Results

| Gate | Result |
|---|---|
| EF migrations on populated DB (add column) | PASS — 2 migrations applied; data preserved |
| Read-only enforcement (`Mode=ReadOnly`) | PASS — writes rejected (`attempt to write a readonly database`) — **after fixing the recipe** (finding 1) |
| In-process full drill (init → migrate → enforce → write → read → backup → corrupt → restore → verify) | PASS — ALL GREEN |
| Cross-process: writer 88,000 rows / 10 s (8,800/s) while reader queries | PASS — 779 reads, **mean 0.65 ms, p95 1.67 ms, max 10.4 ms, zero failures** |
| Backup + restore round-trip after simulated corruption | PASS — 27,000 rows recovered |

## Findings (important)

1. **`Cache=Shared` defeats `Mode=ReadOnly`**: with shared cache, an in-process pooled read-write connection allows writes through a nominally read-only connection (write "succeeded" in the first drill run). **Phase 1 recipe uses `Cache=Private` (default)** — read-only enforcement then verified correct even with pooled read-write connections present. (`Cache=Shared` is unnecessary — WAL provides the concurrency.)
2. **NU1903 (high severity)**: EF Core 10.0.10's transitive `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 bundles a vulnerable SQLite (GHSA-2m69-gcr7-jv3q). **Pin `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5** (current) in Phase 1. Supply-chain item recorded.
3. Cross-process WAL on a host bind mount works (same-machine; the S0.11 deploy re-verifies on the real volume).

## Phase 1 connection recipe (locked)

```
writer:  Data Source=<path>                + PRAGMA journal_mode=WAL; synchronous=NORMAL; busy_timeout=5000;
reader:  Data Source=<path>;Mode=ReadOnly  (no pragmas needed)
cache:   Private (default) — never Cache=Shared
pinning: SQLitePCLRaw.bundle_e_sqlite3 3.0.5 explicit reference
```

## Gate verdict

**PASS** — all gates green; recipe locked; findings recorded (Cache=Shared trap, SQLitePCLRaw pin).

## Decisions feeding

- ADR 15 (final): SQLite + EF Core; worker = only writer; API = read-only connections; `astronomy-cli db migrate` = EF `Migrate()` (deploy-time), `astronomy-cli db backup` = `BackupDatabase`.
- Phase 1 DB setup + `S09` code promoted as the reference pattern.
