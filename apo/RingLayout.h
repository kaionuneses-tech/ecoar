// Shared-memory ring between Ecoar.exe (single writer) and EcoarApo.dll instances (N readers).
// Layout is mirrored in app/RingWriter.cs — keep both in sync.
#pragma once
#include <stdint.h>
#include <stddef.h>

constexpr uint32_t kRingMagic = 0x524F4345;   // "ECOR"
constexpr uint32_t kRingVersion = 1;
constexpr uint32_t kRingCapacity = 65536;     // mono samples, power of two
constexpr size_t kRingHeaderBytes = 64;
constexpr size_t kRingBytes = kRingHeaderBytes + kRingCapacity * sizeof(float);

struct EcoarRingHeader {
    uint32_t magic;               // 0  APO
    uint32_t version;             // 4  APO
    uint32_t sampleRate;          // 8  APO (mic rate)
    uint32_t channels;            // 12 APO (informational)
    uint32_t capacity;            // 16 APO
    uint32_t reserved;            // 20
    volatile int64_t heartbeat;   // 24 APO, +1 per APOProcess
    volatile int64_t writeIndex;  // 32 app, monotonic sample count
    uint8_t padding[24];          // 40..63
};
static_assert(sizeof(EcoarRingHeader) == kRingHeaderBytes, "ring header must be 64 bytes");
static_assert(offsetof(EcoarRingHeader, heartbeat) == 24, "layout");
static_assert(offsetof(EcoarRingHeader, writeIndex) == 32, "layout");
