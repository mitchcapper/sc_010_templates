using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TemplateLib;
using TemplateLib.NativeGen;

namespace Windows.Win32.Foundation;

[StructLayout(LayoutKind.Sequential, Size = 0x10)]
public struct UNICODE_STRING64 : IStructTargetApp64, IAutoValueFetch<string> {
	public ushort Length;
	public ushort MaximumLength;
	/// <summary>
	/// sometimes this is invalid if length is 0 so beware
	/// </summary>
	public UIntPtr64<char> Buffer;
	public unsafe string GetValue(SafeProcessHandle hProcess) {
		if (Length == 0)
			return "";
#if WLIB
		string s = new string('\0', Length / 2);
		fixed (char* sptr = s)
			UnmanagedHelper.ReadProcessMemoryAnyProcBits(hProcess, Buffer, new LargeIntPtr(sptr), Length, Length);
		return s;
#else
		throw new NotImplementedException();
#endif
	}
}
