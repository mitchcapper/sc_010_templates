#pragma warning disable CS0169
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;


/// <summary>
/// Tool that can assist with figuring out the packing, and offsets of .net structures that reflect native structures.  Tries to follow standard packing rules for MSVC/GCC.  It can calculate not jsut the actual pack size but also the natural packing excluding if an item had a forced offset.  Finally it also calculates 0 pad for things like 010 editor that don't yet support padding.
/// Any overrides can be specified but this class generally shouldn't have manualy hard coded work arounds added.   As this maintains a full tree of all types parsed it can also be used for walking the children tree.
/// </summary>
/// 
/*
		https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.structlayoutattribute.pack?view=net-7.0 documents packing
		in short:
			Pack size is the smaller of the explicitly specified pack size and the largest item on the struct.  If it is 0 it is always jus the largest item on the struct.
				Each field on the struct is aligned to the next byte available that is either in alignment with the type of the fields own boudnry from the start of a struct (ie an int64 will be offset 0, 8, 16 ,24 etc from the start of the struct no matter what comes before it) or the boundry of the structs pack size, whichever is smaller.   Unless packsize is set to something non default it would always just be alinged with its own types boundry.  NOTE@!!@#  it is only aligned based on the pack size of its type NOT the size of it overall.  So a decimal (16 bytes) for example is actually 4 int32's so its packsize is only 4.   Note that if you have a struct that has a U128int for example so 16 bytes but it has pack=1 set on it then it is treated with an actual alignment of 1.

*/
namespace TemplateLib.NativeGen;
public class NativeMarshalCalc {
	//can override IntPtr size by adding it as forced
	private static int GetTypeSize<T>() => GetTypeSize(typeof(T));
	private static int GetTypeSize(Type fType) {
		var cached = GetCached(fType);
		if (cached != null)
			return cached.NativeSize;

		if (fType.IsEnum)
			fType = Enum.GetUnderlyingType(fType);

		var itsSize = 0;
		if (!fType.IsGenericType)
			itsSize = Marshal.SizeOf(fType);
		else {
			var genType = fType.GetGenericTypeDefinition();
			throw new NotImplementedException($"Need generic sizing for type: {genType} should add as AddForcedItemInfo override");

		}
		return itsSize;
	}
	static NativeMarshalCalc() {//sadly cant use the disctionary ienumerable of keys constructor not avail in .net 8 frameowkr
		KnownInfo = new();
#if NET7_0_OR_GREATER
		AddForcedItemInfo<UInt128>(16);
		AddForcedItemInfo<Int128>(16);
#endif
#if NET5_0_OR_GREATER
		AddForcedItemInfo(typeof(System.Runtime.Intrinsics.Vector64<>), 8);
		AddForcedItemInfo(typeof(System.Runtime.Intrinsics.Vector128<>), 16);
		AddForcedItemInfo(typeof(System.Runtime.Intrinsics.Vector256<>), 32);
#endif
		AddForcedItemInfo<object>(IntPtr.Size);//assume pointer to
		AddForcedItemInfo<object[]>(IntPtr.Size);//assume pointer to
		AddForcedItemInfo<DateTimeOffset>(2);
		//AddForce<MARSHALLED_UNICODE_STRING>(8),

	}
	static Dictionary<Type, NativeMarshalCalc> KnownInfo;
	public static void AddForcedItemInfo<T>(int ItemSize, int PackSize = -1) => AddForcedItemInfo(typeof(T), ItemSize, PackSize);
	public static void AddForcedItemInfo(Type type, int ItemSize, int PackSize = -1) {
		var kvp = Forced(type, ItemSize, PackSize);
		KnownInfo[kvp.Key] = kvp.Value;
	}
	private static KeyValuePair<Type, NativeMarshalCalc> Forced<T>(int Size, int PackSize = -1) => Forced(typeof(T), Size);
	private static KeyValuePair<Type, NativeMarshalCalc> Forced(Type type, int Size, int PackSize = -1) => new(type, new(type, Size, PackSize == -1 ? Size : PackSize));

