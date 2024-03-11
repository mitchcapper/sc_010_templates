using System;
using System.Runtime.InteropServices;
using TemplateLib.NativeGen;

namespace Windows.Win32;

/// <summary>
/// A stand-in for 32-bit pointers in a 64-bit runtime.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct UIntPtr32 : IUIntPtr {
	public uint Value;

	public static implicit operator UIntPtr32(uint v) => new() { Value = v };
	public static implicit operator uint(UIntPtr32 v) => v.Value;

	public unsafe static explicit operator void*(UIntPtr32 v) => (void*)v.Value;
	public override string ToString() => Value.ToString();
	public UInt64 ToPtr64() => Value;
	public override bool Equals(object obj) => Equals(obj as IUIntPtr);
	public static bool operator ==(UIntPtr32 lhs, IUIntPtr rhs) => rhs.Equals(lhs);
	public static bool operator !=(UIntPtr32 lhs, IUIntPtr rhs) => !(lhs == rhs);
	bool IEquatable<IUIntPtr>.Equals(IUIntPtr other) => _IUIntPtrHelpers.Equals(this, other);
	public override int GetHashCode() => _IUIntPtrHelpers.GetHashCode(this);
}
[StructLayout(LayoutKind.Sequential)]
public struct UIntPtr32<T> : IUIntPtr<T> where T : unmanaged {
	public uint Value;

	public static implicit operator UIntPtr32<T>(uint v) => new() { Value = v };
	public static explicit operator UIntPtr32(UIntPtr32<T> v) => v.Value;
	public unsafe static explicit operator T*(UIntPtr32<T> v) => (T*)v.Value;
	public override string ToString() => Value.ToString();
	public UInt64 ToPtr64() => Value;
	public override bool Equals(object obj) => Equals(obj as IUIntPtr);
	public static bool operator ==(UIntPtr32<T> lhs, IUIntPtr rhs) => rhs.Equals(lhs);
	public static bool operator !=(UIntPtr32<T> lhs, IUIntPtr rhs) => !(lhs == rhs);
	bool IEquatable<IUIntPtr>.Equals(IUIntPtr other) => _IUIntPtrHelpers.Equals(this, other);
	public override int GetHashCode() => _IUIntPtrHelpers.GetHashCode(this);
}
