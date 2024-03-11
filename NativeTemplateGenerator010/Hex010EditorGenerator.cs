using System;
using System.IO;

namespace TemplateLib.NativeGen {
	public class Hex010EditorGenerator
    {
        //private static void AddDefaults(CStructGen gen) {
        //	gen.PromoteTypes(typeof(UIntPtr64), typeof(HANDLE64), typeof(UIntPtr32), typeof(HANDLE32), typeof(UNICODE_STRING32), typeof(UNICODE_STRING64), typeof(NTSTATUS), typeof(BOOLEAN), typeof(LIST_ENTRY64), typeof(LIST_ENTRY32), typeof(LARGE_INTEGER));

        //}
        static Hex010EditorGenerator()
        {
            
        }

		public enum BITNESS_MODE {
			RUNTIME_DETECT,
			x86,
			x64,
			STRUCT_DETECT
		}
        public static CStructGen.GenResult GenerateTemplate(Type type, CStructGen.CStructMissingTypeAction missingAction = CStructGen.CStructMissingTypeAction.AddRecursive, BITNESS_MODE bit_mode = BITNESS_MODE.STRUCT_DETECT,Action<CStructGen> OnStructGenCreate=null, params Type[] addlTypes)
        {

            if (bit_mode == BITNESS_MODE.STRUCT_DETECT)
            {
				if (typeof(IStructTargetApp64).IsAssignableFrom(type))
					bit_mode = BITNESS_MODE.x64;
				else if (typeof(IStructTargetApp32).IsAssignableFrom(type))
					bit_mode = BITNESS_MODE.x86;
				else
					throw new Exception("Please pass the target type bitness as the type passed in does not implmenet IStructTargetApp* or set to runtime detect");
            }
			bool? bitmode = null;//runtime detect
			if (bit_mode == BITNESS_MODE.x64)
				bitmode = true;
			else if (bit_mode == BITNESS_MODE.x86)
				bitmode = false;
            var gen = new CStructGen(bitmode, type);
			OnStructGenCreate?.Invoke(gen);
			if (addlTypes != null) {
				foreach (var typ in addlTypes) {
					gen.AddToTypesToGenerate(typ);
				}
			}
            gen.MissingAction = missingAction;
            //AddDefaults(gen);
            gen.CStructGenerate();
            //gen.PromoteTypes(typeof(UIntPtr64), typeof(HANDLE64), typeof(UIntPtr32), typeof(HANDLE32), typeof(UNICODE_STRING32), typeof(UNICODE_STRING64), typeof(NTSTATUS), typeof(BOOLEAN), typeof(LIST_ENTRY64), typeof(LIST_ENTRY32), typeof(LARGE_INTEGER));
            return gen.GenerateFinal();




        }
    }
}
