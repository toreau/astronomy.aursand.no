# Spike S0.11 — Coolify deploy + host gates

**Date**: 2026-08-04 · **Status**: PASS (deployment pattern + host gates; SPICE ≤ 1″ host gate deferred with recorded risk)

## Objective

Prove the production deployment pattern on the Coolify instance (Debian trixie amd64 host, Coolify 4.1.2) and run the host-only scientific gates: Horizons fixtures (closes the S0.2 position gate), NAIF integrity checks, and reachability verdicts.

## Deployment pattern (proven)

- Coolify project `astronomy.aursand.no` (project uuid `d550f0sx57uc3x9w0u92cwh7`); two git apps from `toreau/astronomy.aursand.no` (dockerfile build pack, `dockerfile_location` per app):
  - **api** (`jk87r6rrgoegw3s6v3hz4ulu`): fqdn `https://astronomy.aursand.no`, port 8080, rootless (uid 10001), no-Curl image
  - **worker** (`p47lnt171dhf6kec7dn2jbtj`): no public domain, runs as root (see finding 3), heartbeat loop + operational subcommands
- **Shared volume**: host path `/data/astronomy` mounted at `/data` in BOTH apps (Coolify persistent storage, host-path mount) — verified end-to-end: api `/ready` reads the db the worker created
- **Health checks**: Coolify-native health checks disabled (see finding 2); app endpoints `/healthz` + `/ready` (SQLite `SELECT 1`, read-only connection per S0.9 recipe) verified live
- **TLS**: Let's Encrypt via the Coolify/Traefik proxy — `astronomy.aursand.no` cert valid 2026-08-04 → 2026-11-02
- **Live verification** (from the public internet):
  - `GET https://astronomy.aursand.no/healthz` → `{"status":"ok"}` 200
  - `GET https://astronomy.aursand.no/ready` → `{"status":"ready","db":"ok"}` 200
  - `GET https://astronomy.aursand.no/` → skeleton 200
- **Rollback mechanics**: validated for real — the first api deploy (image without curl) failed the health gate and Coolify rolled back automatically ("New container is not healthy, rolling back to the old container")
- **Operational pattern**: worker subcommands (`probe`, `fixtures`, `compare`, `naif`) executed via Coolify scheduled tasks (`run_once`) — no public admin endpoints; logs are the evidence channel

## Findings

1. **`apt` is extremely slow on the host** (Ubuntu index downloads at ~30–60 KB/s; a 19.3 MB Packages file took 11+ minutes) — this made every api deploy look "stuck" (they were slowly running `apt-get install curl`). **Fix: no apt in the api image** (curl existed only for the Coolify health check). Worker image never needed apt (managed HttpClient only).
2. **Health-check strategy decision (Phase 1)**: Coolify-native checks stay **off** (they require curl/wget in the image → apt → slow builds). `/healthz`/`/ready` remain; external monitoring via the instance's uptime-kuma (status.aursand.no) is the Phase 1 model. Re-enable paths documented: pre-baked base image with curl (one slow build, then layer-cached), or a TCP-style check if the installed Coolify supports it.
3. **Volume permissions**: `/data/astronomy` (root-owned host dir) is not writable by non-root container users → the worker runs as root (internal-only container; acceptable for the skeleton). Phase 1 item: host-side `chown` as a documented deploy step, or a dedicated volume strategy.
4. **Deploy-queue behavior**: API-triggered deploys interleave with GitHub webhook deploys (auto-deploy on push — works); a wedged deployment holds the queue slot — cancel-then-deploy is the recovery pattern.
5. **`CosineKitty` engine `Equatorial.ra` is in HOURS, not degrees** — latent bug in the never-run S0.2 harness; caught by the first real Horizons comparison (Dec matched to 4 decimals, RA off ×15 exactly). Converted `×15` in the worker comparison. **Recorded for Phase 2: engine RA is hours.**
6. **TEME/TOD frame kernels are NOT in NAIF generic kernels** (fk/satellites and fk/planets listings checked) — `teme.tf` comes from the SGP4 community (CelesTrak/Vallado distribution); `earth_assoc_itrf93.tf` (official NAIF) downloaded. Phase 4 artifact list updated.
7. Horizons batch API emits the table as **one comma-joined line** (no per-row newlines) with two leading empty fields per row and two unidentified trailing values per row (default-column artifacts) — token-based parsing required.

