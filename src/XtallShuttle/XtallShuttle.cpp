#define WIN32_LEAN_AND_MEAN             // Exclude rarely-used stuff from Windows headers
#include "targetver.h"
#include <windows.h>
#include <shellapi.h>
#include <stdlib.h>
#include <malloc.h>
#include <memory.h>
#include <tchar.h>
#include <stdio.h>
#include "resource.h"

static HINSTANCE g_hInstance;

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

void Throw(DWORD errorCode, LPCSTR context, int code)
{
	static char messageBuffer[2000] = "";

	char systemMessageBuffer[1000] = "(no message found)";
	FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM, NULL, errorCode, 0, systemMessageBuffer, sizeof(systemMessageBuffer) - 1, NULL);

	sprintf_s(messageBuffer, "Cannot install this application: while %s, encountered this error: %s (%d)", context, systemMessageBuffer, errorCode);

	throw error(messageBuffer, code);
}

template<typename T>
T CHECK(T value, T badvalue, LPCSTR context, int code)
{
	static char messageBuffer[2000] = "";

	if (value == badvalue)
	{
		DWORD lerr = GetLastError();
		Throw(lerr, context, code);
	}
	return value;
}

bool RunEmbeddedExecutable(LPCSTR resourceName, LPCSTR parameters)
{
	HGLOBAL passengerHandle = NULL;
	HANDLE exeFileHandle = NULL;

	try
	{
		HRSRC passengerInfo = CHECK( FindResource(g_hInstance, resourceName, "EXE"), (HRSRC)NULL, "finding embedded executable", 1 );
 
		passengerHandle = CHECK( LoadResource(g_hInstance, passengerInfo), (HGLOBAL)NULL, "loading embedded executable", 2 );

		void *data = CHECK( LockResource(passengerHandle), (void *)NULL, "locking embedded executable", 3 );

		DWORD size = CHECK( SizeofResource(g_hInstance, passengerInfo), (DWORD)0, "getting size of embedded executable", 4 );

		char tempPath[MAX_PATH];
		CHECK( GetTempPath(sizeof(tempPath) - 1, tempPath), (DWORD)0, "locating the temp folder", 10 );
		char tempFilename[MAX_PATH];
		CHECK( GetTempFileName(tempPath, TEXT("PASS"), 0, tempFilename), (UINT)0, "getting a temp executable filename", 5 );
		strcat_s(tempFilename, ".exe");

		exeFileHandle = CHECK( CreateFile((LPTSTR) tempFilename, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL), INVALID_HANDLE_VALUE,
			"creating a temp executable file", 6 );

		DWORD written = 0;
		CHECK( WriteFile(exeFileHandle, data, size, &written, NULL), FALSE, "writing embedded executable to temp executable file", 7 );
		CloseHandle(exeFileHandle);
		exeFileHandle = NULL;

		CloseHandle(exeFileHandle);
		exeFileHandle = NULL;
		FreeResource(passengerHandle);
		passengerHandle = NULL;

		SHELLEXECUTEINFO sei;
		memset(&sei, 0, sizeof(sei));
		sei.cbSize = sizeof(sei);
		sei.fMask = SEE_MASK_NOASYNC;
		sei.lpVerb = "open";
		sei.lpFile = tempFilename;
		sei.lpParameters = parameters;
		sei.nShow = SW_SHOWNORMAL;

		return ShellExecuteEx(&sei) != FALSE;
	}
	catch (...)
	{
		if (exeFileHandle)
			CloseHandle(exeFileHandle);
		if (passengerHandle)
			FreeResource(passengerHandle);
		throw;
	}
}

bool TestFramework()
{
	HKEY frameworkVersionKey = NULL;

	try
	{
		// verify that the correct version of .NET framework is installed (for XtallLib)
		DWORD err = RegOpenKeyEx(HKEY_LOCAL_MACHINE, "SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full", 0, KEY_READ, &frameworkVersionKey);
		if (ERROR_FILE_NOT_FOUND != err && ERROR_SUCCESS != err)
			Throw(err, "testing for the presence of the .NET Framework", 100);

		if (err == ERROR_SUCCESS)
		{
			char version[1000];
			DWORD versionSize = sizeof(version);
			DWORD versionType = REG_SZ;
			err = RegQueryValueEx(frameworkVersionKey, "Version", NULL, &versionType, (LPBYTE) version, &versionSize);
			if (ERROR_FILE_NOT_FOUND != err && ERROR_SUCCESS != err)
				Throw(err, "testing the version of the .NET Framework", 101);
		}

		CloseHandle(frameworkVersionKey);

		return (err == ERROR_SUCCESS);
	}
	catch (...)
	{
		if (frameworkVersionKey != NULL)
			CloseHandle(frameworkVersionKey);
		throw;
	}
}

void EnsureFramework()
{
	HKEY frameworkVersionKey = NULL;

	try
	{
		if (!TestFramework())
		{
			RunEmbeddedExecutable("IDR_FRAMEWORK", "/passive /showfinalerror /norestart");
		}

		if (!TestFramework())
			throw error("Cannot start this application: attempted to install the .NET Framework, but it did not succeed.", 103);
	}
	catch (...)
	{
		if (frameworkVersionKey != NULL)
			CloseHandle(frameworkVersionKey);
		throw;
	}
}

int APIENTRY _tWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPTSTR lpCmdLine, int nCmdShow)
{
	UNREFERENCED_PARAMETER(hPrevInstance);
	UNREFERENCED_PARAMETER(lpCmdLine);
	g_hInstance = hInstance;

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
		EnsureFramework();

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

		RunEmbeddedExecutable("IDR_PASSENGER", luggage);

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
