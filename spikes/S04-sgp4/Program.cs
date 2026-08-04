using System.Diagnostics;
using System.Globalization;
using S04Sgp4;

var fixtureDir = args.Length > 1 ? args[1] : "fixtures";
var mode = args.Length > 0 ? args[0] : "vectors";

switch (mode)
{
    case "vectors":
        RunVectors(fixtureDir);
        break;
    case "perf":
        RunPerf(fixtureDir);
        break;
    case "threads":
        RunThreads(fixtureDir);
        break;
    case "deep":
        RunDeep(args[1]);
        break;
    case "deep2":
        RunDeep2(args[1]);
        break;
    case "omm":
        RunOmm();
        break;
    case "dump":
        RunDump();
        break;
    default:
        Console.WriteLine("usage: S04Sgp4 <vectors|perf|threads> [fixtureDir]");
        return 1;
}
return 0;

static void RunVectors(string fixtureDir)
{
    var cases = Fixtures.Load(fixtureDir);
    var propagators = new IPropagator[] { new SgpNetAdapter(), new OneSgp4Adapter() };

    Console.WriteLine($"Vallado verification: {cases.Count} cases, tcppver.out rows from SGP4-VER.TLE");
        foreach (var prop in propagators)
        {
            var errors = new List<double>();
            var deepErrors = new List<double>();
            var deepCases = 0;
            var failedCases = 0;
            foreach (var c in cases)
            {
                try
                {
                    prop.Init(c.Line1, c.Line2);
                    var isDeep = Fixtures.MeanMotion(c) < 6.0;
                    if (isDeep) deepCases++;
                    foreach (var (min, x, y, z) in c.Rows)
                    {
                        var (px, py, pz) = prop.PositionAt(min);
                        var err = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z));
                        errors.Add(err);
                        if (isDeep) deepErrors.Add(err);
                    }
                }
                catch (Exception ex)
                {
                    failedCases++;
                    Console.WriteLine($"  [fail] {prop.Name} case {c.Id}: {ex.Message}");
                }
            }
            var summary = errors.Count == 0
                ? "no valid rows"
                : $"N={errors.Count} mean={errors.Average():F4} km p95={P95(errors):F4} km max={errors.Max():F4} km";
            var deepSummary = deepErrors.Count == 0
                ? "n/a"
                : $"mean={deepErrors.Average():F4} km max={deepErrors.Max():F4} km";
            Console.WriteLine($"{prop.Name}: {summary} | deep-space ({deepCases} cases): {deepSummary} | failed cases: {failedCases}");
        }

    Console.WriteLine("\nper-case max errors:");
    foreach (var prop in new IPropagator[] { new SgpNetAdapter(), new OneSgp4Adapter() })
    {
        Console.WriteLine($"  {prop.Name}:");
        foreach (var c in cases)
        {
            try
            {
                prop.Init(c.Line1, c.Line2);
                var max = 0.0;
                foreach (var (min, x, y, z) in c.Rows)
                {
                    var (px, py, pz) = prop.PositionAt(min);
                    max = Math.Max(max, Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z)));
                }
                Console.WriteLine($"    case {c.Id,5} (mm={Fixtures.MeanMotion(c),6:F3} rev/d): max {max,10:F4} km");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    case {c.Id,5} (mm={Fixtures.MeanMotion(c),6:F3} rev/d): FAILED - {ex.Message}");
            }
        }
    }
}

static void RunPerf(string fixtureDir)
{
    var cases = Fixtures.Load(fixtureDir);
    var iss = cases.FirstOrDefault(c => c.Id == "25544" || c.Line1.Contains("25544")) ?? cases[0];
    if (iss == cases[0] && !cases[0].Line1.Contains("25544")) Console.WriteLine("no ISS case in vectors; using case 0");

    foreach (var prop in new IPropagator[] { new SgpNetAdapter(), new OneSgp4Adapter() })
    {
        prop.Init(iss.Line1, iss.Line2);
        var sw = Stopwatch.StartNew();
        double sink = 0;
        for (var i = 0; i < 60000; i++)
        {
            var (x, y, z) = prop.PositionAt(i * 0.01);
            sink += x + y + z;
        }
        sw.Stop();
        Console.WriteLine($"{prop.Name}: 60k propagations (7-day pass @ 10 s) = {sw.ElapsedMilliseconds} ms (sink={sink:F1})");
    }
}

