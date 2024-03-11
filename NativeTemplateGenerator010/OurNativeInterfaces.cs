using System;
using Microsoft.Win32.SafeHandles;
using TemplateLib.NativeGen;
using Windows.Win32.Foundation;

public static class _IUIntPtrHelpers {
	/// <summary>
	/// Do not use this for remote addresses when they may be 64 bit and we are 32 bit
	/// </summary>
	/// <returns></returns>
	public static IntPtr ToPtr(this IUIntPtr ptr) => new IntPtr((long)ptr.ToPtr64());
	public static bool Equals(this IUIntPtr ptr, IUIntPtr ptr2) => ptr?.ToPtr64() == ptr2?.ToPtr64();
	/// <summary>
	/// Do not use for remote addresses when they may be 64 bit and we are 32 bit
	/// </summary>
	/// <param name="ptr"></param>
	/// <returns></returns>
	public static unsafe void* ToVoidPtr(this IUIntPtr ptr) => ptr == null ? (void*)0 : (void*)ptr.ToPtr64();
	public static LargeIntPtr ToLargePtr(this IUIntPtr ptr) => (ptr is LargeIntPtr l) ? l : new LargeIntPtr(ptr.ToPtr64());
	public static IUIntPtr ThrowIfNull(this IUIntPtr ptr) => ptr.IsNull() ? throw new NullReferenceException() : ptr;
	public static IUIntPtr GetZero(this IUIntPtr ptr) => Zero;
	public static IUIntPtr<T> GetZero<T>(this IUIntPtr<T> ptr) where T : unmanaged => (IUIntPtr<T>)Zero;
	public static IUIntPtr Zero { get; private set; } = new LargeIntPtr(0);

	public static bool IsNull(this IUIntPtr ptr) => ptr.ToPtr64() == 0;
	internal static int GetHashCode(this IUIntPtr ptr) => ptr.ToPtr64().GetHashCode();
}
namespace TemplateLib.NativeGen {
	public interface IStructTargetApp32 { }
	public interface IStructTargetApp64 { }
	public interface IAutoValueFetch<T> {
		T GetValue(SafeProcessHandle hProcess);
	}
	
	public interface IUIntPtr : System.IEquatable<IUIntPtr> {
		ulong ToPtr64();

	}
	//public interface WrappedIUIntPtr<T> where T : IUIntPtr { }
	public interface IUIntPtr<T> : IUIntPtr where T : unmanaged { }

	public interface IAbstractUIntPtr<INTERFACE_TYPE> : IUIntPtr {
		Type ActualType { get; }
	}
	public class InterfacedUIntPtr<INTERFACE_TYPE, ACTUAL_TYPE> : AbstractUIntPtr<INTERFACE_TYPE> where ACTUAL_TYPE : unmanaged, INTERFACE_TYPE {
		public InterfacedUIntPtr(IUIntPtr<ACTUAL_TYPE> ActualPointer) : base(ActualPointer, typeof(ACTUAL_TYPE)) {
		}
		public new IUIntPtr<ACTUAL_TYPE> Ptr;
	}
	public abstract class AbstractUIntPtr<INTERFACE_TYPE> : IAbstractUIntPtr<INTERFACE_TYPE> {
		public static InterfacedUIntPtr<INTERFACE_TYPE, ACTUAL_TYPE> Get<ACTUAL_TYPE>(IUIntPtr<ACTUAL_TYPE> ActualPointer) where ACTUAL_TYPE : unmanaged, INTERFACE_TYPE {
			return new(ActualPointer);
		}
		public ulong ToPtr64() => Ptr.ToPtr64();
		protected AbstractUIntPtr(IUIntPtr ActualPointer, Type ActualType) {
			Ptr = ActualPointer;
			this.ActualType = ActualType;
		}
		public IUIntPtr Ptr;
		public Type ActualType { get; private set; }
		public override bool Equals(object obj) => Equals(obj as IUIntPtr);
		public static bool operator ==(AbstractUIntPtr<INTERFACE_TYPE> lhs, IUIntPtr rhs) => rhs.Equals(lhs);
		public static bool operator !=(AbstractUIntPtr<INTERFACE_TYPE> lhs, IUIntPtr rhs) => !(lhs == rhs);
		bool IEquatable<IUIntPtr>.Equals(IUIntPtr other) => _IUIntPtrHelpers.Equals(this, other);
		public override int GetHashCode() => _IUIntPtrHelpers.GetHashCode(this);
	}
}
