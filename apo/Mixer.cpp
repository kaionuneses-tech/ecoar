#include "Mixer.h"
#include <math.h>

float SoftClip(float x) {
    const float a = fabsf(x);
    if (a <= 0.8f) return x;
    const float y = 0.8f + 0.2f * tanhf((a - 0.8f) / 0.2f);
    return x < 0 ? -y : y;
}

void MixMono(float* interleaved, uint32_t frames, uint32_t channels, const float* mono) {
    for (uint32_t f = 0; f < frames; ++f)
        for (uint32_t c = 0; c < channels; ++c) {
            float& v = interleaved[f * channels + c];
            v = SoftClip(v + mono[f]);
        }
}
