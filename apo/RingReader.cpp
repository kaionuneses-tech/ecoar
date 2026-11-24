#include "RingReader.h"
#include <string.h>

uint32_t ReadRing(const float* data, int64_t writeIndex, uint32_t sampleRate, ReaderState& s, float* out, uint32_t n) {
    const int64_t target = (int64_t)sampleRate * 40 / 1000;
    const int64_t maxLag = (int64_t)sampleRate * 100 / 1000;
    if (!s.started) { s.cursor = writeIndex; s.started = true; }   // never replay old audio

    int64_t lag = writeIndex - s.cursor;
    if (lag > maxLag || lag > (int64_t)kRingCapacity) { s.cursor = writeIndex - target; lag = target; }
    if (lag < 0) { s.cursor = writeIndex; lag = 0; }   // writer restarted from a lower index

    if (!s.playing) {
        if (lag < target) { memset(out, 0, n * sizeof(float)); return 0; }
        s.playing = true;
    }
    const uint32_t avail = lag < (int64_t)n ? (uint32_t)lag : n;
    for (uint32_t i = 0; i < avail; ++i) out[i] = data[(s.cursor + i) & (kRingCapacity - 1)];
    if (avail < n) memset(out + avail, 0, (n - avail) * sizeof(float));
    s.cursor += avail;
    if (avail < n) s.playing = false;   // ran dry: prebuffer again before resuming
    return avail;
}
