using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace S07Stars;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage: S07Stars <fetch|verify|bench>"); return 1; }
        return args[0] switch
        {
            "fetch" => await FetchAsync(),
            "verify" => Verify(),
            "bench" => Bench(),
            _ => 1,
        };
    }

    private static async Task<int> FetchAsync()
    {
        var url = "https://cdsarc.cds.unistra.fr/ftp/V/50/catalog.gz";
        Directory.CreateDirectory("fixtures");
        var stars = await BscCatalog.FetchAsync(url, "fixtures/bsc5");
        BscCatalog.WriteCsv("fixtures/bsc.csv", stars);
        Console.WriteLine($"BSC: {stars.Count} stars parsed -> fixtures/bsc.csv");
        return 0;
    }

    private static int Verify()
    {
        var stars = BscCatalog.ReadCsv("fixtures/bsc.csv");
        var indexes = new IStarIndex[] { new BruteForceIndex(stars), new TileIndex(stars), new RaSortedIndex(stars) };
        var rng = new Random(42);
        var failures = 0;
        for (var trial = 0; trial < 200; trial++)
        {
            var ra = rng.NextDouble() * 360;
            var dec = rng.NextDouble() * 170 - 85;
            var radius = 0.5 + rng.NextDouble() * 9.5;
            var mag = 3.0 + rng.NextDouble() * 3.0;
            var reference = indexes[0].ConeSearch(ra, dec, radius, mag);
            foreach (var idx in indexes.Skip(1))
            {
                var got = idx.ConeSearch(ra, dec, radius, mag);
                var a = got.Select(s => s.HrId).OrderBy(x => x).ToArray();
                var b = reference.Select(s => s.HrId).OrderBy(x => x).ToArray();
                if (!a.SequenceEqual(b))
                {
                    failures++;
                    Console.WriteLine($"MISMATCH {idx.Name}: ra={ra:F2} dec={dec:F2} r={radius:F2} mag={mag:F2}: {got.Count} vs {reference.Count}");
                    if (failures > 5) break;
                }
            }
        }
        Console.WriteLine(failures == 0
            ? $"verify: all indexes agree with brute force over 200 random cones"
            : $"verify: {failures} mismatches");
        return failures == 0 ? 0 : 1;
    }

    private static int Bench()
    {
        var stars = BscCatalog.ReadCsv("fixtures/bsc.csv");
        Console.WriteLine($"BSC size: {stars.Count}");
        var synthetic100k = Synthetic(100_000, 42);
        var results = BenchmarkRunner.Run<ConeBench>();
        return 0;
    }

    public static List<Star> Synthetic(int n, int seed)
    {
        var rng = new Random(seed);
        var stars = new List<Star>(n);
        for (var i = 0; i < n; i++)
        {
            var ra = rng.NextDouble() * 360;
            var dec = Math.Asin(rng.NextDouble() * 2 - 1) * 180 / Math.PI;
            stars.Add(new Star(ra, dec, rng.NextDouble() * 8.0, 100000 + i));
        }
        return stars;
    }
}

[MemoryDiagnoser]
public class ConeBench
{
    private List<Star> _bsc = null!;
    private List<Star> _s10k = null!;
    private List<Star> _s100k = null!;
    private IStarIndex _bfBsc = null!, _tileBsc = null!, _raBsc = null!;
    private IStarIndex _bf10k = null!, _tile10k = null!, _ra10k = null!;
    private IStarIndex _bf100k = null!, _tile100k = null!, _ra100k = null!;
    private (double Ra, double Dec, double R, double Mag)[] _queries = null!;

    [GlobalSetup]
    public void Setup()
    {
        _bsc = BscCatalog.ReadCsv(FindFixtures());
        _s10k = Program.Synthetic(10_000, 7);
        _s100k = Program.Synthetic(100_000, 8);
        _bfBsc = new BruteForceIndex(_bsc); _tileBsc = new TileIndex(_bsc); _raBsc = new RaSortedIndex(_bsc);
        _bf10k = new BruteForceIndex(_s10k); _tile10k = new TileIndex(_s10k); _ra10k = new RaSortedIndex(_s10k);
        _bf100k = new BruteForceIndex(_s100k); _tile100k = new TileIndex(_s100k); _ra100k = new RaSortedIndex(_s100k);
        var rng = new Random(1);
        _queries = new (double, double, double, double)[20];
        for (var i = 0; i < 20; i++)
            _queries[i] = (rng.NextDouble() * 360, rng.NextDouble() * 170 - 85, 1.0 + rng.NextDouble() * 9.0, 4.0 + rng.NextDouble() * 3.0);
    }

    private static string FindFixtures()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", "bsc.csv");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("fixtures/bsc.csv not found above the benchmark output dir");
    }

    private void Run(IStarIndex idx, int count)
    {
        var sink = 0;
        for (var i = 0; i < count; i++)
        {
            var q = _queries[i % _queries.Length];
            sink += idx.ConeSearch(q.Ra, q.Dec, q.R, q.Mag).Count;
        }
        if (sink == int.MinValue) Console.WriteLine("unreachable");
    }

    [Benchmark] public void Bsc_Brute_20q() => Run(_bfBsc, 20);
    [Benchmark] public void Bsc_Tile_20q() => Run(_tileBsc, 20);
    [Benchmark] public void Bsc_RaSorted_20q() => Run(_raBsc, 20);
    [Benchmark] public void S10k_Brute_20q() => Run(_bf10k, 20);
    [Benchmark] public void S10k_Tile_20q() => Run(_tile10k, 20);
    [Benchmark] public void S10k_RaSorted_20q() => Run(_ra10k, 20);
    [Benchmark] public void S100k_Brute_20q() => Run(_bf100k, 20);
    [Benchmark] public void S100k_Tile_20q() => Run(_tile100k, 20);
    [Benchmark] public void S100k_RaSorted_20q() => Run(_ra100k, 20);
}
