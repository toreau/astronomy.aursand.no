import datetime, json, math, sys

sys.path.insert(0, ".")
from liveverify import api_get

import requests

results = []


def rec(name, ok, delta, note=""):
    results.append({"check": name, "pass": ok, "delta": delta, "note": note})
    print(f"{'PASS' if ok else 'FAIL'}  {name:<58} delta={delta} {note}")


# ---- fetch current ISS TLE (independent source: CelesTrak TLE format) ----
r = requests.get(
    "https://celestrak.org/NORAD/elements/gp.php",
    params={"CATNR": "25544", "FORMAT": "tle"},
    timeout=60,
)
lines = [l.strip() for l in r.text.splitlines() if l.strip()]
line1 = next(l for l in lines if l.startswith("1 "))
line2 = next(l for l in lines if l.startswith("2 "))

from sgp4.api import Satrec, jday

sat = Satrec.twoline2rv(line1, line2)


def propagate(utc):
    jd = (
        2440587.5
        + (
            utc - datetime.datetime(1970, 1, 1, tzinfo=datetime.timezone.utc)
        ).total_seconds()
        / 86400.0
    )
    jdi, fr = divmod(jd, 1.0)
    e, r, v = sat.sgp4(jdi, fr)
    return r  # TEME km (WGS-72)


def gmst_deg(jd):
    t = (jd - 2451545.0) / 36525.0
    g = (
        280.46061837
        + 360.98564736629 * (jd - 2451545.0)
        + 0.000387933 * t * t
        - t * t * t / 38710000.0
    )
    return g % 360.0


FLAT = 1.0 / 298.26
RE = 6378.135


def geodetic_to_ecef(lat, lon, alt_km):
    e2 = FLAT * (2.0 - FLAT)
    sin = math.sin(math.radians(lat))
    n = RE / math.sqrt(1.0 - e2 * sin * sin)
    return (
        (n + alt_km) * math.cos(math.radians(lat)) * math.cos(math.radians(lon)),
        (n + alt_km) * math.cos(math.radians(lat)) * math.sin(math.radians(lon)),
        (n * (1.0 - e2) + alt_km) * sin,
    )


def topocentric(r_teme, jd, obs_lat, obs_lon, obs_alt_km):
    th = math.radians(gmst_deg(jd))
    ct, st = math.cos(th), math.sin(th)
    pef = (r_teme[0] * ct + r_teme[1] * st, -r_teme[0] * st + r_teme[1] * ct, r_teme[2])
    ox, oy, oz = geodetic_to_ecef(obs_lat, obs_lon, obs_alt_km)
    tx, ty, tz = pef[0] - ox, pef[1] - oy, pef[2] - oz
    rng = math.sqrt(tx * tx + ty * ty + tz * tz)
    lat, lon = math.radians(obs_lat), math.radians(obs_lon)
    ex, ey, ez = -math.sin(lon), math.cos(lon), 0.0
    nx, ny, nz = (
        -math.sin(lat) * math.cos(lon),
        -math.sin(lat) * math.sin(lon),
        math.cos(lat),
    )
    px, py, pz = (
        math.cos(lat) * math.cos(lon),
        math.cos(lat) * math.sin(lon),
        math.sin(lat),
    )
    alt = math.degrees(math.asin(max(-1, min(1, (tx * px + ty * py + tz * pz) / rng))))
    az = math.degrees(
        math.atan2(
            (tx * ex + ty * ey + tz * ez) / rng, (tx * nx + ty * ny + tz * nz) / rng
        )
    )
    if az < 0:
        az += 360
    return alt, az, rng


