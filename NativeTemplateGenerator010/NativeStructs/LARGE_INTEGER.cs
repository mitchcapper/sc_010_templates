using System.Runtime.InteropServices;

namespace Windows.Win32;

[StructLayout(LayoutKind.Explicit)]
public struct LARGE_INTEGER {
	[FieldOffset(0x00)] public long QuadPart;
	public uint LowPart => (uint)(QuadPart & 0x0000FFFF);
	public int HighPart => (int)(QuadPart >> 4);
}
