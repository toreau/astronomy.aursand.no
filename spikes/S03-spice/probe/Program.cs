using System.Globalization;
using SpiceProbe;

Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

if (args.Length < 2) { Console.WriteLine("usage: Probe <mode> <kernels...> | Probe crosscheck <csv> <kernels...> | Probe erfa <csv> <kernels...>"); return 1; }

var mode = args[0];
return mode switch
{
    "run" => RunFunctional(args[1..]),
    "crosscheck" => RunCrosscheck(args[1], args[2..]),
    "erfa" => RunErfa(args[1], args[2..]),
    _ => 1,
};

static int LoadKernels(string[] kernels)
{
    foreach (var kernel in kernels)
    {
        CSpice.Furnsh(kernel);
        if (CSpice.Failed() != 0)
        {
            Console.WriteLine($"FATAL: furnsh failed for {kernel}: {ErrMsg()}");
            return 1;
        }
        Console.WriteLine($"  loaded {kernel}");
    }
    return 0;
}

static int RunFunctional(string[] kernels)
{
    var checks = 0;
    var failures = 0;

    void Check(string label, bool ok, string detail)
    {
        checks++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label}: {detail}");
        if (!ok) failures++;
    }

    if (LoadKernels(kernels) != 0) return 1;

    CSpice.Utc2Et("2000-01-01 12:00:00 UTC", out var etJ2000);
    Check("utc2et(J2000)", CSpice.Failed() == 0, $"et={etJ2000:F6}");

    CSpice.Deltet(etJ2000, "ET", out var ttMinusEt);
    CSpice.Deltet(etJ2000, "UTC", out var ttMinusUtc);
    Console.WriteLine($"INFO  deltet ET @2000 = {ttMinusEt * 1000:F1} ms (SPICE ET/TDB semantics to verify against docs; not a gate)");
    Check("deltet UTC (TT-UTC @2000 = 64.184)", Math.Abs(ttMinusUtc - 64.184) < 0.001, $"TT-UTC = {ttMinusUtc:F4} s");

    var etNow = etJ2000 + 8294400.0;
    CSpice.Deltet(etNow, "UTC", out var ttMinusUtc96);
    Console.WriteLine($"INFO  TT-UTC @+96d = {ttMinusUtc96:F4} s (expect 64.184)");

    var iso = Et2UtcStr(etNow);
    Check("et2utc round-trip", CSpice.Failed() == 0, iso);

    foreach (var (body, refFrame, abcorr) in new[]
    {
        ("SUN", "J2000", "NONE"), ("SUN", "J2000", "LT"),
        ("MOON", "J2000", "NONE"), ("MOON", "J2000", "LT"),
    })
    {
        var pos = new double[3];
        CSpice.SpkPos(body, etJ2000, refFrame, abcorr, "EARTH", pos, out var lt);
        if (CSpice.Failed() != 0) { Console.WriteLine($"FAIL spkpos {body}: {ErrMsg()}"); failures++; checks++; continue; }
        CSpice.RecRad(pos, out var range, out var raRad, out var decRad);
        Console.WriteLine($"  spkpos({body}, {refFrame}, {abcorr}) @J2000: RA={raRad * 180 / Math.PI:F6} deg, Dec={decRad * 180 / Math.PI:F6} deg, r={range:F6} km, lt={lt:F6} s");
        checks++;
    }

    var sun = new double[3];
    CSpice.SpkPos("SUN", etJ2000, "J2000", "LT", "EARTH", sun, out _);
    var rot = new double[9];
    CSpice.PxForm("J2000", "ECLIPJ2000", etJ2000, rot);
    if (CSpice.Failed() != 0)
    {
        Console.WriteLine($"FAIL pxform J2000->ECLIPJ2000: {ErrMsg()}");
        failures++;
        checks++;
    }
    else
    {
        var sunEcl = new double[3];
        for (var i = 0; i < 3; i++)
            sunEcl[i] = rot[i * 3] * sun[0] + rot[i * 3 + 1] * sun[1] + rot[i * 3 + 2] * sun[2];
        CSpice.RecRad(sunEcl, out var r2, out var ra2, out var dec2);
        Console.WriteLine($"  Sun J2000->ECLIPJ2000 @J2000: RA={ra2 * 180 / Math.PI:F6} deg, Dec={dec2 * 180 / Math.PI:F6} deg, r={r2:F6} km");
        checks++;
    }

    var basePos = new double[3];
    CSpice.SpkPos("MOON", etNow, "J2000", "NONE", "EARTH", basePos, out _);
    var maxDev = 0.0;
    Console.WriteLine("  spkpos serial baseline (1600 calls)...");
    for (var i = 0; i < 1600; i++)
    {
        var p = new double[3];
        CSpice.SpkPos("MOON", etNow + i, "J2000", "NONE", "EARTH", p, out _);
        maxDev = Math.Max(maxDev, Math.Abs(p[0] - basePos[0]));
    }
    Console.WriteLine($"  serial ok, drift from base = {maxDev:F6} km (moon moves ~1 km/min, expected)");

    var maxDevLocked = 0.0;
    var lockObj = new object();
    Console.WriteLine("  parallel spkpos with global lock (8 threads x 200)...");
    try
    {
        Parallel.For(0, 8, _ =>
        {
            var p = new double[3];
            for (var i = 0; i < 200; i++)
            {
                lock (lockObj)
                {
                    CSpice.SpkPos("MOON", etNow + i, "J2000", "NONE", "EARTH", p, out double _lt);
                }
                var dev = Math.Sqrt((p[0] - basePos[0]) * (p[0] - basePos[0]) + (p[1] - basePos[1]) * (p[1] - basePos[1]) + (p[2] - basePos[2]) * (p[2] - basePos[2]));
                Max(ref maxDevLocked, dev);
            }
        });
        Check("parallel spkpos under lock", CSpice.Failed() == 0, $"max deviation {maxDevLocked:E2} km, no SPICE errors");
    }
    catch (Exception ex)
    {
        Check("parallel spkpos under lock", false, ex.Message);
    }

    Console.WriteLine($"\n{checks - failures}/{checks} checks passed");
    return failures == 0 ? 0 : 1;
}

