using S02AstronomyEngine;

if (args.Length < 2)
{
    Console.WriteLine("usage: S02AstronomyEngine generate <body> <start> <stop> <step>");
    Console.WriteLine("       S02AstronomyEngine compare <body> [body ...]");
    Console.WriteLine("       S02AstronomyEngine compare-all");
    return 1;
}

Directory.CreateDirectory("fixtures");
Directory.CreateDirectory("output");

switch (args[0])
{
    case "generate":
    {
        var body = args[1];
        if (!HorizonsApi.Bodies.ContainsKey(body)) { Console.WriteLine($"unknown body '{body}'"); return 1; }
        var start = DateTime.Parse(args[2]);
        var stop = DateTime.Parse(args[3]);
        var step = args[4];
        Console.WriteLine($"fetching Horizons {body} {start:yyyy-MM-dd}..{stop:yyyy-MM-dd} step {step} ...");
        var rows = await HorizonsApi.FetchAsync(body, start, stop, step);
        var path = Path.Combine("fixtures", $"horizons_{body}_{start:yyyyMMdd}_{stop:yyyyMMdd}.csv");
        await File.WriteAllLinesAsync(path, rows.Select(r =>
            $"{r.Utc:O},{r.RaJ2000Deg:F6},{r.DecJ2000Deg:F6},{r.RaOfDateDeg:F6},{r.DecOfDateDeg:F6},{r.DistAu:F8}"));
        Console.WriteLine($"wrote {rows.Count} rows -> {path}");
        return 0;
    }

    case "compare":
    {
        var bodies = args.Skip(1).ToList();
        foreach (var body in bodies)
        {
            var fixture = FindFixture(body);
            if (fixture == null) continue;
            var rows = await LoadFixture(fixture);
            var stats = Comparison.Compare(body, rows);
            Console.WriteLine($"{body,-9} N={stats.N,5}  J2000-astrometric: mean {stats.MeanJ2000Arcsec,7:F1}\" p95 {stats.P95J2000Arcsec,7:F1}\" max {stats.MaxJ2000Arcsec,7:F1}\" | of-date-apparent: mean {stats.MeanOfDateArcsec,7:F1}\" p95 {stats.P95OfDateArcsec,7:F1}\" max {stats.MaxOfDateArcsec,7:F1}\" | dist mean {stats.MeanDistPct,5:F3}% max {stats.MaxDistPct,5:F3}%");
        }
        return 0;
    }

    case "compare-all":
    {
        foreach (var body in HorizonsApi.Bodies.Keys)
        {
            var fixture = FindFixture(body);
            if (fixture == null) continue;
            var rows = await LoadFixture(fixture);
            var stats = Comparison.Compare(body, rows);
            Console.WriteLine($"{body,-9} N={stats.N,5}  J2000: mean {stats.MeanJ2000Arcsec,7:F1}\" p95 {stats.P95J2000Arcsec,7:F1}\" max {stats.MaxJ2000Arcsec,7:F1}\" | of-date: mean {stats.MeanOfDateArcsec,7:F1}\" p95 {stats.P95OfDateArcsec,7:F1}\" max {stats.MaxOfDateArcsec,7:F1}\" | dist mean {stats.MeanDistPct,5:F3}% max {stats.MaxDistPct,5:F3}%");
        }
        return 0;
    }

    case "rts":
        await Events.RunRtsAsync(args[1], int.Parse(args[2]));
        return 0;

    case "phases":
        await Events.RunPhasesAsync(int.Parse(args[1]));
        return 0;

    default:
        Console.WriteLine($"unknown mode '{args[0]}'");
        return 1;
}

static string? FindFixture(string body)
{
    var match = Directory.GetFiles("fixtures", $"horizons_{body}_*.csv").FirstOrDefault();
    if (match == null) Console.WriteLine($"[warn] no fixture for {body}");
    return match;
}

static async Task<List<FixtureRow>> LoadFixture(string path)
{
    var rows = new List<FixtureRow>();
    foreach (var line in await File.ReadAllLinesAsync(path))
    {
        var parts = line.Split(',');
        rows.Add(new FixtureRow(
            DateTime.Parse(parts[0]),
            double.Parse(parts[1]), double.Parse(parts[2]),
            double.Parse(parts[3]), double.Parse(parts[4]),
            double.Parse(parts[5])));
    }
    return rows;
}
