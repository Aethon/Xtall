#define WIN32_LEAN_AND_MEAN             // Exclude rarely-used stuff from Windows headers
#include "targetver.h"
#include <windows.h>
#include <stdlib.h>
#include <malloc.h>
#include <memory.h>
#include <tchar.h>
#include <stdio.h>
#include "resource.h"

struct error
{
	LPCSTR Message;
	int Code;

	error(LPCSTR message, int code)
	{
		Message = message;
		Code = code;
	}
};

template<typename T>
T CHECK(T value, T badvalue, LPCSTR context, int code)
{
	static char messageBuffer[2000] = "";

	if (value == badvalue)
	{
		DWORD lerr = GetLastError();
		char systemMessageBuffer[1000] = "(no message found)";
		FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM, NULL, lerr, 0, systemMessageBuffer, sizeof(systemMessageBuffer) - 1, NULL);

		sprintf_s(messageBuffer, "Cannot install this application: while %s, encountered this error: %s (%d)", context, systemMessageBuffer, lerr);

		throw error(messageBuffer, code);
	}
	return value;
}

int APIENTRY _tWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPTSTR lpCmdLine, int nCmdShow)
{
	UNREFERENCED_PARAMETER(hPrevInstance);
	UNREFERENCED_PARAMETER(lpCmdLine);

	int result = 100;
	HANDLE thisFileHandle = NULL;
	char *luggage = NULL;
	HRSRC passengerInfo = NULL;
	HGLOBAL passengerHandle = NULL;
	HANDLE exeFileHandle = NULL;
	PROCESS_INFORMATION pi;
	memset(&pi, 0, sizeof(pi));

	try
	{
		char thisFilename[MAX_PATH];
		CHECK( GetModuleFileName(NULL, thisFilename, MAX_PATH), (DWORD)0, "getting the module filename", 11 );

		HANDLE h = CHECK( CreateFile(thisFilename, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL),
			INVALID_HANDLE_VALUE, "opening file for parameters", 12 );

		long parameterSize = 0;
		long parameterOffset = 0;
		long parameterSig = 0;

		CHECK( SetFilePointer(h, -(int)(sizeof(parameterSize) + sizeof(parameterOffset) + sizeof(parameterSig)), 0, FILE_END), (DWORD)-1, "seeking parameter info", 13 );

		DWORD read = 0;
		CHECK( ReadFile(h, &parameterSig, sizeof(parameterSig), &read, NULL), FALSE, "reading parameter signature", 14 );

		if (parameterSig == 0x42000042)
		{
			CHECK( ReadFile(h, &parameterSize, sizeof(parameterSize), &read, NULL), FALSE, "reading parameter size", 15 );
			CHECK( ReadFile(h, &parameterOffset, sizeof(parameterOffset), &read, NULL), FALSE, "reading parameter offset", 16 );
			CHECK( SetFilePointer(h, -parameterOffset, 0, FILE_END), (DWORD)-1, "seeking parameters", 17 );

			luggage = new char[parameterSize + 1];

			CHECK( ReadFile(h, luggage, parameterSize, &read, NULL), FALSE, "reading parameters", 18 );
			luggage[parameterSize] = 0;
		}
		else
		{
			luggage = new char[1];
			*luggage = 0;
		}

		HRSRC passengerInfo = CHECK( FindResource(hInstance, "IDR_PASSENGER", "EXE"), (HRSRC)NULL, "finding passenger resource", 1 );
 
		passengerHandle = CHECK( LoadResource(hInstance, passengerInfo), (HGLOBAL)NULL, "loading passenger resource", 2 );

		void *data = CHECK( LockResource(passengerHandle), (void *)NULL, "locking passenger resource", 3 );

		DWORD size = CHECK( SizeofResource(hInstance, passengerInfo), (DWORD)0, "getting size of passenger resource", 4 );

		char tempPath[MAX_PATH];
		CHECK( GetTempPath(sizeof(tempPath) - 1, tempPath), (DWORD)0, "locating the temp folder", 10 );
		char tempFilename[MAX_PATH];
		CHECK( GetTempFileName(tempPath, TEXT("PASS"), 0, tempFilename), (UINT)0, "getting a temp executable filename", 5 );

		exeFileHandle = CHECK( CreateFile((LPTSTR) tempFilename, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL), INVALID_HANDLE_VALUE,
			"creating a temp executable file", 6 );

		DWORD written = 0;
		CHECK( WriteFile(exeFileHandle, data, size, &written, NULL), FALSE, "writing passenger to temp executable file", 7 );
		CloseHandle(exeFileHandle);
		exeFileHandle = NULL;

		STARTUPINFO sui;
		memset(&sui, 0, sizeof(sui));
		sui.cb = sizeof(sui);

		char args[MAX_PATH * 2];
		CHECK( sprintf_s(args, "\"%s\" %s", tempFilename, luggage), -1, "formatting passenger parameters", 9);

		CHECK( CreateProcess(tempFilename, args, NULL, NULL, FALSE, 0, NULL, NULL, &sui, &pi), FALSE, "creating passenger process", 8 );

		result = 0;
	}
	catch(error e)
	{
		MessageBox(NULL, e.Message, "Error", MB_OK | MB_ICONHAND);
		result = e.Code;
	}
	catch (...)
	{
		MessageBox(NULL, "Cannot install this application: an unknown error occurred", "Error", MB_OK | MB_ICONHAND);
		result = 101;
	}

	if (pi.hThread)
		CloseHandle(pi.hThread);
	if (pi.hProcess)
		CloseHandle(pi.hProcess);
	if (exeFileHandle)
		CloseHandle(exeFileHandle);
	if (passengerHandle)
		FreeResource(passengerHandle);
	if (luggage != NULL)
		delete[] luggage;

	return result;
}