static int RunCrosscheck(string csvPath, string[] kernels)
{
    if (LoadKernels(kernels) != 0) return 1;
    var rows = ReadRefCsv(csvPath);
    var stats = new Dictionary<string, (int N, double MaxArcsec, double SumArcsec, double MaxDistRel, double SumDistRel)>();
    var aberrationProbeDone = false;

    foreach (var (body, utc, _, _, raDeg, decDeg, distKm) in rows)
    {
        CSpice.Utc2Et(utc, out var et);
        var pos = new double[3];
        CSpice.SpkPos(SpiceBody(body), et, "J2000", "LT", "EARTH", pos, out _);
        if (CSpice.Failed() != 0) { Console.WriteLine($"FAIL spkpos {body} @{utc}: {ErrMsg()}"); return 1; }
        CSpice.RecRad(pos, out var range, out var raRad, out var decRad);
        var sep = SeparationArcsec(raRad * 180 / Math.PI, decRad * 180 / Math.PI, raDeg, decDeg);
        var distRel = Math.Abs(range - distKm) / distKm;
        var s = stats.GetValueOrDefault(body);
        stats[body] = (s.N + 1, Math.Max(s.MaxArcsec, sep), s.SumArcsec + sep, Math.Max(s.MaxDistRel, distRel), s.SumDistRel + distRel);

        if (!aberrationProbeDone && body == "sun")
        {
            var posApp = new double[3];
            CSpice.SpkPos("SUN", et, "J2000", "LT+S", "EARTH", posApp, out _);
            CSpice.RecRad(posApp, out _, out var raApp, out var decApp);
            var ab = SeparationArcsec(raApp * 180 / Math.PI, decApp * 180 / Math.PI, raDeg, decDeg);
            Console.WriteLine($"INFO  Sun aberration (LT+S vs astrometric @{utc}): {ab:F2} arcsec (expect ~20.5)");
            aberrationProbeDone = true;
        }
    }

    Console.WriteLine($"\nCSPICE (LT, J2000) vs skyfield astrometric (ICRS), {rows.Count} rows:");
    foreach (var (body, s) in stats.OrderBy(kv => kv.Key))
        Console.WriteLine($"  {body,-8} N={s.N,4} mean={s.SumArcsec / s.N,8:F4}\" max={s.MaxArcsec,8:F4}\" | dist rel max={s.MaxDistRel:E2}");
    return 0;
}

