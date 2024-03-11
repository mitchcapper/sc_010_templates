using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using TemplateLib.NativeGen;
using Windows.Win32.Foundation;
#if WLIB
using Windows.Win32.System.Threading;
#endif

namespace NativeTemplateGenerator010 {
	internal class Program {
		private static string BASE_TEMPLATE_DIR;
		static async Task Main(string[] args) {
			BASE_TEMPLATE_DIR = args[0];
			structgen();
			Console.WriteLine("Done!");
		}
		private static CStructGen.CONF_OPT ADDL_OPTS = CStructGen.CONF_OPT.StructModeAtEnd;
		private static void AddGenPtrJumpChecks() {
#if WLIB
			CStructGen.AddPointerJumpCheckForType("LockCount != -1", 1, typeof(RTL_CRITICAL_SECTION64), typeof(RTL_CRITICAL_SECTION32));
#endif

		}

		private static void structgen() {
			AddGenPtrJumpChecks();
			var allSructMode = false;
			if (allSructMode)
				ADDL_OPTS |= CStructGen.CONF_OPT.StructModeExternal | CStructGen.CONF_OPT.StructModeInline | CStructGen.CONF_OPT.StructModeAtEnd;
			//SaveToFile(typeof(PROCESS_BASIC_INFORMATION64), Path.Combine(BASE_TEMPLATE_DIR, @$"RunningProcess{(allSructMode?"AllStruct":"")}.bt"), OnStructGenCreate: OnStructGenCreate, PEBAdd: true, addlTypes: typeof(PROCESS_BASIC_INFORMATION32));
		}
		private static void SaveToFile(Type type, string output_file, CStructGen.CStructMissingTypeAction missingAction = CStructGen.CStructMissingTypeAction.AddRecursive, Hex010EditorGenerator.BITNESS_MODE bit_mode = Hex010EditorGenerator.BITNESS_MODE.STRUCT_DETECT, Action<CStructGen> OnStructGenCreate = null, bool PEBAdd = false, params Type[] addlTypes) {
			var res = Hex010EditorGenerator.GenerateTemplate(type, missingAction, bit_mode, OnStructGenCreate, addlTypes);
			if (PEBAdd) {
				res.FinalInit = @"
local P_C peb = FindPEB();
if (PointerJump(""PROCESS_BASIC_INFORMATION.PebBaseAddress(PEB)"",peb)){
	if (proc_bitness == BITNESS_x64){
		PEB64 _PebBaseAddress64;
}
	else {
		PEB32 _PebBaseAddress32;
}
}
";
			}
			var final_str = res.ToString();
			File.WriteAllText(output_file, final_str);

		}

		private static void OnStructGenCreate(CStructGen gen) {
			gen.Config |= ADDL_OPTS;
			gen.Am64Bit = null;
		}
	}
}
