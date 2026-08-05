using System.Runtime.InteropServices;

namespace Astronomy.Modules.Ephemeris.Reference;

/// <summary>
/// Minimal CSPICE (N66, mirror build) P/Invoke surface, promoted from the S0.3 spike
/// (spikes/S03-spice/probe/CSpice.cs, validated 9/9 there).
/// CSPICE is NOT thread-safe in this build (CHKIN/CHKOUT corruption under concurrency,
/// S0.3 finding); every call must be serialized via SpiceKernelPool.Sync.
/// </summary>
internal static partial class CSpice
{
    private const string Lib = "libcspice";

    [LibraryImport(Lib, EntryPoint = "furnsh_c")]
    public static partial void Furnsh([MarshalAs(UnmanagedType.LPUTF8Str)] string file);

    [LibraryImport(Lib, EntryPoint = "unload_c")]
    public static partial void Unload([MarshalAs(UnmanagedType.LPUTF8Str)] string file);

    [LibraryImport(Lib, EntryPoint = "failed_c")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int Failed();

    [LibraryImport(Lib, EntryPoint = "reset_c")]
    public static partial void Reset();

    [LibraryImport(Lib, EntryPoint = "getmsg_c")]
    public static partial void GetMsg(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string msgType, int msglen,
        [Out] byte[] message);

    [LibraryImport(Lib, EntryPoint = "spkpos_c")]
    public static partial void SpkPos(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string target, double et,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string referenceFrame,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string aberration,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string observer,
        [In, Out] double[] ptarg, out double lt);

    [LibraryImport(Lib, EntryPoint = "recrad_c")]
    public static partial void RecRad([In] double[] vrect, out double range, out double raRad, out double decRad);

    [LibraryImport(Lib, EntryPoint = "pxform_c")]
    public static partial void PxForm(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string from,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string to, double et,
        [In, Out] double[] rotate);

    [LibraryImport(Lib, EntryPoint = "utc2et_c")]
    public static partial void Utc2Et([MarshalAs(UnmanagedType.LPUTF8Str)] string utc, out double et);

    [LibraryImport(Lib, EntryPoint = "erract_c")]
    public static partial void Erract(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string op, int lenout,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string action, [Out] byte[] value);

    [LibraryImport(Lib, EntryPoint = "deltet_c")]
    public static partial void Deltet(double epoch, [MarshalAs(UnmanagedType.LPUTF8Str)] string eptype, out double delta);

    [DllImport(Lib, EntryPoint = "spkobj_c")]
    internal static extern void SpkObj([MarshalAs(UnmanagedType.LPUTF8Str)] string spk, ref SpiceCell idsCell);

    [DllImport(Lib, EntryPoint = "spkcov_c")]
    internal static extern void SpkCov([MarshalAs(UnmanagedType.LPUTF8Str)] string spk, int idcode, ref SpiceCell coverCell);

    [LibraryImport(Lib, EntryPoint = "et2utc_c")]
    public static partial void Et2Utc(
        double et, [MarshalAs(UnmanagedType.LPUTF8Str)] string format, int prec, int lenout,
        [Out] byte[] utc);
}