	public override string ToString() => $"{OurType} SZ: {NativeSize,2} Pack: {PackSize,2}";
	public NativeMarshalCalc(Type ourType, Type decendentType, int NativeSize, int packSize) : this(ourType) {
		this.LargestDecendentType = decendentType;
		this.CalcPackSize = packSize;
		this.CalcSize = NativeSize;
	}
	public Type OurType;
	public NativeMarshalCalc(Type forcedType, int NativeSize, int packSize) : this(forcedType, forcedType, NativeSize, packSize) { }
	public NativeMarshalCalc(Type ourType) {
		this.OurType = ourType;
		if (ourType.StructLayoutAttribute?.Size > 0 && ForcedSize > 0 == false)
			ForcedSize = ourType.StructLayoutAttribute.Size;
		if (ourType.StructLayoutAttribute?.Pack > 0)
			ForcedPackSize = ourType.StructLayoutAttribute.Pack;
	}
	public NativeMarshalCalc AddChildItem(FieldInfo field, NativeMarshalCalc _child) {
		var child = new WrappedChild(_child, field);
		var explicitAttrib = field.GetCustomAttribute<FieldOffsetAttribute>();//only allowed on explicit layouts so if it exists we can assume its explicit
		var marshalAs = field.GetCustomAttribute<MarshalAsAttribute>();
		var fixedBufferAttrib = field.GetCustomAttribute<FixedBufferAttribute>();
		if (fixedBufferAttrib != null) {
			child = new WrappedChild(GetNativeInformation(fixedBufferAttrib.ElementType), field);
			child.count = fixedBufferAttrib.Length;
		} else if (child.data.OurType == typeof(string)) {
			if (marshalAs?.Value == UnmanagedType.ByValTStr) { // other than ByValTStr though it says it is always a pointer to something
				child = new WrappedChild(GetNativeInformation(typeof(char)), field);
				child.count = marshalAs.SizeConst;
			} else if (child.data.NativeSize != GetTypeSize<IntPtr>())
				throw new Exception("Expected a string to be ptr size? Some reason not???");
			//bstr is 4 bytes for the length of the string(int32), two bytes per character for the strin in UTF16 form,  finally two bytes that are 0's as terminator (but as you have length its not required)
			// When you get a BSTR pointer however it is pointing to the data part of the string right after the length in memory, the system SysStringLen can figure out the length byl ooking at the int right before it
			// you cannot move a BSTR pointer like a normal string ptr as it must point to the start in memory 
			// Technically as a BSTR need not be null terminated it can contain binary data and can even be odd in length (just not genrally)
		} else if (marshalAs != null) {

			switch (marshalAs.Value) {
				case UnmanagedType.LPArray:  //c style array with pointer to first item
					if (child.data.NativeSize != GetTypeSize<IntPtr>())
						throw new Exception("Expected an array to be pointer size ? Some reason not???");
					break;
				case UnmanagedType.ByValArray:
					child.count = marshalAs.SizeConst;
					break;

			}
		}
		if (explicitAttrib != null)
			child.ForcedOffset = explicitAttrib.Value;

		if (child.data.CalcPackSize > CalcPackSize) {
			CalcPackSize = child.data.CalcPackSize;
			LargestDecendentType = child.data.OurType;
		}
		foreach (var itm in child.data.DependantTypes)
			DependantTypes.Add(itm);
		DependantTypes.Add(child.data.OurType);
		
		Children.Add(child); //we can't calculate our size until we know our pack size.
							 //CalcSize += child.OurSize * ArrayCnt;
		if (IsInvalid || !child.data.IsInvalid)
			return this;
		IsInvalid = true;
		InvalidCausedBy = child.data.InvalidCausedBy;
		return this;
	}
	public class WrappedChild {
		/// <summary>
		/// field is just taken for info not used
		/// </summary>
		/// <param name="data"></param>
		/// <param name="field"></param>
		public WrappedChild(NativeMarshalCalc data, FieldInfo field) {
			this.data = data;
			this.field = field;
			var isBackingMatch = BackingFieldNameRegex.Match(this.field.Name);
			if (isBackingMatch.Success)
				FieldName = isBackingMatch.Groups["fieldName"].Value;
			else
				FieldName = field.Name;

		}
		private static Regex BackingFieldNameRegex = new(@"<(?<fieldName>[^>]+)>k__BackingField");
		public FieldInfo field;
		public string FieldName;
		public override string ToString() => $"{FieldName,15} ({data.OurType.Name,10}) offset: {Offset,3} Size: {TotalSize,3}";
		public NativeMarshalCalc data;
		public int count = 1;
		public int? ForcedOffset;
		public int TotalSize => data.NativeSize * count;
		public int Offset;
		/// <summary>
		/// Offset if not forced
		/// </summary>
		public int NaturalOffset;
		public int NoPadOffset;//if there was 0 padding between the last var and this one, needed for 010 editor

	}
	public List<WrappedChild> Children = new();
	public HashSet<Type> DependantTypes = new();
	public NativeMarshalCalc InvalidCausedBy { get; private set; }
	public string InvalidMsg { get; private set; }
	public Type LargestDecendentType { get; private set; }
	public int CalcSize { get; private set; }
	public int EndPadding { get; private set; }
	public int? ForcedSize { get; private set; }
	public int? ForcedPackSize { get; private set; }
	public int NativeSize => ForcedSize > CalcSize == true ? ForcedSize.Value : CalcSize;
	public int PackSize => IsPackSizedForced ? ForcedPackSize.Value : CalcPackSize;
	public bool IsPackSizedForced => (ForcedPackSize < CalcPackSize) == true;
	public int CalcPackSize { get; private set; }
	public bool IsInvalid { get; private set; }
	public NativeMarshalCalc SetCauseOfInvalid(String msg) {
		IsInvalid = true;
		InvalidCausedBy = this;
		InvalidMsg = msg;
		return this;
	}
	public NativeMarshalCalc SetUsAsFinal(int OurSize, int PackSize = -1) {
		if (PackSize == -1)
			PackSize = OurSize;
		this.CalcSize = OurSize;
		this.CalcPackSize = PackSize;
		return this;
	}

