#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include "Log.h"

// Folder ACL (set by setup) lets audiodg's restricted token write here.
static const wchar_t kLogPath[] = L"C:\\ProgramData\\Ecoar\\apo.log";
static const LONGLONG kMaxLogBytes = 256 * 1024;

void LogLine(const char* fmt, ...) {
    char line[512];
    SYSTEMTIME t; GetLocalTime(&t);
    int n = snprintf(line, sizeof(line), "%02u:%02u:%02u.%03u [%lu] ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds, GetCurrentProcessId());
    va_list a; va_start(a, fmt);
    int m = vsnprintf(line + n, sizeof(line) - n - 2, fmt, a);
    va_end(a);
    if (n < 0 || m < 0) return;
    n += m;
    if (n > (int)sizeof(line) - 2) n = (int)sizeof(line) - 2;
    line[n++] = '\n';

    HANDLE h = CreateFileW(kLogPath, FILE_APPEND_DATA | FILE_WRITE_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    LARGE_INTEGER size;
    if (GetFileSizeEx(h, &size) && size.QuadPart > kMaxLogBytes) {   // truncate instead of growing forever
        SetFilePointer(h, 0, nullptr, FILE_BEGIN);
        SetEndOfFile(h);
    }
    SetFilePointer(h, 0, nullptr, FILE_END);
    DWORD w; WriteFile(h, line, n, &w, nullptr);
    CloseHandle(h);
}
