using System.Runtime.InteropServices;

namespace Astronomy.Modules.Ephemeris.Reference;

/// <summary>
/// CSPICE cell header (SpiceCel.h struct _SpiceCell, 64-bit build, SpiceInt=int32).
/// Layout: dtype, length, size, card, isSet, adjust, init (ints), then base/data
/// pointers. The backing array carries SPICE_CELL_CTRLSZ (6) control cells followed
/// by the data; base points at the array start, data at the first data cell.
/// dtype: SPICE_CHR=0, SPICE_DP=1, SPICE_INT=2.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SpiceCell
{
    public int Dtype;
    public int Length;
    public int Size;
    public int Card;
    public int IsSet;
    public int Adjust;
    public int Init;
    public nint Base;
    public nint Data;
}
