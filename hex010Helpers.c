#include "hex010Helpers.h"
typedef uint32 P_X86;
typedef uint64 P_X64;

#include "x64Detect.c"

//#ifdef IS_64_BIT
//typedef P_X64 P_C;
//#define ReadUInt_C ReadUInt64
//#else
//typedef P_X86 P_C;
//#define ReadUInt_C ReadUInt
//#endif

typedef P_X64 P_C;
uint64 ReadUInt_C(uint64 address) {
	if (IsX64())
		return ReadUInt64(address);
	else
		return (uint64)ReadUInt(address);
}


OffsetClear();// YES NEEDED EVEN THOUGH LOOKS LIKE NOT IT REALLY IS
typedef local struct {
	string desc;
	string type;
	string comment;
	int offset;
	P_C start;
	BOOL is_list_entry_grp;
} EXTERNAL_DECLARE;
if (STRUCT_MODE == STRUCT_External)
	local EXTERNAL_DECLARE externals[MAX_EXTERNAL_DECLARE];
else
	local EXTERNAL_DECLARE externals[0];
local uint64 CUR_EXTERNAL_POS = 0;

typedef byte pbyte <hidden = true>;
void Pad(int bytes) {
	FSkip(bytes);
	//pbyte arr[bytes];
	//struct{pbyte arr[bytes];}_;
}
//#ifdef IS_64_BIT
//const int PEB_OFFSET_FROM_HEAP_START = 0;
//const int HEAP_OFFSET_IN_PEB = 0x30;
//#else
//const int PEB_OFFSET_FROM_HEAP_START = 4096;//random brute
//const int HEAP_OFFSET_IN_PEB = 0x18;
//#endif //  IS_64_BIT
local int PEB_OFFSET_FROM_HEAP_START;
local int HEAP_OFFSET_IN_PEB;
Printf("Is process x64: %i\n", IsX64());
if (IsX64()) {
	PEB_OFFSET_FROM_HEAP_START = 0;
	HEAP_OFFSET_IN_PEB = 0x30;
}
else {
	PEB_OFFSET_FROM_HEAP_START = 4096;//random brute
	HEAP_OFFSET_IN_PEB = 0x18;
}



string PrintAddy(P_C addy) {
	local P_C locVal = ProcessHeapToLocalAddress(addy);
#ifdef IS_64_BIT
	return Str("%Lu(%Lu/0x%x)", addy, locVal, addy);
#else
	return Str("%u(%u/0x%Lx)", addy, locVal, addy);
#endif // IS_64_BIT
}
P_C FindPEB() {

	local int heapCnt = ProcessGetNumHeaps();
	local int x;
	local int y;
	local P_C startAddy;
	local P_C pebAddy;
	local P_C maybeHeapAddyInPEB;
	local P_C localMaybeHeapAddyInPEB;
	local P_C maybeAHeapStartAddy;
	for (x = 0; x < heapCnt; x++) {
		startAddy = ProcessGetHeapStartAddress(x);
		pebAddy = startAddy + PEB_OFFSET_FROM_HEAP_START;
		maybeHeapAddyInPEB = pebAddy + HEAP_OFFSET_IN_PEB;
		localMaybeHeapAddyInPEB = ProcessHeapToLocalAddress(maybeHeapAddyInPEB);
		if (localMaybeHeapAddyInPEB >= FileSize())
			continue;
		maybeAHeapStartAddy = ReadUInt_C(localMaybeHeapAddyInPEB);
		if (FIND_PEB_DEBUG)
			Printf("At heap starting at: %s (peb at %s) reading local addy %s to get it maybe heap loc was: %s in module: %i for a possible heap at: %s\n", PrintAddy(startAddy), PrintAddy(pebAddy), PrintAddy(localMaybeHeapAddyInPEB), PrintAddy(maybeHeapAddyInPEB), x, PrintAddy(maybeAHeapStartAddy));
		for (y = 0; y < heapCnt; y++) {
			if (ProcessGetHeapStartAddress(y) == maybeAHeapStartAddy && startAddy != maybeAHeapStartAddy) {
				Printf("Found the PEB start at: %s and the heap start at: %s\n", PrintAddy(pebAddy), PrintAddy(maybeAHeapStartAddy));
				return pebAddy;
			}
		}
	}
	Printf("PEB not found maybe the process is not this x86/x64 bit?");
	return 0;
}

int PointerJump(string desc, P_C JumpTo) {
	return CondPointerJump(desc, JumpTo, 1);
}
wstring OurReadString(string desc, P_C StringAddress, int MaxLen) {
	local wstring rstr = L"";
	if (MaxLen == 0)
		return rstr;
	rstr = ReadWString(ProcessHeapToLocalAddress(StringAddress), MaxLen);
	if (VERBOSE_READ_STRING)
		Printf("Read string: %s from address: %s (%s)\n", rstr, PrintAddy(StringAddress), desc);
	return rstr;
}
int HowManyItemsInListEntry(P_C StopAtEntryPointer, P_C NextEntryPointer) {
	local P_C startPos = FTell();
	local int count = 0;
	while (TRUE) {
		NextEntryPointer = GoToNextItemInListEntry("", StopAtEntryPointer, NextEntryPointer, 0);
		//Printf("Stopping at: %s currently at: %s count: %i", PrintAddy(StopAtEntryPointer), PrintAddy(NextEntryPointer), count);
		if (NextEntryPointer == -1)
			break;
		count++;
	}
	FSeek(startPos);
	return count;
}
P_C GoToNextItemInListEntry(string desc, P_C StopAtEntryPointer, P_C NextEntryPointer, int ItemOffsetFromPointer) {
	//Printf("Stopping at: %s currently at: %s", PrintAddy(StopAtEntryPointer), PrintAddy(NextEntryPointer));
	if (NextEntryPointer == 0 || NextEntryPointer == StopAtEntryPointer)
		return -1;
	if (ItemOffsetFromPointer > 0) {
		Printf("GoToNextItemInListEntry: This is wrong ItemOffsetFromPointer should almost certainly be a negative number for a LIST_ENTRY");
		return -1;
	}

	if (!PointerJump(desc, NextEntryPointer))
		return -1;
	local P_C nextPtr = ReadUInt_C(FTell());
	if (ItemOffsetFromPointer != 0)
		FSkip(ItemOffsetFromPointer);
	
	return nextPtr;
}
int CondPointerJump(string desc, P_C JumpTo, int MustNotBeZero) {
	if (JumpTo == 0 || MustNotBeZero == 0)
		return 0;
	local P_C ConvertedAddress = ProcessHeapToLocalAddress(JumpTo);
	local P_C maxLocalAddy = FileSize();
	if (VERBOSE_PRINT_DUMP && Strlen(desc) != 0)
		Printf("Jumping from %s => %s for: %s\n", PrintAddy(ProcessLocalToHeapAddress(FTell())), PrintAddy(JumpTo), desc);
	local P_C curPos;

	if (ConvertedAddress < maxLocalAddy)
		FSeek(ConvertedAddress);
	else
		Printf("Warning Invalid Seek attempted current pos is %Lu (%Lu) jumping to memory address %Lu (%Lu) for %s max local addy is: %Lu jumping %Lu over\n", ProcessLocalToHeapAddress(FTell()), FTell(), JumpTo, ConvertedAddress, desc, maxLocalAddy, (maxLocalAddy - ConvertedAddress));

	return 1;
}
