using System.Runtime.InteropServices;
using System.Text;

namespace SpiceProbe;

public static unsafe partial class CSpice
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

    [LibraryImport(Lib, EntryPoint = "radrec_c")]
    public static partial void RadRec(double range, double ra, double dec, [In, Out] double[] vrect);

    [LibraryImport(Lib, EntryPoint = "pxform_c")]
    public static partial void PxForm(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string from,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string to, double et,
        [In, Out] double[] rotate);

    [LibraryImport(Lib, EntryPoint = "utc2et_c")]
    public static partial void Utc2Et([MarshalAs(UnmanagedType.LPUTF8Str)] string utc, out double et);

    [LibraryImport(Lib, EntryPoint = "et2utc_c")]
    public static partial void Et2Utc(
        double et, [MarshalAs(UnmanagedType.LPUTF8Str)] string format, int prec, int lenout,
        [Out] byte[] utc);

    [LibraryImport(Lib, EntryPoint = "deltet_c")]
    public static partial void Deltet(double epoch, [MarshalAs(UnmanagedType.LPUTF8Str)] string eptype, out double delta);

    [LibraryImport(Lib, EntryPoint = "bodn2c_c")]
    public static partial double Bodn2C([MarshalAs(UnmanagedType.LPUTF8Str)] string name, out int code);

    [LibraryImport(Lib, EntryPoint = "spkcov_c")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int SpkCov([MarshalAs(UnmanagedType.LPUTF8Str)] string file, int idcode, [In, Out] double[] cover);
}
