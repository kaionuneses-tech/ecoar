// Assert-based tests for the ring reader and mixer. Build with /MT; run: out\apo_tests.exe
#undef NDEBUG
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
#include <vector>
#include "../RingReader.h"
#include "../Mixer.h"

static const uint32_t kRate = 48000;   // target 1920, maxLag 4800

struct Ring {
    std::vector<float> data = std::vector<float>(kRingCapacity, 0.f);
    int64_t write = 0;
    void push(float v, int count) { for (int i = 0; i < count; ++i) data[(write++) & (kRingCapacity - 1)] = v; }
    void pushRamp(int count) { for (int i = 0; i < count; ++i) { data[write & (kRingCapacity - 1)] = (float)(write % 1000); ++write; } }
};

static void reader_first_call_starts_at_write_index() {
    Ring r; r.push(1.f, 10000);
    ReaderState s; float out[480];
    assert(ReadRing(r.data.data(), r.write, kRate, s, out, 480) == 0);
    assert(s.cursor == 10000);
    for (float v : out) assert(v == 0.f);
}

static void reader_waits_for_prebuffer() {
    Ring r; ReaderState s; float out[480];
    ReadRing(r.data.data(), r.write, kRate, s, out, 480);
    r.pushRamp(1000);
    assert(ReadRing(r.data.data(), r.write, kRate, s, out, 480) == 0);
    r.pushRamp(1000);   // lag 2000 >= 1920
    assert(ReadRing(r.data.data(), r.write, kRate, s, out, 480) == 480);
    for (int i = 0; i < 480; ++i) assert(out[i] == (float)(i % 1000));
}

static void reader_resyncs_when_too_far_behind() {
    Ring r; ReaderState s; float out[480];
    ReadRing(r.data.data(), r.write, kRate, s, out, 480);
    r.pushRamp(6000);   // lag 6000 > 4800
    assert(ReadRing(r.data.data(), r.write, kRate, s, out, 480) == 480);
    assert(s.cursor == r.write - 1920 + 480);
    assert(out[0] == (float)((r.write - 1920) % 1000));
}

static void reader_frozen_writer_outputs_silence_after_data() {
    Ring r; ReaderState s; float out[480];
    ReadRing(r.data.data(), r.write, kRate, s, out, 480);
    r.push(0.5f, 1920);
    for (int call = 1; call <= 4; ++call) {
        assert(ReadRing(r.data.data(), r.write, kRate, s, out, 480) == 480);
        for (float v : out) assert(v == 0.5f);
    }
    assert(ReadRing(r.data.data(), r.write, kRate, s, out, 480) == 0);
    for (float v : out) assert(v == 0.f);
}

static void two_readers_get_same_audio() {
    Ring r; ReaderState a, b; float oa[480], ob[480];
    ReadRing(r.data.data(), r.write, kRate, a, oa, 480);
    ReadRing(r.data.data(), r.write, kRate, b, ob, 480);
    r.pushRamp(3000);
    for (int call = 0; call < 4; ++call) {
        uint32_t na = ReadRing(r.data.data(), r.write, kRate, a, oa, 480);
        uint32_t nb = ReadRing(r.data.data(), r.write, kRate, b, ob, 480);
        assert(na == 480 && nb == 480);
        assert(memcmp(oa, ob, sizeof(oa)) == 0);
    }
}

static void reader_wraps_index() {
    Ring r; r.write = (int64_t)kRingCapacity * 3 - 1000;
    ReaderState s; float out[480];
    ReadRing(r.data.data(), r.write, kRate, s, out, 480);
    r.pushRamp(2400);   // crosses a multiple of the capacity
    assert(ReadRing(r.data.data(), r.write, kRate, s, out, 480) == 480);
    int64_t first = r.write - 2400;
    for (int i = 0; i < 480; ++i) assert(out[i] == (float)((first + i) % 1000));
    for (int k = 0; k < 3; ++k) {
        int64_t base = s.cursor;
        assert(ReadRing(r.data.data(), r.write, kRate, s, out, 480) == 480);
        for (int i = 0; i < 480; ++i) assert(out[i] == (float)((base + i) % 1000));
    }
}

static void mix_mono_into_stereo() {
    float buf[4] = {0.1f, 0.2f, 0.3f, 0.4f};
    float mono[2] = {0.1f, 0.1f};
    MixMono(buf, 2, 2, mono);
    const float want[4] = {0.2f, 0.3f, 0.4f, 0.5f};
    for (int i = 0; i < 4; ++i) assert(fabsf(buf[i] - want[i]) < 1e-6f);
}

static void mixer_limits_peaks() {
    assert(SoftClip(0.5f) == 0.5f);
    assert(SoftClip(5.f) <= 1.f && SoftClip(5.f) > 0.8f);
    assert(SoftClip(-5.f) >= -1.f && SoftClip(-5.f) < -0.8f);
    float prev = SoftClip(-3.f);
    for (float x = -2.99f; x <= 3.f; x += 0.01f) { float y = SoftClip(x); assert(y >= prev); prev = y; }
}

int main() {
    reader_first_call_starts_at_write_index();
    reader_waits_for_prebuffer();
    reader_resyncs_when_too_far_behind();
    reader_frozen_writer_outputs_silence_after_data();
    two_readers_get_same_audio();
    reader_wraps_index();
    mix_mono_into_stereo();
    mixer_limits_peaks();
    printf("ALL PASS\n");
    return 0;
}