static int RunErfa(string csvPath, string[] kernels)
{
    if (LoadKernels(kernels) != 0) return 1;
    var rows = ReadRefCsv(csvPath);
    var maxSep = 0.0;
    var maxTdbDiff = 0.0;
    var n = 0;
    var nTdb = 0;
    var pvh = new double[3];
    var pvb = new double[3];

    foreach (var (body, utc, ttJd, tdbMinusTt, _, _, _) in rows)
    {
        if (body != "sun") continue;
        var date1 = Math.Floor(ttJd - 0.5) + 0.5;
        var date2 = ttJd - date1;
        var rc = Erfa.Epv00(date1, date2, pvh, pvb);
        if (rc != 0) { Console.WriteLine($"FAIL eraEpv00 @{utc}"); return 1; }
        var sunGeo = new[] { -pvh[0], -pvh[1], -pvh[2] };
        var ra = Math.Atan2(sunGeo[1], sunGeo[0]) * 180 / Math.PI;
        var dec = Math.Asin(sunGeo[2] / Math.Sqrt(sunGeo[0] * sunGeo[0] + sunGeo[1] * sunGeo[1] + sunGeo[2] * sunGeo[2])) * 180 / Math.PI;
        if (ra < 0) ra += 360;

        CSpice.Utc2Et(utc, out var et);
        var pos = new double[3];
        CSpice.SpkPos("SUN", et, "J2000", "NONE", "EARTH", pos, out _);
        CSpice.RecRad(pos, out _, out var raRad, out var decRad);
        maxSep = Math.Max(maxSep, SeparationArcsec(raRad * 180 / Math.PI, decRad * 180 / Math.PI, ra, dec));
        n++;

        var ut = ttJd + 0.5 - Math.Floor(ttJd + 0.5);
        var dtdb = Erfa.Dtdb(date1, date2, ut, 0.0, 0.0, 0.0);
        maxTdbDiff = Math.Max(maxTdbDiff, Math.Abs(dtdb - tdbMinusTt));
        nTdb++;
    }

    Console.WriteLine($"\nERFA eraEpv00 (geocentric Sun, geometric) vs CSPICE spkpos NONE: N={n} max sep={maxSep:F6} arcsec (gate <= 1\")");
    Console.WriteLine($"ERFA eraDtdb vs skyfield TDB-TT: N={nTdb} max |diff|={maxTdbDiff * 1e6:F2} us");
    return maxSep <= 1.0 ? 0 : 1;
}

static List<(string Body, string Utc, double TtJd, double TdbMinusTt, double RaDeg, double DecDeg, double DistKm)> ReadRefCsv(string path)
{
    var rows = new List<(string, string, double, double, double, double, double)>();
    foreach (var line in File.ReadAllLines(path).Skip(1))
    {
        var p = line.Split(',');
        rows.Add((p[0], p[1], double.Parse(p[2], CultureInfo.InvariantCulture), double.Parse(p[3], CultureInfo.InvariantCulture),
            double.Parse(p[4], CultureInfo.InvariantCulture), double.Parse(p[5], CultureInfo.InvariantCulture), double.Parse(p[6], CultureInfo.InvariantCulture)));
    }
    return rows;
}

static string SpiceBody(string body) => body switch
{
    "mars" => "MARS BARYCENTER",
    "jupiter" => "JUPITER BARYCENTER",
    "saturn" => "SATURN BARYCENTER",
    _ => body.ToUpperInvariant(),
};

static double SeparationArcsec(double ra1, double dec1, double ra2, double dec2)
{
    var (r1, d1, r2, d2) = (ra1 * Math.PI / 180, dec1 * Math.PI / 180, ra2 * Math.PI / 180, dec2 * Math.PI / 180);
    var cosSep = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(r1 - r2);
    return Math.Acos(Math.Clamp(cosSep, -1.0, 1.0)) * 180 / Math.PI * 3600.0;
}

static void Max(ref double target, double value)
{
    double current;
    do { current = target; } while (value > current && Interlocked.CompareExchange(ref target, value, current) != current);
}

static string Et2UtcStr(double et)
{
    var buf = new byte[64];
    CSpice.Et2Utc(et, "C", 3, 64, buf);
    return System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
}

static string ErrMsg()
{
    var buf = new byte[1024];
    CSpice.GetMsg("SHORT", 1024, buf);
    CSpice.Reset();
    return System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
}