	private static int CalcItemOffset(int CurOffset, int ItemPackSize, int ParentPackSize) {
		var alignSize = Math.Min(ItemPackSize, ParentPackSize);
		if (alignSize == 0)//for 0 sized items
			alignSize = 1;
		var offAmt = CurOffset % alignSize;
		if (offAmt != 0)
			CurOffset += alignSize - offAmt;
		return CurOffset;
	}
	public class OverlapGroup {
		public WrappedChild[] children;
	}
	public void PrintOverlaps() {
		var found = FindOverlaps();
		if (found.Any())
			Debug.WriteLine($"Overlaps for struct: {OurType}");
		foreach (var itm in found) {
			Debug.WriteLine($"  -----");
			foreach (var child in itm.children.OrderBy(a=>a.Offset))
				Debug.WriteLine($"\t  {child.Offset,3} -> {child.Offset+child.TotalSize,3} -- {child.FieldName}");
		}
	}
	public IEnumerable<OverlapGroup> FindOverlaps() {
		var ret = new List<OverlapGroup>();
		for (var x = 0; x < NativeSize; x++) {
			var atSpot = Children.Where(c => c.Offset == x);
			if (atSpot.Any()) {
				var overlapsWith = Children.Where(c => c.Offset < x && (c.Offset + c.TotalSize) > x);
				var all = atSpot.Union(overlapsWith).ToArray();
				if (all.Length > 1)
					ret.Add(new OverlapGroup { children = all });
			}
		}
		var smaller = ret.Where(small => ret.Any(large => large != small && small.children.All(large.children.Contains))).ToArray();
		foreach (var s in smaller)
			ret.Remove(s);
		return ret;
	}
	private void SetChildOffset(WrappedChild child, ref int CurOffset, bool ForNaturalCalc = false) {
		if (child.Offset != 0 && !ForNaturalCalc)
			throw new Exception("UM WHAT???");


		if (!ForNaturalCalc && child.ForcedOffset != null)//forced always wins, also all items must be forcedso no need to worry about not changing curoffset
			child.Offset = child.ForcedOffset.Value;
		else {
			var calcOffset = CalcItemOffset(CurOffset, child.data.PackSize, PackSize);
			if (ForNaturalCalc) {
				child.NoPadOffset = CurOffset;
				child.NaturalOffset = calcOffset;
				//to find overlaps use the FindOverlaps function
				CurOffset = child.Offset;
			} else
				CurOffset = child.Offset = calcOffset;

			CurOffset += child.TotalSize;
		}
	}
	public void Done() {
		int CurOffset = 0;
		Children.ForEach(child => SetChildOffset(child, ref CurOffset));
		if (CalcSize == 0 && Children.Count > 0)
			CalcSize = Children.Max(a => a.Offset + a.TotalSize);
		CurOffset = CalcSize;
		var fauxNextOffset = CalcItemOffset(CurOffset, PackSize, PackSize);//finally we are aligning ourselves by asking where the next item would go, 
		var targetSize = CalcSize;
		if (ForcedSize > CalcSize)
			targetSize = ForcedSize.Value;
		else
			targetSize = fauxNextOffset;

		if (targetSize != CalcSize) {
			EndPadding = targetSize - CalcSize;
			ForcedSize = targetSize;

		}

		//alr now lets calculate the natural offsets
		var sortedKids = Children.OrderBy(a => a.Offset).ToList();
		CurOffset = 0;
		sortedKids.ForEach(child => SetChildOffset(child, ref CurOffset, true));
	}

