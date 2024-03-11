using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TemplateLib;
using TemplateLib.NativeGen;

namespace Windows.Win32.Foundation;

[StructLayout(LayoutKind.Sequential)]
public struct STRING64 : IAutoValueFetch<string> {
	public ushort Length;
	public ushort MaximumLength;
	/* The compiler adds 6 bytes of padding here, hence the total size of 24 bytes instead of 18 bytes */
	private UIntPtr64 _buffer;

	/// <summary>
	/// Copy the 8-bit string to a managed, utf-16 string
	/// </summary>
	/// <param name="hProcess">a SafeProcessHandle with PROCESS_QUERY_LIMITED_INFORMATION and PROCESS_VM_READ rights.</param>
	/// <returns>A managed string that holds a copy of the native string/returns>
	/// <exception cref="NTStatusException">NtWow64ReadVirtualMemory64 failed</exception>
	public unsafe string GetValue(SafeProcessHandle hProcess) {
		// because this process uses 32-bit pointers and the target process
		// uses 64-bit pointers, we have to use a ulong to ensure the
		// pointer parameter is not truncated.
		// our buffer can be a 32-bit pointer, however.
#if WLIB
		IntPtr buffer = Marshal.AllocHGlobal(MaximumLength);
		try {
			UnmanagedHelper.ReadProcessMemoryAnyProcBits(hProcess, _buffer, new LargeIntPtr(buffer), MaximumLength);
			return Marshal.PtrToStringAnsi(buffer, MaximumLength);
		} finally {
			Marshal.FreeHGlobal(buffer);
		}
#else
			throw new NotImplementedException();
#endif
	}
}
