using System.Diagnostics;
using SpiceProbe;

Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

if (args.Length < 1) { Console.WriteLine("usage: Probe <de440s.bsp path>"); return 1; }

var checks = 0;
var failures = 0;

void Check(string label, bool ok, string detail)
{
    checks++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label}: {detail}");
    if (!ok) failures++;
}

string ErrMsg()
{
    var buf = new byte[1024];
    CSpice.GetMsg("SHORT", 1024, buf);
    CSpice.Reset();
    return System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
}

foreach (var kernel in args)
{
    CSpice.Furnsh(kernel);
    if (CSpice.Failed() != 0)
    {
        Console.WriteLine($"FATAL: furnsh failed for {kernel}: {ErrMsg()}");
        return 1;
    }
    Console.WriteLine($"  loaded {kernel}");
}


CSpice.Utc2Et("2000-01-01 12:00:00 UTC", out var etJ2000);
Check("utc2et(J2000)", CSpice.Failed() == 0, $"et={etJ2000:F6}");

CSpice.Deltet(etJ2000, "ET", out var ttMinusEt);
CSpice.Deltet(etJ2000, "UTC", out var ttMinusUtc);
Console.WriteLine($"INFO  deltet ET @2000 = {ttMinusEt * 1000:F1} ms (SPICE ET/TDB semantics to verify against docs; not a gate)");
Check("deltet UTC (TT-UTC @2000 = 64.184)", Math.Abs(ttMinusUtc - 64.184) < 0.001, $"TT-UTC = {ttMinusUtc:F4} s");

var etNow = etJ2000 + 8294400.0; // +96 days
CSpice.Deltet(etNow, "UTC", out var ttMinusUtc96);
Console.WriteLine($"INFO  TT-UTC @+96d = {ttMinusUtc96:F4} s (expect 64.184)");

var iso = Et2UtcStr(etNow);
Check("et2utc round-trip", CSpice.Failed() == 0, iso);

foreach (var (body, refFrame, abcorr) in new[]
{
    ("SUN", "J2000", "NONE"),
    ("SUN", "J2000", "LT"),
    ("MOON", "J2000", "NONE"),
    ("MOON", "J2000", "LT"),
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
foreach (var targetFrame in new[] { "ECLIPJ2000" })
{
    var rot = new double[9];
    CSpice.PxForm("J2000", targetFrame, etJ2000, rot);
    if (CSpice.Failed() != 0)
    {
        Console.WriteLine($"FAIL pxform J2000->{targetFrame}: {ErrMsg()}");
        failures++;
        checks++;
        continue;
    }
    var sunFrame = new double[3];
    for (var i = 0; i < 3; i++)
        sunFrame[i] = rot[i * 3] * sun[0] + rot[i * 3 + 1] * sun[1] + rot[i * 3 + 2] * sun[2];
    CSpice.RecRad(sunFrame, out var r2, out var ra2, out var dec2);
    Console.WriteLine($"  Sun J2000->{targetFrame} @J2000: RA={ra2 * 180 / Math.PI:F6} deg, Dec={dec2 * 180 / Math.PI:F6} deg, r={r2:F6} km");
    checks++;
}

var basePos = new double[3];
CSpice.SpkPos("MOON", etNow, "J2000", "NONE", "EARTH", basePos, out _);
var maxDev = 0.0;
var lockObj = new object();
Console.WriteLine("  spkpos serial baseline (1600 calls)...");
for (var i = 0; i < 1600; i++)
{
    var p = new double[3];
    CSpice.SpkPos("MOON", etNow + i, "J2000", "NONE", "EARTH", p, out _);
    maxDev = Math.Max(maxDev, Math.Abs(p[0] - basePos[0]));
}
Console.WriteLine($"  serial ok, drift from base = {maxDev:F6} km (moon moves ~1 km/min, expected)");

var maxDevLocked = 0.0;
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
            InterlockedEx.Max(ref maxDevLocked, dev);
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

static string Et2UtcStr(double et)
{
    var buf = new byte[64];
    CSpice.Et2Utc(et, "C", 3, 64, buf);
    return System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
}

static class InterlockedEx
{
    public static void Max(ref double target, double value)
    {
        double current;
        do { current = target; } while (value > current && Interlocked.CompareExchange(ref target, value, current) != current);
    }
}