	private static NativeMarshalCalc GetCached(Type type) => (KnownInfo.TryGetValue(type, out var ourSize) || (type.IsGenericType && KnownInfo.TryGetValue(type.GetGenericTypeDefinition(), out ourSize))) ? ourSize : null;
	public static NativeMarshalCalc GetNativeInformation(Type type) { //, int arraySize = -1
																	  //Marshalling can override these things for the field itself
		NativeMarshalCalc ourSize = GetCached(type);
		if (ourSize != null)
			return ourSize;
		ourSize = new(type);
		try {
			var debugTypeName = type.FullName;

			if (type == typeof(string)) {
				return ourSize.SetUsAsFinal(GetTypeSize<IntPtr>());
			}
			if (type.IsAbstract)
				return ourSize.SetCauseOfInvalid("IsAbstract");

			if (type.IsArray)//ets assume its actually always a pointer ot an array if not marshalled per above
				return ourSize.SetUsAsFinal(GetTypeSize<IntPtr>());
			else if (type.IsValueType == false) {
				if (type.IsPointer)
					return ourSize.SetUsAsFinal(GetTypeSize<IntPtr>());
				return ourSize;
			}

			if (!type.IsPrimitive) {
				var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (fields.Count() > 0) {
					foreach (var field in fields)
						ourSize.AddChildItem(field, GetNativeInformation(field.FieldType));

					return ourSize;
				} else
					return ourSize.SetUsAsFinal(0, 1);//this probably should only be empty structs basically
			}
			//we are a primative so easy enough just get our size
			return ourSize.SetUsAsFinal(GetTypeSize(type));
		} finally {
			KnownInfo[type] = ourSize;
			ourSize.Done();
		}

	}

	public bool MatchesMartialer() {
		if (OurType.IsGenericType)//marshaller doesnt handle generics
			return true;
		if (OurType.IsEnum)
			OurType = OurType.GetEnumUnderlyingType();
		if (OurType.IsPointer)
			OurType = typeof(IntPtr);
		var marshalSize = Marshal.SizeOf(OurType);
		var marshalPackSize = MartialerGetPackSize(OurType);
		return NativeSize == marshalSize && PackSize == marshalPackSize;

	}
#pragma warning disable CS0649 // Field 'NativeMarshalCalc.HackDetectPackSizeStruct<T>._' is never assigned to, and will always have its default value 0
	private struct HackDetectPackSizeStruct<T> { public byte _; public T f2; }
#pragma warning restore CS0649 // Field 'NativeMarshalCalc.HackDetectPackSizeStruct<T>._' is never assigned to, and will always have its default value 0
	public static int MartialerGetPackSize<T>() => _MartialerGetPackSize(typeof(HackDetectPackSizeStruct<T>));
	public static int MartialerGetPackSize(Type type) => _MartialerGetPackSize(typeof(HackDetectPackSizeStruct<>).MakeGenericType(type));
	private static int _MartialerGetPackSize(Type HackStructType) => (int)Marshal.OffsetOf(HackStructType, nameof(HackDetectPackSizeStruct<byte>.f2));
}
