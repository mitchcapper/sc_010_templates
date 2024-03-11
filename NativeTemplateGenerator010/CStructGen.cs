using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;

//using Windows.Win32;
//using Windows.Win32.Foundation;
// Note if you get an unability to load type from assembly  TypeLoadException on a templated struct that refers to itself its a .net bug: https://github.com/dotnet/runtime/issues/6924  it can be resolved by making on empty? https://github.com/sunkin351/GenericStructCyclesAnalyzer
// This is fixed in dotnet 2023-04-04 targetting .net 8 although indirect template loops on empty structs will still screw you.
// https://www.geoffchappell.com/studies/windows/km/ntoskrnl/inc/api/pebteb/peb/index.htm
namespace TemplateLib.NativeGen {

	/// <summary>
	/// Tool to generate C structs from .net code, primarily focused right now on 010 Editor templates.
	/// </summary>
	public class CStructGen {
		public enum CStructMissingTypeAction {
			/// <summary>
			/// If a type is unknown change it to bytes and just use padding for it
			/// </summary>
			BytePad,
			/// <summary>
			/// Recursively add unknown types to the to generate list
			/// </summary>
			AddRecursive,
			/// <summary>
			/// Unknown types are added as if they were known, expected to add elsewhere
			/// </summary>
			AddBlind
		};
		public CStructMissingTypeAction MissingAction;
		[Flags]
		public enum CONF_OPT {
			None,
			CommentStructItemSize = 1 << 0,
			CommentFieldItemSize = 1 << 1,
			CommentFieldItemOffset = 1 << 2,
			CommentPointerItemType = 1 << 3,
			SingleFieldStructsAsTypedefs = 1 << 4,
			SingleFieldStructWithMemberRead = 1 << 5,
			PrintFBeforePtrJump = 1 << 6,
			PrintFFoReadStrings = 1 << 10,
			AssumeNoNaturalOffsets = 1 << 8,
			DebugOutputOverlaps = 1 << 9,
			NO_MAIN_START = 1 << 11,
			StructModeAtEnd = 1 << 12,
			StructModeExternal = 1 << 13,
			StructModeInline = 1 << 14,
			AllComments = CommentStructItemSize | CommentFieldItemSize | CommentFieldItemOffset | CommentPointerItemType,
		};
		public CONF_OPT Config = CONF_OPT.CommentStructItemSize | CONF_OPT.SingleFieldStructsAsTypedefs | CONF_OPT.CommentFieldItemOffset | CONF_OPT.PrintFBeforePtrJump | CONF_OPT.AssumeNoNaturalOffsets | CONF_OPT.DebugOutputOverlaps | CONF_OPT.NO_MAIN_START;
		private HashSet<Type> GeneratedTypes = new();
		private List<Type> ToGenerate = new();
		private StringBuilder OutStr = new();
		private Type CurrentGenType;
		private Type RootType;
		public bool? Am64Bit;
		public bool SingularStructMode => ((Config.HasFlag(CONF_OPT.StructModeInline) ? 1 : 0) + (Config.HasFlag(CONF_OPT.StructModeAtEnd) ? 1 : 0) + (Config.HasFlag(CONF_OPT.StructModeExternal) ? 1 : 0)) == 1;
		public CStructGen(bool? am64Bit, Type rootType) {
			PtrExternalCallLog = new();
			CTypeDictConvertInit();
			GeneratedTypes.Add(rootType);
			ToGenerate.Add(rootType);
			RootType = rootType;
			Am64Bit = am64Bit;
			foreach (var typ in TypeToCType.Keys)
				GeneratedTypes.Add(typ);

		}
		private int ChildCount;
		public void StartNewStruct(Type type) {
			TabDepth = 0;
			ChildCount = 0;
			OutStr.Clear();
			toPointerResolve.Clear();
			if (CurrentGenType != null)
				throw new Exception("Already building a struct why wasnt finished called");
			CurrentGenType = type;
			WriteOut($"typedef struct {{", 1);
		}

