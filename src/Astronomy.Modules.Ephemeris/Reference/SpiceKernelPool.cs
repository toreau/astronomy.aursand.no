using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Astronomy.Modules.Ephemeris.Reference;

/// <summary>
/// Loads the CSPICE kernel pool (de440s.bsp, naif0012.tls, pck00010.tpc,
/// optionally earth_assoc_itrf93.tf) and serializes all CSPICE calls under a global
/// lock (S0.3 finding: this mirror build corrupts its CHKIN/CHKOUT tracer under
/// concurrency; the lock was validated 9/9 in the spike probe).
/// Kernel absence or native-lib absence degrades gracefully: IsAvailable=false with a
/// human-readable Reason; no kernel writes, no exceptions at construction.
/// </summary>
internal sealed class SpiceKernelPool
{
    private static readonly object Sync = new();

    private static readonly string[] BaseKernels = ["naif0012.tls", "pck00010.tpc"];
    private static readonly string[] OptionalKernels = ["de440s_plus_MarsPC.bsp", "earth_assoc_itrf93.tf"];
    private static readonly string[] PlanetaryKernels = ["de441.bsp", "de440.bsp", "de440s.bsp"];

    public bool IsAvailable { get; }

    public string Reason { get; } = "kernel pool not initialized";

    public IReadOnlyDictionary<string, string> KernelVersions { get; } = new Dictionary<string, string>();

    /// <summary>
    /// Epoch coverage of the loaded planetary kernel: de441/de440 span 1620-2170,
    /// de440s (reduced NAIF short product) 1849-2150. The DE-series SPKs carry planet
    /// CENTER segments for the inner planets (Sun, Moon, Mercury, Venus, Earth) and
    /// BARYCENTER segments for the outer planets; the reference tier uses planet
    /// barycenters for Mars..Neptune unless de440s_plus_MarsPC.bsp is loaded
    /// (Mars center). Barycenter-vs-center offset is <= 0.05" for all outer planets.
    /// de441 is preferred because Horizons computes its astrometric quantities from it.
    /// </summary>
    public DateTime CoverageStartUtc { get; } = new(1849, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public DateTime CoverageEndUtc { get; } = new(2150, 12, 31, 23, 59, 59, DateTimeKind.Utc);

    public bool HasKernel(string name) => KernelVersions.ContainsKey(name);

    public SpiceKernelPool(string kernelDir)
    {
        if (!Directory.Exists(kernelDir))
        {
            Reason = $"kernel directory not found: {kernelDir}";
            return;
        }
        var versions = new Dictionary<string, string>();
        var planetKernel = PlanetaryKernels.FirstOrDefault(k => File.Exists(Path.Combine(kernelDir, k)));
        if (planetKernel is null)
        {
            Reason = $"no planetary kernel found in {kernelDir} (looked for {string.Join(", ", PlanetaryKernels)})";
            return;
        }
        var kernels = new List<string> { planetKernel };
        kernels.AddRange(BaseKernels);
        foreach (var optional in OptionalKernels)
        {
            if (File.Exists(Path.Combine(kernelDir, optional)))
                kernels.Add(optional);
        }
        foreach (var name in kernels)
        {
            var path = Path.Combine(kernelDir, name);
            if (!File.Exists(path))
            {
                Reason = $"kernel missing: {path}";
                return;
            }
            versions[name] = Sha256Prefix(path);
            if (name is "de441.bsp" or "de440.bsp")
            {
                CoverageStartUtc = new DateTime(1620, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                CoverageEndUtc = new DateTime(2170, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            }
        }
        try
        {
            lock (Sync)
            {
                CSpice.Erract("SET", 32, "RETURN", new byte[32]);
                foreach (var name in versions.Keys)
                {
                    CSpice.Furnsh(Path.Combine(kernelDir, name));
                    if (CSpice.Failed() != 0)
                    {
                        Reason = $"furnsh failed for {name}: {SpiceError()}";
                        return;
                    }
                }
            }
            IsAvailable = true;
            Reason = "ok";
            KernelVersions = versions;
        }
        catch (DllNotFoundException)
        {
            Reason = "libcspice.so not found (reference tier not deployed in this container)";
        }
        catch (BadImageFormatException)
        {
            Reason = "libcspice.so failed to load (architecture mismatch?)";
        }
        catch (UnauthorizedAccessException ex)
        {
            Reason = $"kernel directory unreadable: {ex.Message.Split('\n')[0]}";
        }
        catch (Exception ex)
        {
            Reason = ex.Message.Split('\n')[0];
        }
    }

    public double Et(DateTimeOffset utc)
    {
        lock (Sync)
        {
            var utcText = utc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
            CSpice.Utc2Et(utcText, out var et);
            ThrowIfFailed($"utc2et({utcText})");
            return et;
        }
    }

    public (double[] Xyz, double Lt) SpkPos(string target, double et, string abcorr)
    {
        lock (Sync)
        {
            var pos = new double[3];
            CSpice.SpkPos(target, et, "J2000", abcorr, "EARTH", pos, out var lt);
            ThrowIfFailed($"spkpos({target}, J2000, {abcorr})");
            return (pos, lt);
        }
    }

    public (double RangeKm, double RaDeg, double DecDeg) RecRad(double[] xyz)
    {
        lock (Sync)
        {
            CSpice.RecRad(xyz, out var range, out var raRad, out var decRad);
            ThrowIfFailed("recrad");
            return (range, raRad * 180.0 / Math.PI, decRad * 180.0 / Math.PI);
        }
    }

    public void FurnshRaw(string path)
    {
        lock (Sync)
        {
            CSpice.Furnsh(path);
            ThrowIfFailed($"furnsh({path})");
        }
    }

    public static void ThrowIfFailed(string op)
    {
        if (CSpice.Failed() == 0) return;
        var message = SpiceError();
        throw new InvalidOperationException($"{op} failed: {message}");
    }

    private static string SpiceError()
    {
        var buffer = new byte[4096];
        CSpice.GetMsg("LONG", buffer.Length, buffer);
        CSpice.Reset();
        var text = Encoding.UTF8.GetString(buffer);
        var nul = text.IndexOf('\0');
        return (nul >= 0 ? text[..nul] : text).Trim();
    }

    private static string Sha256Prefix(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))[..8].ToLowerInvariant();
    }
}