## Host gates (Branch A + B) — all executed from the host network via the worker

**Connectivity (probe)**: ssd.jpl.nasa.gov, naif.jpl.nasa.gov, maia.usno.navy.mil, celestrak.org, cdsarc.cds.unistra.fr — **all HTTP 200 in < 2 s**. (The dev-network block is local routing only.)

**Branch A — Horizons fixtures + S0.2 position gate: CLOSED**

Fixtures fetched on the host: 6 bodies × 2,435 epochs (1900–2100 @ 30 d), `OBSERVER` geocentric, quantities 1/2/9 → `/data/fixtures/horizons_{body}.csv`. Astronomy Engine comparison (both semantic pairs):

| Body | J2000-astrometric mean / max | of-date-apparent mean / max | consumer gate ≤ 60″ |
|---|---|---|---|
| sun | 1.3″ / 7.3″ | 1.3″ / 7.2″ | PASS (8×) |
| moon | 20.7″ / **105.5″** | 12.2″ / 85.6″ | **PARTIAL — tier table adjustment** |
| venus | 2.8″ / 20.0″ | 2.8″ / 19.8″ | PASS (3×) |
| mars | 2.4″ / 16.1″ | 2.4″ / 16.0″ | PASS |
| jupiter | 4.5″ / 14.1″ | 4.5″ / 14.1″ | PASS |
| saturn | 9.7″ / 22.5″ | 9.7″ / 22.5″ | PASS |

**Tier-table change required**: consumer-tier Moon accuracy is mean 21″ / max 106″ over 1900–2100 (the engine's truncated Moon model drifts at the range edges). Options for Phase 2: restrict the consumer valid range for the Moon (e.g., 1950–2050), or document the 106″ ceiling. Sun/planets comfortably meet the 60″ gate; several bodies are close to the advanced 10″ tier but do not meet it (consistent with the plan: advanced tier rides SPICE).

**Branch B — NAIF integrity: CLOSED**

- `de440s.bsp` downloaded from NAIF's own server: sha256 `c1c7feea…` — **exact match** with the S0.3 dual-mirror hash → kernel integrity fully verified
- Official `naif0012.tls` (5,257 B), `pck00010.tpc` (126,143 B), `earth_assoc_itrf93.tf` (7,522 B) → `/data/kernels`
- TEME/TOD FK provenance finding recorded (finding 6)

## Gate verdicts

- **G1 (deploy pattern)**: PASS — deploy → probes green → redeploy → rollback → shared volume → TLS, all verified live
- **G2 (connectivity)**: PASS — host reaches all data sources incl. JPL + NAIF
- **G3 (Branch A)**: PASS with tier-table adjustment — S0.2 position gate closed on real Horizons data (Sun/planets ≤ 23″ max)
- **G3 (Branch B)**: PASS — kernel integrity verified against NAIF; official kernels downloaded
- **Deferred (recorded)**: SPICE ≤ 1″ vs Horizons host gate + official-CSPICE-build thread-safety retest (need gcc/apt on the host = one slow build; precision already bounded by S0.3 A2: CSPICE ≡ skyfield at 0.003″ on the same kernel). Next-session item.

## Decisions feeding

- §19 deployment (final): Coolify project/app pattern, host-path shared volume, no-apt images, health-off + external monitoring, deploy-queue recovery pattern
- ADR 21 (HTTP caching) n/a; accuracy tier table: Moon consumer range/ceiling adjustment
- Phase 1: api/worker skeletons in `src/` are the Phase 1 base; worker = operational tooling surface (scheduled tasks, no public endpoints)
- Phase 2: engine RA-in-hours conversion recorded; Moon model range limits
- Phase 4: FK artifact list (teme.tf from CelesTrak; earth_assoc_itrf93.tf from NAIF)

## Open items

- SPICE ≤ 1″ vs Horizons host gate + official-build thread retest (next session)
- Volume-permission strategy (host chown) for a rootless worker
- uptime-kuma monitor for astronomy.aursand.no/healthz
