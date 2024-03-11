OffsetClear();
FSeek(0);


typedef enum<WORD>
{
    IMAGE_MACHINE_UNKNOWN  = 0,
    I386      = 0x014c,  // Intel 386 or later processors and compatible processors
    AMD64     = 0x8664  // x64
} IMAGE_MACHINE <comment="WORD">;

typedef struct
{
    IMAGE_MACHINE    Machine                    <fgcolor=cPurple,format=hex,comment="WORD">;
} IMAGE_FILE_HEADER;


typedef struct
{
    DWORD Signature <format=hex,comment="IMAGE_NT_SIGNATURE = 0x00004550">;
    IMAGE_FILE_HEADER FileHeader;

} IMAGE_NT_HEADERS;


typedef struct
{
    WORD   MZSignature              <comment="IMAGE_DOS_SIGNATURE = 0x5A4D",format=hex>;
    WORD   UsedBytesInTheLastPage   <comment="Bytes on last page of file">;
    WORD   FileSizeInPages          <comment="Pages in file">;
    WORD   NumberOfRelocationItems  <comment="Relocations">;
    WORD   HeaderSizeInParagraphs   <comment="Size of header in paragraphs">;
    WORD   MinimumExtraParagraphs   <comment="Minimum extra paragraphs needed">;
    WORD   MaximumExtraParagraphs   <comment="Maximum extra paragraphs needed">;
    WORD   InitialRelativeSS        <comment="Initial (relative) SS value">;
    WORD   InitialSP                <comment="Initial SP value">;
    WORD   Checksum                 <comment="Checksum">;
    WORD   InitialIP                <comment="Initial IP value">;
    WORD   InitialRelativeCS        <comment="Initial (relative) CS value">;
    WORD   AddressOfRelocationTable <comment="File address of relocation table">;
    WORD   OverlayNumber            <comment="Overlay number">;
    WORD   Reserved[4]              <comment="Reserved words">;
    WORD   OEMid                    <comment="OEM identifier (for OEMinfo)">;
    WORD   OEMinfo                  <comment="OEM information; OEMid specific">;
    WORD   Reserved2[10]            <comment="Reserved words">;
    LONG   AddressOfNewExeHeader    <comment="NtHeader Offset",format=hex>;
} IMAGE_DOS_HEADER;

void SeekToImageStart(){
	local int heapCnt = ProcessGetNumHeaps();
	local int x;
	local uint64 modStartAddy;
	local string modName;
	local string OurName = GetFileName();
	local string OutNameParsed;
	local int res = SScanf(OurName,"Process: %s[^(]",OutNameParsed);
	OurName=OutNameParsed;
	//OurName = StrDel(OutNameParsed,Strlen(OutNameParsed)-1,1);

	for (x = 0; x < heapCnt; x++) {
		modName = ProcessGetHeapModule(x);
		if (Stricmp(modName,OurName) == 0){
			modStartAddy = ProcessGetHeapStartAddress(x);
			Printf("Found image base in heap: %s\n",PrintAddy(modStartAddy));

			FSeek(ProcessHeapToLocalAddress(modStartAddy));
			break;
		}
		//Printf("Found heap module: %s our name: %s\n",modName,OurName);
	}
}
if (proc_bitness == BITNESS_Unknown) {
    SeekToImageStart();
    IMAGE_DOS_HEADER DosHeader <bgcolor = cLtPurple>;
    SeekToImageStart();
    FSkip(DosHeader.AddressOfNewExeHeader);
    IMAGE_NT_HEADERS NtHeader <bgcolor = cLtPurple>;
    if (NtHeader.FileHeader.Machine == AMD64)
        proc_bitness = BITNESS_x64;
    else
        proc_bitness = BITNESS_x86;
}

//Printf("Machine type is: %x enum val: %x",NtHeader.FileHeader.Machine, AMD64);
BOOL IsX64(){
    return proc_bitness == BITNESS_x64;
}