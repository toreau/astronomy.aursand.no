import json, os, time, urllib.parse

import os as _os
API = _os.environ.get("ASTRONOMY_API_BASE", "https://astronomy.aursand.no")
CACHE = os.path.join(os.path.dirname(__file__), ".cache")
os.makedirs(CACHE, exist_ok=True)

GRID = {
    "epochs": [
        "2000-01-01T12:00:00Z",
        "2026-08-05T00:00:00Z",
        "2026-08-15T12:00:00Z",
        "2027-01-01T00:00:00Z",
        "1900-06-01T12:00:00Z",
    ],
    "locations": [
        ("oslo", 59.9, 10.7, 0),
        ("tromso", 69.65, 18.96, 0),
        ("singapore", 1.35, 103.82, 0),
        ("sydney", -33.87, 151.21, 0),
    ],
    "bodies": [
        "sun",
        "moon",
        "mercury",
        "venus",
        "mars",
        "jupiter",
        "saturn",
        "uranus",
        "neptune",
    ],
    "horizons_ids": {
        "sun": "10",
        "moon": "301",
        "mercury": "199",
        "venus": "299",
        "mars": "499",
        "jupiter": "599",
        "saturn": "699",
        "uranus": "799",
        "neptune": "899",
    },
}

import requests


def api_get(path, params=None, wait=0.15):
    q = urllib.parse.urlencode(params or {})
    key = path.replace("/", "_") + ("?" + q if q else "")
    safe = "".join(c if c.isalnum() or c in "-_." else "_" for c in key)
    cache_file = os.path.join(CACHE, safe + ".json")
    if os.path.exists(cache_file):
        return json.load(open(cache_file))
    time.sleep(wait)
    r = requests.get(API + path, params=params, timeout=60)
    r.raise_for_status()
    data = r.json()
    json.dump(data, open(cache_file, "w"))
    return data


def horizons(params, wait=1.2):
    """Fetch a JPL Horizons text table; returns the raw text."""
    import hashlib
    key = "h_" + hashlib.sha1("&".join(f"{k}={v}" for k, v in sorted(params.items())).encode()).hexdigest()[:16]
    cache_file = os.path.join(CACHE, key + ".txt")
    if os.path.exists(cache_file):
        return open(cache_file).read()
    time.sleep(wait)
    r = requests.get(
        "https://ssd.jpl.nasa.gov/api/horizons.api", params=params, timeout=90
    )
    r.raise_for_status()
    text = r.text
    open(cache_file, "w").write(text)
    return text
