// Opens the Ecoar ring read-only and checks that an APO is alive (magic + heartbeat moving).
// Exit 0 = ok, 1 = no ring / bad magic / heartbeat stalled.
#include <windows.h>
#include <stdio.h>
#include "../apo/RingLayout.h"
#include "../apo/SharedRing.h"

int main() {
    const wchar_t* backing = kRingGlobalName;
    HANDLE m = OpenFileMappingW(FILE_MAP_READ, FALSE, kRingGlobalName);
    HANDLE f = INVALID_HANDLE_VALUE;
    if (!m) {
        printf("Global mapping not found (err=%lu), trying file\n", GetLastError());
        backing = kRingFilePath;
        f = CreateFileW(kRingFilePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
        if (f == INVALID_HANDLE_VALUE) { printf("no ring: file not found\n"); return 1; }
        m = CreateFileMappingW(f, nullptr, PAGE_READONLY, 0, 0, nullptr);
        if (!m) { printf("no ring: map failed err=%lu\n", GetLastError()); return 1; }
    }
    auto* h = static_cast<const EcoarRingHeader*>(MapViewOfFile(m, FILE_MAP_READ, 0, 0, kRingBytes));
    if (!h) { printf("view failed err=%lu\n", GetLastError()); return 1; }
    long long hb1 = h->heartbeat;
    Sleep(500);
    long long hb2 = h->heartbeat;
    printf("backing=%ls magic=0x%08X version=%u rate=%u channels=%u capacity=%u writeIndex=%lld heartbeat=%lld->%lld\n",
           backing, h->magic, h->version, h->sampleRate, h->channels, h->capacity, (long long)h->writeIndex, hb1, hb2);
    bool ok = h->magic == kRingMagic && hb2 > hb1;
    printf(ok ? "OK: APO alive\n" : "NOT OK: bad magic or heartbeat stalled\n");
    return ok ? 0 : 1;
}
