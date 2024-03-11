#ifndef HEX_010_HELPERS_H
#define HEX_010_HELPERS_H

#define TRUE 1
#define FALSE 0
#define BOOL int
#define MAX_EXTERNAL_DECLARE 600
enum STRUCT_DECLARE_MODE { STRUCT_Inline, STRUCT_AtEnd, STRUCT_External };
enum PROC_BITNESS { BITNESS_Unknown, BITNESS_x64, BITNESS_x86};
local PROC_BITNESS proc_bitness = BITNESS_Unknown;

#ifdef _MSC_BUILD
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#define local
#define uint64 uint64_t
#define uint32 uint32_t
#define wstring wchar_t*
#define string char*
#define Printf printf
#define SPrintf sprintf
#define Strlen strlen
#define Str printf
#define uint32 ULONG;
const BOOL VERBOSE_PRINT_DUMP = TRUE;
const BOOL VERBOSE_READ_STRING = TRUE;
const BOOL FIND_PEB_DEBUG = FALSE;
const STRUCT_DECLARE_MODE STRUCT_MODE = STRUCT_Inline; //declare all structures outside of their parent

#define IS_64_BIT
#endif // _MSC_BUILD

#endif // !HEX_010_HELPERS_H

