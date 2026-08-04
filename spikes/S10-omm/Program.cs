using System.Globalization;

namespace S10Omm;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("usage: S10Omm <init|fetch|stage-file|activate|status|rollback|lifecycle|propagate|cross-tle> <db> [...]"); return 1; }
        var mode = args[0];
        var db = args.Length > 1 ? args[1] : "";
        return mode switch
        {
            "init" => OmmStore.Init(db),
            "fetch" => await FetchAsync(db, args[2]),
            "stage-file" => StageFile(db, args[2], args[3]),
            "activate" => Activate(db, args[2]),
            "status" => Status(db),
            "rollback" => OmmStore.Rollback(db, args[2]),
            "lifecycle" => await LifecycleAsync(db),
            "propagate" => Propagate(db, args[2]),
            "cross-tle" => await CrossTle(),
            _ => 1,
        };
    }

    private static async Task<int> FetchAsync(string db, string version)
    {
        var (payload, rows) = await OmmIngest.FetchCelesTrakAsync();
        var errors = OmmIngest.Validate(rows, DateTime.UtcNow);
        if (errors.Count > 0)
        {
            Console.WriteLine($"validate: REJECTED - {errors.Count} violations (first 5):");
            foreach (var (row, field, value) in errors.Take(5))
                Console.WriteLine($"  row {row} [{field}] = {value}");
            return 1;
        }
        OmmStore.Stage(db, version, "celestrak-live", payload, rows);
        Console.WriteLine($"validate: OK - {rows.Count} rows");
        return 0;
    }

    private static int StageFile(string db, string version, string path)
    {
        var payload = File.ReadAllText(path);
        var rows = OmmIngest.ParseCsv(payload);
        var errors = OmmIngest.Validate(rows, DateTime.UtcNow);
        if (errors.Count > 0)
        {
            Console.WriteLine($"validate: REJECTED - {errors.Count} violations (first 5):");
            foreach (var (row, field, value) in errors.Take(5))
                Console.WriteLine($"  row {row} [{field}] = {value}");
            return 1;
        }
        OmmStore.Stage(db, version, Path.GetFileName(path), payload, rows);
        return 0;
    }

    private static int Activate(string db, string version)
    {
        OmmStore.Activate(db, version);
        return Status(db);
    }

    private static int Status(string db)
    {
        var active = OmmStore.ActiveVersion(db);
        var elements = OmmStore.Elements(db);
        var (fresh, warn, degraded, refuse) = OmmIngest.FreshnessState(elements, DateTime.UtcNow);
        Console.WriteLine($"status: active={active ?? "(none)"} elements={elements.Count} " +
                          $"fresh(<24h)={fresh} warn(<72h)={warn} degraded(<168h)={degraded} refuse(>=168h)={refuse}");
        return 0;
    }

    private static async Task<int> LifecycleAsync(string db)
    {
        var rc = OmmStore.Init(db);
        var v1 = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        rc |= await FetchAsync(db, v1);
        rc |= Status(db);
        if (rc == 0)
        {
            rc |= Activate(db, v1);
            rc |= Status(db);
            rc |= OmmStore.Rollback(db, "rollback-target");
            rc |= Status(db);
        }
        else
        {
            Console.WriteLine("lifecycle: activation SKIPPED (staging rejected)");
            rc |= Status(db);
        }
        Console.WriteLine(rc == 0 ? "lifecycle: ALL GREEN" : $"lifecycle: failures rc={rc}");
        return rc;
    }

    private static int Propagate(string db, string ommCsvPath)
    {
        var active = OmmStore.ActiveVersion(db);
        var elements = OmmStore.Elements(db);
        var iss = elements.FirstOrDefault(e => e.Item3.Contains("ISS"));
        if (iss.Item1 == null) { Console.WriteLine("propagate: no ISS in active dataset"); return 1; }
        var (norad, epochUtc, _) = iss;
        Console.WriteLine($"propagate: ISS {norad} epoch={epochUtc:O} active={active}");

        var csv = File.ReadAllText(ommCsvPath);
        var ommList = new SGPdotNET.Parsers.OmmCsvParser().Parse(csv);
        var issOmm = ommList.First(o => o.ObjectName.Contains("ISS"));

        var t = epochUtc.AddHours(1.0);
        var sat = new SGPdotNET.Observation.Satellite(issOmm);
        var pNet = sat.Predict(t);
        Console.WriteLine($"  SGP.NET (OMM->Satellite) @+1h: ({pNet.Position.X:F3}, {pNet.Position.Y:F3}, {pNet.Position.Z:F3}) km");

        var sgp4 = new SGPdotNET.Propagation.Sgp4(new SGPdotNET.TLE.Tle(issOmm));
        var pRaw = sgp4.FindPosition(t);
        Console.WriteLine($"  SGP.NET (OMM->Tle->Sgp4) @+1h: ({pRaw.Position.X:F3}, {pRaw.Position.Y:F3}, {pRaw.Position.Z:F3}) km");
        return 0;
    }

    private static async Task<int> CrossTle()
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var tle = await hc.GetStringAsync("https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=tle");
        var lines = tle.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var iss = -1;
        for (var i = 0; i + 2 < lines.Length; i++)
        {
            if (lines[i].Contains("ISS") && lines[i + 1].StartsWith("1 ") && lines[i + 2].StartsWith("2 "))
            {
                iss = i;
                break;
            }
        }
        if (iss < 0) { Console.WriteLine("cross-tle: ISS TLE not found in feed"); return 1; }
        var (name, l1, l2) = (lines[iss], lines[iss + 1], lines[iss + 2]);
        Console.WriteLine($"cross-tle: {name}");
        Console.WriteLine($"  {l1}");
        Console.WriteLine($"  {l2}");

        var tleObj = new SGPdotNET.TLE.Tle(name, l1, l2);
        var t = tleObj.Epoch.AddHours(1.0);
        var pNet = new SGPdotNET.Propagation.Sgp4(tleObj).FindPosition(t);
        Console.WriteLine($"  SGP.NET  @+1h: ({pNet.Position.X:F3}, {pNet.Position.Y:F3}, {pNet.Position.Z:F3}) km");

        var oneTle = One_Sgp4.ParserTLE.parseTle(l1, l2, name);
        var (y, d) = EpochFromTle(l1);
        var eTime = new One_Sgp4.EpochTime(y, d);
        eTime.addHours(1.0);
        var pOne = One_Sgp4.SatFunctions.getSatPositionAtTime(oneTle, eTime, One_Sgp4.Sgp4.wgsConstant.WGS_72);
        Console.WriteLine($"  One_Sgp4 @+1h: ({pOne.getX():F3}, {pOne.getY():F3}, {pOne.getZ():F3}) km");

        var diff = Math.Sqrt(Math.Pow(pNet.Position.X - pOne.getX(), 2) + Math.Pow(pNet.Position.Y - pOne.getY(), 2) + Math.Pow(pNet.Position.Z - pOne.getZ(), 2));
        Console.WriteLine($"  cross-check |\u0394| = {diff:F3} km (expect < 1 km)");
        return 0;
    }

    private static (int, double) EpochFromTle(string line1)
    {
        var s = line1.Substring(18, 14).Trim();
        var y2 = int.Parse(s[..2]);
        return (y2 >= 57 ? 1900 + y2 : 2000 + y2, double.Parse(s[2..], CultureInfo.InvariantCulture));
    }
}
