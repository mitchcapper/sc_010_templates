using System;
using System.Numerics;

namespace TemplateLib.NativeGen {

	/// <summary>
	/// can safely hold a 64 bit pointer and convert between several of the common formats
	/// </summary>
	public struct LargeIntPtr : IUIntPtr
#if NET7_0_OR_GREATER
		,IAdditionOperators<LargeIntPtr, LargeIntPtr, LargeIntPtr>, ISubtractionOperators<LargeIntPtr, LargeIntPtr, LargeIntPtr>, IAdditionOperators<LargeIntPtr, IUIntPtr, LargeIntPtr>, IAdditionOperators<LargeIntPtr, ulong, LargeIntPtr>
#endif
		{
		private static void NullValCheck(ulong Value, bool allowNull = false) { if (!allowNull && Value == 0) throw new NullReferenceException("Creating a pointer but giving it a null reference and not set to allowed"); }
		public ulong Value;
		public LargeIntPtr(IUIntPtr ptr, bool allowNull = false) => NullValCheck(Value = ptr.ToPtr64(), allowNull);
		unsafe public LargeIntPtr(void* ptr, bool allowNull = false) => NullValCheck(Value = (ulong)ptr);
		public LargeIntPtr(IntPtr PlatformPtr, bool allowNull = false) => NullValCheck(Value = (ulong)PlatformPtr.ToInt64());
		public LargeIntPtr(ulong Value, bool allowNull = false) => NullValCheck(this.Value = Value);

		//public LargeIntPtr ToLargePtr() => this;

		//public IntPtr ToPtr() => new IntPtr((int)Value);

		public ulong ToPtr64() => Value;

		public static LargeIntPtr operator +(LargeIntPtr left, LargeIntPtr right) => new LargeIntPtr(left.Value + right.Value);

		public static LargeIntPtr operator +(LargeIntPtr left, IUIntPtr right) => new LargeIntPtr(left.Value + right.ToPtr64());
		public static LargeIntPtr operator +(LargeIntPtr left, ulong right) => new LargeIntPtr(left.Value + right);

		public static LargeIntPtr operator -(LargeIntPtr left, LargeIntPtr right) => new LargeIntPtr(left.Value - right.Value);

		public static implicit operator LargeIntPtr(IntPtr CurPlatformPtr) => new LargeIntPtr(CurPlatformPtr);

		public override bool Equals(object obj) => Equals(obj as IUIntPtr);
		public static bool operator ==(LargeIntPtr lhs, IUIntPtr rhs) => rhs.Equals(lhs);
		public static bool operator !=(LargeIntPtr lhs, IUIntPtr rhs) => !(lhs == rhs);
		bool IEquatable<IUIntPtr>.Equals(IUIntPtr other) => _IUIntPtrHelpers.Equals(this, other);
		public override int GetHashCode() => _IUIntPtrHelpers.GetHashCode(this);


	}


}
