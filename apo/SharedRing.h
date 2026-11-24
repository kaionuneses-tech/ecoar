#pragma once
#include <windows.h>
#include "RingLayout.h"

// Opens (or creates) the shared ring. First tries Global\EcoarRing-v1; if audiodg's token
// can't create global objects, falls back to a file-backed mapping in C:\ProgramData\Ecoar.
class SharedRing {
public:
    ~SharedRing() { Close(); }
    bool OpenOrCreate(uint32_t sampleRate, uint32_t channels);
    EcoarRingHeader* Header() { return m_hdr; }
    const float* Data() { return m_hdr ? reinterpret_cast<const float*>(reinterpret_cast<BYTE*>(m_hdr) + kRingHeaderBytes) : nullptr; }
    const wchar_t* BackingName() { return m_backing; }
    void Close();

private:
    HANDLE m_file = INVALID_HANDLE_VALUE;
    HANDLE m_mapping = nullptr;
    EcoarRingHeader* m_hdr = nullptr;
    const wchar_t* m_backing = L"none";
};

constexpr wchar_t kRingGlobalName[] = L"Global\\EcoarRing-v1";
constexpr wchar_t kRingFilePath[] = L"C:\\ProgramData\\Ecoar\\ring.bin";
