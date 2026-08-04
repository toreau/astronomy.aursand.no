using System.Globalization;
using System.IO.Compression;

namespace S07Stars;

public sealed record Star(double RaDeg, double DecDeg, double Vmag, int HrId)
{
    public double X => Math.Cos(RaDeg * Math.PI / 180) * Math.Cos(DecDeg * Math.PI / 180);
    public double Y => Math.Sin(RaDeg * Math.PI / 180) * Math.Cos(DecDeg * Math.PI / 180);
    public double Z => Math.Sin(DecDeg * Math.PI / 180);
}

public static class BscCatalog
{
    public static async Task<List<Star>> FetchAsync(string url, string outPath)
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var data = await hc.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(outPath + ".gz", data);
        return ParseGz(outPath + ".gz");
    }

    public static List<Star> ParseGz(string gzPath)
    {
        using var fs = File.OpenRead(gzPath);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var sr = new StreamReader(gz);
        var stars = new List<Star>(9200);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (line.Length < 107) continue;
            var rah = Parse(line, 75, 77);
            var ram = Parse(line, 77, 79);
            var ras = Parse(line, 79, 83);
            var decSign = line[83] == '-' ? -1 : 1;
            var decd = Parse(line, 84, 86);
            var decm = Parse(line, 86, 88);
            var decs = Parse(line, 88, 90);
            var vmag = Parse(line, 102, 107);
            var hr = Parse(line, 0, 4);
            if (rah < 0 || decd < 0 || vmag <= 0) continue;
            var raDeg = (rah + ram / 60.0 + ras / 3600.0) * 15.0;
            var decDeg = decSign * (decd + decm / 60.0 + decs / 3600.0);
            stars.Add(new Star(raDeg, decDeg, vmag, (int)hr));
        }
        return stars;
    }

    private static double Parse(string line, int start, int end)
    {
        var s = line.Substring(start, end - start).Trim();
        return s.Length == 0 ? -1 : double.Parse(s, CultureInfo.InvariantCulture);
    }

    public static void WriteCsv(string path, List<Star> stars) =>
        File.WriteAllLines(path, stars.Select(s =>
            $"{s.RaDeg.ToString("F6", CultureInfo.InvariantCulture)},{s.DecDeg.ToString("F6", CultureInfo.InvariantCulture)},{s.Vmag.ToString("F2", CultureInfo.InvariantCulture)},{s.HrId}"));

    public static List<Star> ReadCsv(string path) =>
        File.ReadAllLines(path).Select(l =>
        {
            var p = l.Split(',');
            return new Star(double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture),
                double.Parse(p[2], CultureInfo.InvariantCulture), int.Parse(p[3]));
        }).ToList();
}
