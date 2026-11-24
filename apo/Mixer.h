#pragma once
#include <stdint.h>

// Identity up to |x| = 0.8, then a tanh knee that never exceeds 1.0.
float SoftClip(float x);

// interleaved[f*channels + c] = SoftClip(interleaved[f*channels + c] + mono[f]).
void MixMono(float* interleaved, uint32_t frames, uint32_t channels, const float* mono);
