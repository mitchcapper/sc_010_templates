using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TemplateLib;
using TemplateLib.NativeGen;

namespace Windows.Win32.Foundation {

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct STRING32 : IAutoValueFetch<string> {
		public ushort Length;
		public ushort MaximumLength;
		public readonly UIntPtr32<byte> _buffer;

		/// <summary>
		/// Copy the 8-bit string to a managed, utf-16 string
		/// </summary>
		/// <param name="hProcess">a SafeProcessHandle with PROCESS_QUERY_LIMITED_INFORMATION and PROCESS_VM_READ rights.</param>
		/// <returns>A managed string that holds a copy of the native string/returns>
		/// <exception cref="NTStatusException">ReadProcessMemory failed</exception>
		public unsafe string GetValue(SafeProcessHandle hProcess) {
			IntPtr buffer = Marshal.AllocHGlobal(MaximumLength);
#if WLIB
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



}