# ---- position comparison (4 instants) ----
for tstr in [
    "2026-08-15T12:00:00Z",
    "2026-08-15T20:00:00Z",
    "2026-08-16T02:00:00Z",
    "2026-08-16T10:00:00Z",
]:
    utc = datetime.datetime.fromisoformat(tstr.replace("Z", "+00:00"))
    jd = (
        2440587.5
        + (
            utc - datetime.datetime(1970, 1, 1, tzinfo=datetime.timezone.utc)
        ).total_seconds()
        / 86400.0
    )
    alt_h, az_h, rng_h = topocentric(propagate(utc), jd, 59.9, 10.7, 0.0)
    api = api_get(
        "/api/v1/satellites/25544/position",
        {"time": tstr, "latitude": 59.9, "longitude": 10.7},
    )
    dalt = abs(api["altitudeDeg"] - alt_h)
    daz = min(abs(api["azimuthDeg"] - az_h), 360 - abs(api["azimuthDeg"] - az_h))
    drng = abs(api["rangeKm"] - rng_h)
    ok = dalt < 0.5 and daz < 0.5 and drng < 20
    rec(
        f"sat position {tstr}",
        ok,
        f"altΔ={dalt:.3f}° azΔ={daz:.3f}° rangeΔ={drng:.1f}km (api {api['altitudeDeg']:.2f}/{api['azimuthDeg']:.2f} h {alt_h:.2f}/{az_h:.2f})",
    )


# ---- passes: recompute with python sgp4 and compare to the API's pass list ----
def predict_passes(t0, t1, step_s=30, min_el=10.0):
    t = t0
    crossing = []
    prev_alt = None
    prev_above = None
    while t < t1:
        jd = (
            2440587.5
            + (
                t - datetime.datetime(1970, 1, 1, tzinfo=datetime.timezone.utc)
            ).total_seconds()
            / 86400.0
        )
        alt, _, _ = topocentric(propagate(t), jd, 59.9, 10.7, 0.0)
        above = alt >= min_el
        if prev_above is not None and above != prev_above:
            crossing.append((t, above))
        prev_alt, prev_above = alt, above
        t += datetime.timedelta(seconds=step_s)
    passes = []
    for i, (ct, rising) in enumerate(crossing):
        if not rising:
            continue
        sett = next((x[0] for x in crossing[i + 1 :] if not x[1]), None)
        if sett is None:
            sett = t1
        passes.append((ct, sett))
    return passes


t0 = datetime.datetime(2026, 8, 15, 0, 0, tzinfo=datetime.timezone.utc)
t1 = t0 + datetime.timedelta(hours=24)
sf_passes = predict_passes(t0, t1)
api_passes = api_get(
    "/api/v1/satellites/25544/passes",
    {
        "from": "2026-08-15T00:00:00Z",
        "to": "2026-08-16T00:00:00Z",
        "latitude": 59.9,
        "longitude": 10.7,
        "minElevation": 10,
        "stepSeconds": 30,
    },
)
ap = [(p["riseUtc"], p["setUtc"]) for p in api_passes["passes"]]
matched = 0
for sr, ss in sf_passes:
    for ar, as_ in ap:
        if (
            abs(
                (
                    datetime.datetime.fromisoformat(ar.replace("Z", "+00:00")) - sr
                ).total_seconds()
            )
            < 300
        ):
            matched += 1
            break
rec(
    f"sat passes 24h (sgp4 {len(sf_passes)} vs api {len(ap)})",
    len(sf_passes) == len(ap) and matched == len(sf_passes),
    f"sgp4={len(sf_passes)} api={len(ap)} matched={matched}",
)

# ---- search + status consistency ----
s = api_get("/api/v1/satellites/search", {"name": "iss"})
rec(
    "satellites/search iss",
    any(r["noradId"] == "25544" for r in s),
    f"{len(s)} results",
)

r = requests.get(
    "https://celestrak.org/NORAD/elements/gp.php",
    params={"GROUP": "stations", "FORMAT": "omm"},
    timeout=60,
)
count = len(
    [
        l
        for l in r.text.splitlines()
        if l.strip() and not l.startswith("OBJECT_NAME") and l.count(",") > 15
    ]
)
st = api_get("/api/v1/satellites/status")
rec(
    "satellites/status count vs CelesTrak",
    abs(st["elementCount"] - count) <= 1,
    f"api={st['elementCount']} celestrak={count}",
)

json.dump(results, open("results_satellites.json", "w"), indent=1)
fails = [r for r in results if not r["pass"]]
print(f"\n{len(results) - len(fails)}/{len(results)} passed, {len(fails)} deviations")
for r in fails:
    print("  DEVIATION:", r["check"], "|", r["delta"])
sys.exit(1 if fails else 0)
