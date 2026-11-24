#include "SharedRing.h"
#include <sddl.h>
#include "Log.h"

// SYSTEM + LOCAL SERVICE full, interactive users and admins read/write. No low-integrity label:
// the default (medium) keeps sandboxed low-integrity processes from injecting audio into the mic.
static const wchar_t kSddl[] = L"D:(A;;GA;;;SY)(A;;GA;;;LS)(A;;GRGW;;;IU)(A;;GRGW;;;BA)";

bool SharedRing::OpenOrCreate(uint32_t sampleRate, uint32_t channels) {
    Close();
    PSECURITY_DESCRIPTOR sd = nullptr;
    ConvertStringSecurityDescriptorToSecurityDescriptorW(kSddl, SDDL_REVISION_1, &sd, nullptr);
    SECURITY_ATTRIBUTES sa = {sizeof(sa), sd, FALSE};

    m_mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, sd ? &sa : nullptr, PAGE_READWRITE, 0, (DWORD)kRingBytes, kRingGlobalName);
    if (m_mapping) {
        m_backing = kRingGlobalName;
    } else {
        LogLine("ring: Global mapping failed err=%lu, using file fallback", GetLastError());
        m_file = CreateFileW(kRingFilePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, sd ? &sa : nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (m_file != INVALID_HANDLE_VALUE)
            m_mapping = CreateFileMappingW(m_file, nullptr, PAGE_READWRITE, 0, (DWORD)kRingBytes, nullptr);
        if (!m_mapping) {
            LogLine("ring: file fallback failed err=%lu", GetLastError());
            if (sd) LocalFree(sd);
            Close();
            return false;
        }
        m_backing = kRingFilePath;
    }
    if (sd) LocalFree(sd);

    m_hdr = static_cast<EcoarRingHeader*>(MapViewOfFile(m_mapping, FILE_MAP_ALL_ACCESS, 0, 0, kRingBytes));
    if (!m_hdr) { LogLine("ring: MapViewOfFile failed err=%lu", GetLastError()); Close(); return false; }

    if (m_hdr->magic != kRingMagic || m_hdr->version != kRingVersion) {
        ZeroMemory(m_hdr, kRingHeaderBytes);
        m_hdr->version = kRingVersion;
        m_hdr->capacity = kRingCapacity;
        MemoryBarrier();
        m_hdr->magic = kRingMagic;
    }
    m_hdr->sampleRate = sampleRate;
    m_hdr->channels = channels;
    return true;
}

void SharedRing::Close() {
    if (m_hdr) { UnmapViewOfFile(m_hdr); m_hdr = nullptr; }
    if (m_mapping) { CloseHandle(m_mapping); m_mapping = nullptr; }
    if (m_file != INVALID_HANDLE_VALUE) { CloseHandle(m_file); m_file = INVALID_HANDLE_VALUE; }
    m_backing = L"none";
}
