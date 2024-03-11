# 010 Template Generator
This makes it easy to take c# structures and export them into templates for the [010 Hex Editor](https://www.sweetscape.com/010editor/).  It recursively builds c# types into templates as well.  This allows very complicated multi-tier classes/structs to be transformed. It works to support in-memory structures as well meaning padding is considered.  As it generates the templates automatically the templates are not designed to be super human-readable.  Instead it has the power of annotating them with debug information like the expected offsets, memory locations related to jumps, and some other features (see CStructGen.CONF_OPT for all options).

In general if you need a 010 Template you are welcome to email me the c# structures and I can generate it if you do not want to figure out this tool:)

See the [Generated](https://github.com/mitchcapper/sc_010_templates/Generated) folder for some examples of generated types.

## Disclaimer

This wasn't planned to be made public in its current form.  It was done by request. To simplify the amount of code I duplicated into this project some items were pulled/throw not implemented exceptions.  Some of this code is used not just for 010 template generation but for my own runtime use in live inspecting malware processes so may not make sense for just this tool.  There are some Process structure samples referenced but commented out.  They are largely not included as it adds dozens of different additional structures most people don't need.  


## Usage

CStructGen.CONF_OPT has the default options.  Most can be inferred from their code usage.   One I will mention is the StructMode option.  It allows you to pick how the generator should reference nested structures.  The 3 modes are:

- StructModeAtEnd -- All structures for a type are listed at the very end of the type.
- StructModeExternal -- Flat structure tree, there are no embedded structures they are all top level.  This is a bit interesting when you have multiple embedded structures of the same type, but it works:)
- StructModeInline -- The structure is declared exactly where it occurs in memory

Technically you can include multiple of them.  This makes the template longer, but allows you at runtime (in 010 editor that is) to switch between them just by toggling the global STRUCT_MODE setting for the template.  It makes templates much uglier though, so by default the templates I submit I normally leave in AtEnd mode.  If you only have one all the struct mode checks in the final template are removed (so cleaner).  See `RunningProcessAllStruct.bt` for an example of all modes in one file. 

By default it is designed for in-memory structures and handles padding as much.  If the structures are defined with padding but are being used in a situation where the padding does not apply one could easily short circuit the `void Pad(int bytes)` function in hex010helpers.c to ignore all pads.

It doesn't really have a way of knowing what types are native C types/010 types unless you tell it.  To Alias one or more c# types to their C types a call like `AddCType("UINT64", typeof(long), typeof(ulong), typeof(long));` is done.  The basic valuetypes in c# are already added but if you have other classes/structs/etc that exist in 010 already you can use that method to add them.