		private void WriteOut(string str, int tabChange = 0, bool noNewLine = false) {
			if (tabChange < 0)
				TabDepth += tabChange;
			var tabs = new string('\t', TabDepth);
			if (tabChange > 0)
				TabDepth += tabChange;
			if (str == null)
				return;
			if (!noNewLine)
				str += "\n";
			var nlAtEnd = false;
			if (str.EndsWith("\n")) {
				nlAtEnd = true;
				str = str.Substring(0, str.Length - 1);
			}


			str = str.Replace("\n", "\n" + tabs);
			if (nlAtEnd)
				str += "\n";
			OutStr.Append(tabs + str);
		}
		private int TabDepth = 0;
		private string GetOptionString(string comment = null, string read = null) {
			var ret = "";
			if (!string.IsNullOrWhiteSpace(comment))
				WComma(ref ret, $"comment=\"{comment}\"");
			if (!string.IsNullOrWhiteSpace(read))
				WComma(ref ret, $"read={read}");
			if (string.IsNullOrWhiteSpace(ret))
				return "";
			return $" <{ret}>";
		}
		private void WComma(ref string str, string msg) {
			if (string.IsNullOrWhiteSpace(msg))
				return;
			if (string.IsNullOrWhiteSpace(str) == false)
				str += ", ";
			str += msg;
		}
		private string NumDisplay(int number, int pad = 0, bool includeHex = true, int hexPad = 2) {
			var ret = $"{number.ToString().PadLeft(pad)}";
			if (includeHex)
				ret += $"(0x{number.ToString($"x{hexPad}")})";
			return ret;
		}
		private string GetCommentText(int? size = null, int? offset = null, string pointerTo = null) {
			var ret = "";
			if (size != null)
				WComma(ref ret, "Size: " + NumDisplay(size.Value));
			if (offset != null)
				WComma(ref ret, "Offset: " + NumDisplay(offset.Value));
			if (!string.IsNullOrWhiteSpace(pointerTo))
				WComma(ref ret, $"Ptr: {pointerTo}");
			return ret;
		}
		public void FinishStruct() {
			var readAdd = "";


			var isRootItem = CurrentGenType == RootType;//for the root item we can put the pointers outside of it
			var info = NativeMarshalCalc.GetNativeInformation(CurrentGenType);
			if (Config.HasFlag(CONF_OPT.DebugOutputOverlaps))
				info.PrintOverlaps();
			var bypassStructEnd = false;
			var useOffset = Config.HasFlag(CONF_OPT.AssumeNoNaturalOffsets) ? lastChild.NoPadOffset : lastChild.NaturalOffset;
			if (ChildCount == 1 &&
					Config.HasFlag(CONF_OPT.SingleFieldStructsAsTypedefs) && (isRootItem || toPointerResolve.Count == 0)
					&& useOffset == lastChild.Offset) {
				OutStr.Clear();
				//OutStr.Append($"typedef ");
				//AddCStructLineForField(lastChild,true);
				//OutStr.Append( OutStr.ToString().Trim().TrimEnd(';'));
				var type = GetCNameForType(lastChild.data.OurType).cName;


				var typeDefStr = $"typedef {type} {GetCNameForType(CurrentGenType).cName}";

				OutStr.Append(typeDefStr);
				bypassStructEnd = true;
			} else if (ChildCount == 1 && Config.HasFlag(CONF_OPT.SingleFieldStructWithMemberRead))
				readAdd = $"(this.{lastChild.FieldName})";
			if (string.IsNullOrWhiteSpace(readAdd) && toPointerResolve.Count == 1 &&
				new[] { typeof(UNICODE_STRING64), typeof(UNICODE_STRING32) }.Contains(CurrentGenType) &&
				new[] { typeof(char), typeof(char) }.Contains(toPointerResolve.First().ptrTo)) {
				readAdd = $"OurReadString(\"{GetCNameForType(CurrentGenType).cName}.{toPointerResolve.First().structVarNameWithPtr}\",this.{toPointerResolve.First().structVarNameWithPtr},this.Length )";
				toPointerResolve.Clear();
			}
			var inlineOffsetIncrease = 0;
			foreach (var ptr in toPointerResolve) {//do inlines
				if (isRootItem)
					continue;
				var str = GetPointerResolveStr(ptr, PTR_STRUCT_DECLARE_MODE.Inline);
				var insertPos = inlineOffsetIncrease + ptr.InlineStreamAddPosition;
				if (TypeChecksBeforePointerJumps.TryGetValue(ptr.OrigOnGenType, out var ptrCheck)) {
					if (ptrCheck.lineShift != 0) {
						insertPos = OutStr.ToString().IndexOf("\n", insertPos + 1);

					}
				}
				OutStr.Insert(insertPos, str);
				inlineOffsetIncrease += str.Length;
				if (!PtrExternalCallLog.ContainsKey(ptr.ptrTo))
					PtrExternalCallLog[ptr.ptrTo] = new();
				PtrExternalCallLog[ptr.ptrTo].Add(ptr);
			}

			var comment = GetCommentText(Config.HasFlag(CONF_OPT.CommentStructItemSize) ? info.NativeSize : null);
			var optStr = GetOptionString(comment, readAdd);
			var structEndStr = "";
			if (!bypassStructEnd)
				structEndStr = $"}} {GetCNameForType(CurrentGenType).cName}";

			var mainDeclare = isRootItem ? $"{GetCNameForType(CurrentGenType).cName} _main;\n" : "";
			if (Config.HasFlag(CONF_OPT.NO_MAIN_START))
				mainDeclare = "";
			var StructEnd = $"{structEndStr} {optStr};\n{mainDeclare}";

			if (isRootItem)
				WriteOut(StructEnd, -1);

			if (toPointerResolve.Count > 0) {
				if (Config.HasFlag(CONF_OPT.StructModeAtEnd)) {
					if (!SingularStructMode)
						WriteOut(@"if (STRUCT_MODE == STRUCT_AtEnd){", 1);
					WriteOut(@"local P_C _cur_pos = FTell();");

					foreach (var ptr in toPointerResolve) {
						if (isRootItem && Config.HasFlag(CONF_OPT.NO_MAIN_START))
							continue;
						WriteOut(GetPointerResolveStr(ptr, PTR_STRUCT_DECLARE_MODE.AtEnd, isRootItem ? $"_main." : ""));
					}
					WriteOut("FSeek(_cur_pos);");
					if (!SingularStructMode)
						WriteOut("}", -1); //at end
				}
				if (Config.HasFlag(CONF_OPT.StructModeExternal)) {
					if (!isRootItem) {
						if (!SingularStructMode)
							WriteOut(@"if (STRUCT_MODE == STRUCT_External){", 1);
						foreach (var ptr in toPointerResolve)
							WriteOut(GetPointerResolveStr(ptr, PTR_STRUCT_DECLARE_MODE.External));
						if (!SingularStructMode)
							WriteOut("}", -1);
					}
				}
			}

			if (!isRootItem)
				WriteOut(StructEnd, -1);



			generated.Add(new() { content = OutStr.ToString(), type = info, HasComment = true });

			CurrentGenType = null;
		}


