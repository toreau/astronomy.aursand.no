import json, sys

sys.path.insert(0, ".")
from liveverify import api_get, API
import requests

results = []


def check(name, ok, detail=""):
    results.append((name, "PASS" if ok else "FAIL", detail))
    print(f"{'PASS' if ok else 'FAIL'}  {name}  {detail}")


# --- baseline ---
h = api_get("/health/ready", wait=0)
check(
    "health/ready 200 + ready",
    h.get("status") == "ready" and h.get("db") == "ok",
    json.dumps(
        {k: h.get(k) for k in ("db", "kernels", "starCatalog", "satelliteElements")}
    ),
)

s = api_get("/api/v1/satellites/status")
check(
    "satellites/status shape",
    "activeVersion" in s and "elementCount" in s,
    f"active={s['activeVersion']} count={s['elementCount']} fresh={s['fresh']} warn={s['warn']}",
)


# --- contract sweep: valid + invalid ---
def status_of(path, params):
    r = requests.get(API + path, params=params, timeout=60)
    return r.status_code, r.text[:200]


valid = [
    ("/api/v1/time/julian-date", {"time": "2026-08-15T12:00:00Z"}),
    ("/api/v1/time/time-scales", {"time": "2026-08-15T12:00:00Z"}),
    ("/api/v1/calendars/convert", {"date": "2026-08-15", "timezone": "Europe/Oslo"}),
    ("/api/v1/calendars/date-arithmetic", {"date": "2026-08-15", "days": 7}),
    ("/api/v1/calendars/range", {"from": "2026-08-01", "to": "2026-08-31"}),
    (
        "/api/v1/ephemeris/sun/position",
        {"time": "2026-08-15T12:00:00Z", "precision": "consumer"},
    ),
    (
        "/api/v1/ephemeris/sun/position",
        {"time": "2026-08-15T12:00:00Z", "precision": "reference"},
    ),
    (
        "/api/v1/ephemeris/sun/rise-set",
        {"date": "2026-08-15", "latitude": 59.9, "longitude": 10.7},
    ),
    (
        "/api/v1/ephemeris/twilight",
        {"date": "2026-08-15", "latitude": 59.9, "longitude": 10.7, "type": "civil"},
    ),
    (
        "/api/v1/ephemeris/moon/phases",
        {"from": "2026-08-01T00:00:00Z", "to": "2026-09-01T00:00:00Z"},
    ),
    (
        "/api/v1/ephemeris/jupiter/visibility",
        {"time": "2026-08-15T12:00:00Z", "latitude": 59.9, "longitude": 10.7},
    ),
    (
        "/api/v1/ephemeris/events",
        {
            "from": "2026-08-01T00:00:00Z",
            "to": "2026-09-01T00:00:00Z",
            "bodies": "venus",
            "types": "max-elongation",
        },
    ),
    ("/api/v1/stars/name", {"name": "sirius"}),
    ("/api/v1/stars/brightest", {"limit": 5}),
    (
        "/api/v1/stars/32349/position",
        {
            "time": "2026-08-15T12:00:00Z",
            "frame": "icrs",
            "positionType": "astrometric",
        },
    ),
    (
        "/api/v1/stars/32349/rise-set",
        {"date": "2026-08-15", "latitude": 59.9, "longitude": 10.7},
    ),
    (
        "/api/v1/satellites/25544/position",
        {"time": "2026-08-15T12:00:00Z", "latitude": 59.9, "longitude": 10.7},
    ),
    (
        "/api/v1/satellites/25544/passes",
        {"date": "2026-08-15", "latitude": 59.9, "longitude": 10.7},
    ),
    ("/api/v1/satellites/search", {"name": "iss"}),
    (
        "/api/v1/almanac/daily",
        {"date": "2026-08-15", "latitude": 59.9, "longitude": 10.7},
    ),
    (
        "/api/v1/almanac/monthly",
        {"month": "2026-08", "latitude": 59.9, "longitude": 10.7},
    ),
    ("/api/v1/almanac/monthly", {"year": 2026, "latitude": 59.9, "longitude": 10.7}),
]
invalid = [
    ("/api/v1/time/julian-date", {"time": "not-a-time"}),
    ("/api/v1/calendars/convert", {"date": "2026-02-31"}),
    ("/api/v1/calendars/range", {"from": "2026-01-01", "to": "2027-01-02"}),
    ("/api/v1/ephemeris/nope/position", {"time": "2026-08-15T12:00:00Z"}),
    (
        "/api/v1/ephemeris/sun/position",
        {"time": "2026-08-15T12:00:00Z", "frame": "horizontal"},
    ),
    (
        "/api/v1/ephemeris/sun/position",
        {"time": "2026-08-15T12:00:00Z", "positionType": "geometric"},
    ),
    ("/api/v1/almanac/monthly", {"latitude": 59.9, "longitude": 10.7}),
    (
        "/api/v1/almanac/monthly",
        {"month": "2026-08", "year": 2026, "latitude": 59.9, "longitude": 10.7},
    ),
]

for path, params in valid:
    code, _ = status_of(path, params)
    check(f"valid {path}", code == 200, f"status={code}")
for path, params in invalid:
    code, body = status_of(path, params)
    check(f"invalid {path}", code == 400, f"status={code} (expected 400)")
    if code == 400 and "AST-4001" not in body:
        check(f"invalid {path} AST-4001", False, "missing AST-4001")

# satellite 503 for unknown norad is a 400 (unknown satellite); star unknown hip 400
code, _ = status_of("/api/v1/stars/99999/position", {"time": "2026-08-15T12:00:00Z"})
check("stars unknown hip -> 400", code == 400, f"status={code}")

fails = [r for r in results if r[1] == "FAIL"]
print(f"\n{len(results) - len(fails)}/{len(results)} passed")
sys.exit(1 if fails else 0)
