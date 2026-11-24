#pragma once
#include "RingLayout.h"

// Per-APO-instance read cursor. Readers never consume: each instance keeps its own cursor.
struct ReaderState {
    int64_t cursor = 0;
    bool started = false;
    bool playing = false;
};

// Copies up to n mono samples from the ring into out; zero-fills the rest.
// Returns the number of real samples copied. Real-time safe (no alloc, no locks).
uint32_t ReadRing(const float* data, int64_t writeIndex, uint32_t sampleRate, ReaderState& s, float* out, uint32_t n);