static void RunThreads(string fixtureDir)
{
    var cases = Fixtures.Load(fixtureDir);
    foreach (var template in new IPropagator[] { new SgpNetAdapter(), new OneSgp4Adapter() })
    {
        template.Init(cases[0].Line1, cases[0].Line2);
        var serial = new List<(double, double, double)>();
        for (var i = 0; i < 5000; i++) serial.Add(template.PositionAt(i * 0.05));

        var parallel = new (double, double, double)[5000];
        Parallel.For(0, 5000, i =>
        {
            var p = template is SgpNetAdapter ? (IPropagator)new SgpNetAdapter() : new OneSgp4Adapter();
            p.Init(cases[0].Line1, cases[0].Line2);
            parallel[i] = p.PositionAt(i * 0.05);
        });
        var maxDiff = 0.0;
        for (var i = 0; i < 5000; i++)
            maxDiff = Math.Max(maxDiff, Math.Abs(serial[i].Item1 - parallel[i].Item1));
        Console.WriteLine($"{template.Name} thread-safety: max deviation serial vs 8-parallel = {maxDiff:E1} km");
    }
}


static void RunDeep(string caseId)
{
    var cases = Fixtures.Load("fixtures");
    var c = cases.First(x => x.Id == caseId);
    Console.WriteLine($"case {c.Id} mm={Fixtures.MeanMotion(c):F4} epoch={Fixtures.Epoch(c).Year}:{Fixtures.Epoch(c).Day:F6}");
    foreach (var prop in new IPropagator[] { new SgpNetAdapter(), new OneSgp4Adapter() })
    {
        prop.Init(c.Line1, c.Line2);
        Console.WriteLine($"--- {prop.Name}");
        foreach (var (min, x, y, z) in c.Rows)
        {
            var (px, py, pz) = prop.PositionAt(min);
            var err = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z));
            Console.WriteLine($"  t={min,8:F1} min  err={err,10:F3} km");
        }
    }
}


static void RunDeep2(string caseId)
{
    var cases = Fixtures.Load("fixtures");
    var c = cases.Last(x => x.Id == caseId);
    Console.WriteLine($"case {c.Id} (2nd) mm={Fixtures.MeanMotion(c):F4}");
    var sat = new SGPdotNET.Observation.Satellite(c.Line1, c.Line2);
    var (y, d) = Fixtures.Epoch(c);
    var epochUtc = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(d - 1.0);
    foreach (var (min, x, yz, z) in c.Rows.Take(40))
    {
        var p = sat.Predict(epochUtc.AddMinutes(min));
        var err = Math.Sqrt((p.Position.X - x) * (p.Position.X - x) + (p.Position.Y - yz) * (p.Position.Y - yz) + (p.Position.Z - z) * (p.Position.Z - z));
        Console.WriteLine($"  t={min,8:F1} min  err={err,10:F3} km  (Predict API)");
    }
}


static void RunOmm()
{
    var path = Path.Combine("fixtures", "iss-stations-omm.csv");
    Console.WriteLine($"OMM parse test: {path}");
    var sgpNetSats = SGPdotNET.TLE.Tle.ParseOmmCsv(path);
    Console.WriteLine($"  SGP.NET.ParseOmmCsv: {sgpNetSats.Count} satellites");
    var issTle = sgpNetSats.FirstOrDefault(s => s.Name.Contains("ISS"));
    if (issTle != null)
    {
        var epoch = DateTime.ParseExact(File.ReadAllLines(path).Skip(1).First().Split(',')[2],
            "yyyy-MM-ddTHH:mm:ss.ffffff", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var sat = new SGPdotNET.Observation.Satellite(issTle);
        var p = sat.Predict(epoch.AddHours(1.0));
        Console.WriteLine($"  ISS epoch={epoch:O} predict +1h: pos=({p.Position.X:F1}, {p.Position.Y:F1}, {p.Position.Z:F1}) km, vel=({p.Velocity.X:F4}, {p.Velocity.Y:F4}, {p.Velocity.Z:F4}) km/s");
    }
}

static void RunDump()
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    foreach (var dll in Directory.GetFiles(Path.Combine(home, ".nuget/packages/sgp.net"), "*.dll", SearchOption.AllDirectories)
                 .Concat(Directory.GetFiles(Path.Combine(home, ".nuget/packages/one_sgp4"), "*.dll", SearchOption.AllDirectories))
                 .Where(p => p.Contains("net9.0") || p.Contains("net8.0") || p.Contains("netstandard2.1")))
    {
        try { System.Reflection.Assembly.LoadFrom(dll); } catch { }
    }
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name!.Contains("SGP") || a.GetName().Name!.Contains("Sgp4")))
        foreach (var t in asm.GetExportedTypes().OrderBy(t => t.FullName))
            Console.WriteLine(t.FullName);
}

static double P95(List<double> values)
{
    var sorted = values.OrderBy(v => v).ToArray();
    return sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1];
}