		private List<GeneratedStruct> generated = new();
		private class GeneratedStruct {
			public string content;
			public NativeMarshalCalc type;
			public bool HasComment;
			public override string ToString() =>
				$"Struct for: {type.OurType} depends on: {String.Join(",", type.DependantTypes.Select(a => a.Name))}";

		}
		private static Dictionary<Type, string> TypeToCType;
		public Type NextTypeToGenerate() {
			if (ToGenerate.Count == 0)
				return null;
			var ret = ToGenerate.First();
			ToGenerate.RemoveAt(0);
			return ret;
		}
		private NativeMarshalCalc.WrappedChild lastChild;
		public (string cName, bool isNative) GetCNameForType(Type type) {
			if (type.IsPointer) {
				Debug.WriteLine($"CStructGen.cs::GetCNameForType: Warning asked to get a cname for a pointer to {type} probably need to turn into a proper UIntPtr64 as this will always return an IntPtr which is 64 bit by hard code");
				type = typeof(IntPtr);

			}
			if (TypeToCType.TryGetValue(type, out var varType) || type.IsGenericType && TypeToCType.TryGetValue(type.GetGenericTypeDefinition(), out varType))
				return (varType, true);
			var name = type.Name;
			if (name.Contains("*")) {
				name = name.Replace("*", "").Trim();
				name = "P_" + name;
			}
			return (name, false);
		}
		/// <summary>
		/// may need to handle padding here
		/// </summary>
		public void AddCStructLineForField(NativeMarshalCalc.WrappedChild child, bool suppressComment = false) {
			var useOffset = Config.HasFlag(CONF_OPT.AssumeNoNaturalOffsets) ? child.NoPadOffset : child.NaturalOffset;
			PointerResolveCall? pointerAdded = null;
			var offsetDiff = child.Offset - useOffset;
			if (offsetDiff > 0)//struct{	byte _padd[4];} _;
				OutStr.AppendLine($"\tPad({offsetDiff});\n");

			//OutStr.AppendLine($"\tstruct{{byte _padd[{offsetDiff}];}}_;\n");
			lastChild = child;
			ChildCount++;
			var childConvertedType = child.data.OurType;
			var childOriginalType = child.field.FieldType;
			if (childOriginalType.IsEnum)
				childConvertedType = childOriginalType;

			var arrayEndAdd = "";
			var cNameInfo = GetCNameForType(childConvertedType);
			if (!cNameInfo.isNative) {
				if (MissingAction == CStructMissingTypeAction.AddRecursive) {
					AddToGenTypesIfNeeded(childConvertedType);
				} else if (MissingAction == CStructMissingTypeAction.BytePad) {
					cNameInfo.cName = "byte";
					arrayEndAdd = $"[{child.TotalSize}]";
				} else if (MissingAction == CStructMissingTypeAction.AddBlind) { //we just ad it under the name and assume its 
				}
			}
			var ptrComment = "";
			if (childOriginalType.IsGenericType) {
				//want to use this over OurType as OurType may be overriden by a force
				var typeDef = childOriginalType.GetGenericTypeDefinition();
				if (typeDef == typeof(UIntPtr64<>) || typeDef == typeof(UIntPtr32<>)) {

					var ptToType = childOriginalType.GetGenericArguments()[0];
					NativeMarshalCalc.GetNativeInformation(CurrentGenType).DependantTypes.Add(ptToType);
					AddToGenTypesIfNeeded(ptToType);
					var name = GetCNameForType(ptToType).cName;
					if (Config.HasFlag(CONF_OPT.CommentPointerItemType))
						ptrComment = $"* to {name}";
					toPointerResolve.Add(pointerAdded = new() { structVarNameWithPtr = child.FieldName, ptrTo = ptToType });
#if WLIB
				} else if (typeDef == typeof(ListEntryHead64<>) || typeDef == typeof(ListEntryHead32<>)) {
					var ptToType = childOriginalType.GetGenericArguments()[0];
					NativeMarshalCalc.GetNativeInformation(CurrentGenType).DependantTypes.Add(ptToType);
					AddToGenTypesIfNeeded(ptToType);
					var name = GetCNameForType(ptToType).cName;
					if (Config.HasFlag(CONF_OPT.CommentPointerItemType))
						ptrComment = $"LIST_ENTRY * to {name}";
					if (child.FieldName.Contains("InMemoryOrder"))
						toPointerResolve.Add(pointerAdded = new() { structVarNameWithPtr = $"{child.FieldName}", ptrTo = ptToType, type = POINTER_RESOLV_TYPE.LIST_ENTRY, OffsetAmt = (int)Marshal.OffsetOf(ptToType, "InMemoryOrderLinks").ToInt64() * -1 });
#endif
				}
			}
			if (child.count > 1 && string.IsNullOrWhiteSpace(arrayEndAdd)) {
				cNameInfo.cName = cNameInfo.cName.Replace("[]", "");
				arrayEndAdd = $"[{child.count}]";
			}
			var commentStr = GetCommentText(Config.HasFlag(CONF_OPT.CommentFieldItemSize) ? child.TotalSize : null,
				Config.HasFlag(CONF_OPT.CommentFieldItemOffset) ? child.Offset : null,
				ptrComment);

			var finalOut = GetOptionString(!suppressComment ? commentStr : null);

			OutStr.AppendLine($"\t{cNameInfo.cName} {child.FieldName}{arrayEndAdd}{finalOut};");
			if (pointerAdded != null) {
				pointerAdded.InlineStreamAddPosition = OutStr.Length;
				pointerAdded.OrigOnGenType = CurrentGenType;
			}


		}
		/// <summary>
		/// allows you to add a check that must return true (1) before doing the jump this is inserted into 010 code directly
		/// </summary>
		/// <param name="type"></param>
		/// <param name="boolCheck"></param>
		public static void AddPointerJumpCheckForType(string boolCheck, int lineShift, params Type[] types) {
			foreach (var type in types)
				TypeChecksBeforePointerJumps[type] = new PtrJumpCheck { check = boolCheck, lineShift = lineShift };
		}
		private class PtrJumpCheck {
			public string check;
			public int lineShift;
		}
		private static Dictionary<Type, PtrJumpCheck> TypeChecksBeforePointerJumps = new();
		public static void AddCType(string cTypeOut, params Type[] types) {
			CTypeDictConvertInit();
			foreach (var type in types)
				TypeToCType[type] = cTypeOut;
		}
		public static void ADDCTypePassThrough(params Type[] types) {
			CTypeDictConvertInit();
			foreach (var type in types)
				AddCType(type.Name.ToUpper() == type.Name ? type.Name : type.Name.ToLower(), type);
		}
		private static void CTypeDictConvertInit() {
			if (TypeToCType != null)
				return;

			TypeToCType = new();
			AddCType("char *", typeof(string));
			//_AddCType("UNICODE_STRING", typeof(UNICODE_STRING32), typeof(UNICODE_STRING64));
			AddCType("float", typeof(float));
			AddCType("UINT32", typeof(int), typeof(uint));
			AddCType("UINT64", typeof(long), typeof(ulong), typeof(long));
			AddCType("P_C", typeof(IntPtr));
			AddCType("P_X86", typeof(UIntPtr32<>));
			AddCType("P_X64", typeof(UIntPtr64<>));
			AddCType("P_X86", typeof(UIntPtr32<>));
			AddCType("P_X64", typeof(UIntPtr64));
			AddCType("P_X86", typeof(UIntPtr32));
			AddCType("char", typeof(char));
			ADDCTypePassThrough(typeof(char), typeof(double), typeof(byte), typeof(ushort));
#if WLIB
			AddCType("LIST_ENTRY64", typeof(ListEntryHead64<>));
			AddCType("LIST_ENTRY32", typeof(ListEntryHead32<>));
			AddPointerJumpCheckForType("Length != 0", 0, typeof(UNICODE_STRING32), typeof(UNICODE_STRING64));//technically the other string handler should handle
#endif

		}

