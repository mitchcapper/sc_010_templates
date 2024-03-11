using System;
using TemplateLib.NativeGen;

namespace Windows.Win32;

/// <summary>
/// A stand-in for 64-bit pointers in a 32-bit runtime.
/// </summary>
public struct UIntPtr64 : IUIntPtr {
	public ulong Value;

	public static implicit operator UIntPtr64(ulong v) => new() { Value = v };
	public static explicit operator ulong(UIntPtr64 v) => v.Value;
	public override string ToString() => Value.ToString();
	public UInt64 ToPtr64() => Value;
	public override bool Equals(object obj) => Equals(obj as IUIntPtr);
	public static bool operator ==(UIntPtr64 lhs, IUIntPtr rhs) => rhs.Equals(lhs);
	public static bool operator !=(UIntPtr64 lhs, IUIntPtr rhs) => !(lhs == rhs);
	bool IEquatable<IUIntPtr>.Equals(IUIntPtr other) => _IUIntPtrHelpers.Equals(this, other);
	public override int GetHashCode() => _IUIntPtrHelpers.GetHashCode(this);
}

public struct UIntPtr64<T> : IUIntPtr<T> where T : unmanaged {
	public ulong Value;

	public static implicit operator UIntPtr64<T>(ulong v) => new() { Value = v };
	public static explicit operator ulong(UIntPtr64<T> v) => v.Value;

	public static explicit operator UIntPtr64(UIntPtr64<T> v) => v.Value;
	public UInt64 ToPtr64() => Value;
	public override string ToString() => Value.ToString();

	public override bool Equals(object obj) => Equals(obj as IUIntPtr);
	public static bool operator ==(UIntPtr64<T> lhs, IUIntPtr rhs) => rhs.Equals(lhs);
	public static bool operator !=(UIntPtr64<T> lhs, IUIntPtr rhs) => !(lhs == rhs);
	bool IEquatable<IUIntPtr>.Equals(IUIntPtr other) => _IUIntPtrHelpers.Equals(this, other);
	public override int GetHashCode() => _IUIntPtrHelpers.GetHashCode(this);
}