		private List<PointerResolveCall> toPointerResolve = new();
		private class PointerResolveCall {
			public string structVarNameWithPtr;
			public Type ptrTo;
			public Type OrigOnGenType;
			public POINTER_RESOLV_TYPE type = POINTER_RESOLV_TYPE.NORMAL_POINTER;
			public int OffsetAmt;
			public int InlineStreamAddPosition;

		}
		enum PTR_STRUCT_DECLARE_MODE { Inline, AtEnd, External }
		private Dictionary<Type, List<PointerResolveCall>> PtrExternalCallLog;
		private string GetPointerResolveStr(PointerResolveCall ptr, PTR_STRUCT_DECLARE_MODE mode, String externalAddPath = "") {
			if (mode == PTR_STRUCT_DECLARE_MODE.Inline && !Config.HasFlag(CONF_OPT.StructModeInline))
				return "";
			if (mode == PTR_STRUCT_DECLARE_MODE.External && !Config.HasFlag(CONF_OPT.StructModeExternal))
				return "";
			if (mode == PTR_STRUCT_DECLARE_MODE.AtEnd && !Config.HasFlag(CONF_OPT.StructModeAtEnd))
				return "";
			var retStr = "";
			var jumpFunc = "PointerJump";
			var jumpDecl = $"{GetCNameForType(ptr.ptrTo).cName} _{ptr.structVarNameWithPtr}";
			var jumpArgList = new List<string>();
			jumpArgList.Add($"\"{GetCNameForType(ptr.OrigOnGenType).cName}.{ptr.structVarNameWithPtr}({GetCNameForType(ptr.ptrTo).cName})\"");
			jumpArgList.Add($"{externalAddPath}{ptr.structVarNameWithPtr}");


			if (ptr.type == POINTER_RESOLV_TYPE.LIST_ENTRY) {
				jumpFunc = "GoToNextItemInListEntry";
				jumpArgList[1] = $"ProcessLocalToHeapAddress(startof({jumpArgList[1]}))";
				jumpArgList.Add($"{externalAddPath}{ptr.structVarNameWithPtr}._Flink");
				jumpArgList.Add(ptr.OffsetAmt.ToString());
			}
			//var noActualDeclare = false;
			//if ((ptr.ptrTo == typeof(Char) || ptr.ptrTo == typeof(char)) && (CurrentGenType == typeof(UNICODE_STRING64) || CurrentGenType == typeof(UNICODE_STRING32)) ) {
			//	jumpFunc = "OurReadString";
			//	jumpArgs += ",Length";
			//	noActualDeclare = true;
			//} else
			string condition = "";
			if (TypeChecksBeforePointerJumps.TryGetValue(ptr.OrigOnGenType, out var jumpData)) {
				condition = jumpData.check;
				jumpArgList.Add($"{condition}");
				jumpFunc = "CondPointerJump";
			}
			var jumpArgs = String.Join(", ", jumpArgList);
			//if (noActualDeclare)
			//	retStr +=$"{jumpFunc}({jumpArgs});\n";

			//else {
			if (mode == PTR_STRUCT_DECLARE_MODE.AtEnd && ptr.type == POINTER_RESOLV_TYPE.LIST_ENTRY) {
				retStr += $@"
local uint64 NextEntryPointer = {jumpArgList[2]};
while(TRUE){{
	NextEntryPointer = GoToNextItemInListEntry("""", {jumpArgList[1]}, NextEntryPointer, {jumpArgList[3]}); //{jumpArgList[0]} don't have description so its silent
	if (NextEntryPointer == -1)
		break;
	{jumpDecl};
}}
";

				var ifAdd = "";
				var ifAddEnd = "";
				if (!SingularStructMode) {
					ifAdd = "\n\tif (STRUCT_MODE == STRUCT_AtEnd){";
					ifAddEnd = "}";
				}
				retStr = "\t" + $@"{ifAdd}
		{retStr.Trim().Replace("\n", "\n\t")}
	{ifAddEnd}
".Trim() + "\n";
				return retStr;
			}
			if (mode == PTR_STRUCT_DECLARE_MODE.External) {
				var offsetAdd = ptr.OffsetAmt != 0 ? $"externals[CUR_EXTERNAL_POS].offset={ptr.OffsetAmt};\n" : "";
				var listEntryAdd = "";
				if (ptr.type == POINTER_RESOLV_TYPE.LIST_ENTRY)
					listEntryAdd = "externals[CUR_EXTERNAL_POS].is_list_entry_grp=TRUE;\n";
				retStr += $@"
externals[CUR_EXTERNAL_POS].desc = {jumpArgList[0]};
externals[CUR_EXTERNAL_POS].type = ""{GetCNameForType(ptr.ptrTo).cName}"";
externals[CUR_EXTERNAL_POS].comment = ""{ptr.structVarNameWithPtr}"";
externals[CUR_EXTERNAL_POS].start = {jumpArgList[1]};
{offsetAdd}{listEntryAdd}CUR_EXTERNAL_POS++;";
				if (String.IsNullOrEmpty(condition) == false) {
					retStr = @$"if ({condition}){{
		{retStr}
}}";
				}
				return retStr;
			}
			retStr += $"if ({jumpFunc}({jumpArgs}))\n";
			retStr += $"\t {jumpDecl};\n";
			//}

			if (mode == PTR_STRUCT_DECLARE_MODE.Inline) {
				var ifAdd = "";
				var ifAddEnd = "";
				if (!SingularStructMode) {
					ifAdd = "\n\tif (STRUCT_MODE == STRUCT_Inline){";
					ifAddEnd = "}";
				}
				retStr = "\t" + $@"{ifAdd}
		local P_C _cp{cpcnt} = FTell();
		{retStr.Trim().Replace("\n", "\n\t\t")}
		FSeek(_cp{cpcnt++});
	{ifAddEnd}
".Trim() + "\n";
			}
			return retStr;
		}
		private int cpcnt = 0;
		public enum POINTER_RESOLV_TYPE { NORMAL_POINTER, LIST_ENTRY }
		/// <summary>
		/// Allows you to force it to generate a specific type
		/// </summary>
		/// <param name="type"></param>
		private void AddToGenTypesIfNeeded(Type type, bool force = false) {
			if (MissingAction != CStructMissingTypeAction.AddRecursive && !force)
				return;
			if (GetCNameForType(type).isNative)
				return;
			if (GeneratedTypes.Add(type))
				ToGenerate.Add(type);
		}
		public void AddToTypesToGenerate(Type type) => AddToGenTypesIfNeeded(type, true);
		//public void PromoteTypes(params Type[] types) {
		//	foreach (var type in types.Reverse()) {
		//		var elem = generated.FirstOrDefault(a => a.type == type);
		//		if (elem == null)
		//			continue;
		//		generated.Remove(elem);
		//		generated.Add(elem);
		//		//generated.Insert(0, elem);
		//	}
		//}
		public class GenResult {
			public string Headers;
			public string GeneratedBody;
			public string FinalInit;
			public string ExternalInits;
			public string FinalSuccessPrint;
			public override string ToString() => $"{Headers}\n{GeneratedBody}\n{FinalInit}\n{ExternalInits}\n{FinalSuccessPrint}\n";
		}
		private bool DependantGenericBypass(Type type) {
			if (type.IsPointer)
				return true;
			if (type.IsGenericType && TypeToCType.ContainsKey(type.GetGenericTypeDefinition()))
				return true;
			return false;
			//type.IsGenericType && TypeToCType.TryGetValue(type.GetGenericTypeDefinition(), out varType)
		}
		public GenResult GenerateFinal() {
			HashSet<Type> OutputTypes = new(TypeToCType.Keys);
			var left = generated.ToList();
			left.Reverse();
			var sb = new StringBuilder();
			while (left.Count > 0) {
				//
				var toOutput = left.Where(a => a.type.DependantTypes.All(a => OutputTypes.Contains(a) || DependantGenericBypass(a))).ToArray();
				if (toOutput.Length == 0) {
					Debug.WriteLine("At a stalemate don't seem to have any other types to output so will just dump the rest");
					toOutput = left.ToArray();
				}
				sb.AppendLine(string.Join("\n", toOutput.Select(a => a.content)));
				foreach (var struc in toOutput) {
					OutputTypes.Add(struc.type.OurType);
					left.Remove(struc);
				};
			}
			var ret = new GenResult();
			ret.GeneratedBody = sb.ToString();
			////const BOOL SAVE_POS_BEFORE_JUMP = {(Config.HasFlag(CONF_OPT.DoSeekResetAfterPtr) ? "TRUE" : "FALSE")};
			var structMode = "STRUCT_Inline";
			if (Config.HasFlag(CONF_OPT.StructModeExternal))
				structMode = "STRUCT_External";
			else if (Config.HasFlag(CONF_OPT.StructModeAtEnd))
				structMode = "STRUCT_AtEnd";
			ret.Headers = @$"
#include ""hex010Helpers.h""
const BOOL VERBOSE_PRINT_DUMP = {(Config.HasFlag(CONF_OPT.PrintFBeforePtrJump) ? "TRUE" : "FALSE")};
const BOOL VERBOSE_READ_STRING = {(Config.HasFlag(CONF_OPT.PrintFFoReadStrings) ? "TRUE" : "FALSE")};
const STRUCT_DECLARE_MODE STRUCT_MODE = {structMode};
const BOOL FIND_PEB_DEBUG = FALSE;
proc_bitness = {(Am64Bit switch { true => "BITNESS_x64", false => "BITNESS_x86", null => "BITNESS_Unknown" })};
#include ""hex010Helpers.c""
";

			OutStr.Clear();
			if (Config.HasFlag(CONF_OPT.StructModeExternal)) {
				WriteOut(@"if (STRUCT_MODE == STRUCT_External){
	local P_C _cur_pos = FTell();", 1);
				WriteOut(@"local int x;
local uint64 NextEntryPointer;//only used for list items
for (x = 0; x < CUR_EXTERNAL_POS;x++){", 1);
				WriteOut(@"if (externals[x].is_list_entry_grp) {", 1);
				WriteOut(@"if (! PointerJump(""Initial list entry fetch"",externals[x].start))
	continue;");
				WriteOut("NextEntryPointer  = ReadUInt_C(FTell());");
				WriteOut("} //list_entry", -1);
				WriteOut("while(TRUE){ //item true loop", 1);
				WriteOut("if (externals[x].is_list_entry_grp){", 1);  //if is listentry
				WriteOut(@"
NextEntryPointer = GoToNextItemInListEntry(externals[x].desc, externals[x].start, NextEntryPointer, externals[x].offset);
if (NextEntryPointer == -1)
	break;
");
				WriteOut(@"} else if (! PointerJump(externals[x].desc,externals[x].start)) //IsListEntryGroup
	break;", -1);
				WriteOut("switch (externals[x].type) {", 1);
				foreach (var itm in PtrExternalCallLog) {
					var typeName = GetCNameForType(itm.Key).cName;
					WriteOut(@$"
case ""{typeName}"":
	{typeName} _{typeName} <comment=(externals[x].comment)>;break;".Trim());
				}
				WriteOut("} //switch end", -1);

				WriteOut(@"if (! externals[x].is_list_entry_grp)
	break;");
				WriteOut("} //while end", -1);
				WriteOut("} //for external loop", -1);
				WriteOut("} //is external", -1);
			}
			ret.ExternalInits = OutStr.ToString();
			ret.FinalSuccessPrint = $"\nPrintf(\"Done success for type: {RootType}\\n\");\n";
			return ret;
		}


		private void Traverse(Type type) {
			StartNewStruct(type);
			var nativeInfo = NativeMarshalCalc.GetNativeInformation(type);
			if (!nativeInfo.MatchesMartialer())
				throw new Exception("we don't match Martialer???");

			foreach (var child in nativeInfo.Children)
				AddCStructLineForField(child);
			FinishStruct();


		}
		public void CStructGenerate() {
			Type curType;
			while ((curType = NextTypeToGenerate()) != null) {
				if (curType.IsEnum)
					AddGeneratedEnum(curType);
				else
					Traverse(curType);
			}
		}
		public void AddGeneratedEnum(Type curType) {
			OutStr.Clear();
			var varType = GetCNameForType(curType.GetEnumUnderlyingType());
			if (!varType.isNative)
				throw new Exception("Don't know how to convert a native enum backing type to c?????");
			var underSize = Marshal.SizeOf(curType.GetEnumUnderlyingType());
			OutStr.Append("enum");
			if (underSize != 4) //assumed int by 010 editor
				OutStr.Append($" <{GetCNameForType(curType.GetEnumUnderlyingType()).cName}>");
			OutStr.Append($" {GetCNameForType(curType).cName}");

			OutStr.Append(" {");
			var isFirst = true;
			var DoneNames = new HashSet<string>();
			foreach (var UniqueName in Enum.GetValues(curType)) {
				var val = Convert.ChangeType(UniqueName, Type.GetTypeCode(curType));
				var name = UniqueName.ToString();
				if (DoneNames.Contains(name))
					continue;
				DoneNames.Add(name);
				if (!isFirst)
					OutStr.Append(",");
				OutStr.Append($" {name}={val}");

				isFirst = false;
			}
			OutStr.AppendLine(" };");
			generated.Add(new() { content = OutStr.ToString(), type = NativeMarshalCalc.GetNativeInformation(curType) });
		}
	}
}

