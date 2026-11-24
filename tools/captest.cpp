// Records 4 s from the default capture device (shared mode, like Discord) and counts 50 ms blocks
// containing a strong tone. Usage: captest [--freq <hz>] [--expect present|absent]
// PASS when the tone presence matches --expect (default: 880 Hz, present).
#include <windows.h>
#include <stdlib.h>
#include <string.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <mmreg.h>
#include <ks.h>
#include <ksmedia.h>
#include <math.h>
#include <stdio.h>
#include <vector>

#define CHECK(hr, what) if (FAILED(hr)) { printf("FAIL %s hr=0x%08lx\n", what, (unsigned long)(hr)); return 2; }

int main(int argc, char** argv) {
    double freq = 880.0;
    bool expectPresent = true;
    bool raw = false, comms = false;   // --raw / --comms open the stream like voice-chat apps do
    for (int a = 1; a < argc; ++a) {
        if (!strcmp(argv[a], "--raw")) raw = true;
        else if (!strcmp(argv[a], "--comms")) comms = true;
        else if (a + 1 < argc && !strcmp(argv[a], "--freq")) freq = atof(argv[++a]);
        else if (a + 1 < argc && !strcmp(argv[a], "--expect")) expectPresent = strcmp(argv[++a], "absent") != 0;
    }
    CHECK(CoInitializeEx(nullptr, COINIT_MULTITHREADED), "CoInitializeEx");
    IMMDeviceEnumerator* en = nullptr;
    CHECK(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&en)), "enumerator");
    IMMDevice* dev = nullptr;
    CHECK(en->GetDefaultAudioEndpoint(eCapture, eCommunications, &dev), "default capture");
    LPWSTR id = nullptr;
    dev->GetId(&id);
    printf("device: %ls\n", id);
    IAudioClient* ac = nullptr;
    CHECK(dev->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, (void**)&ac), "activate");
    if (raw || comms) {
        IAudioClient2* ac2 = nullptr;
        CHECK(ac->QueryInterface(IID_PPV_ARGS(&ac2)), "IAudioClient2");
        AudioClientProperties props = {};
        props.cbSize = sizeof(props);
        props.eCategory = comms ? AudioCategory_Communications : AudioCategory_Other;
        props.Options = raw ? AUDCLNT_STREAMOPTIONS_RAW : AUDCLNT_STREAMOPTIONS_NONE;
        CHECK(ac2->SetClientProperties(&props), "SetClientProperties");
        ac2->Release();
        printf("stream: raw=%d communications=%d\n", raw, comms);
    }
    WAVEFORMATEX* wf = nullptr;
    CHECK(ac->GetMixFormat(&wf), "mix format");
    bool isFloat = wf->wFormatTag == WAVE_FORMAT_IEEE_FLOAT ||
        (wf->wFormatTag == WAVE_FORMAT_EXTENSIBLE &&
         reinterpret_cast<WAVEFORMATEXTENSIBLE*>(wf)->SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
    printf("format: %u Hz, %u ch, %u bit, float=%d\n", (unsigned)wf->nSamplesPerSec, wf->nChannels, wf->wBitsPerSample, isFloat);
    if (!isFloat || wf->wBitsPerSample != 32) { printf("FAIL unsupported mix format\n"); return 2; }
    CHECK(ac->Initialize(AUDCLNT_SHAREMODE_SHARED, 0, 10000000, 0, wf, nullptr), "initialize");
    IAudioCaptureClient* cc = nullptr;
    CHECK(ac->GetService(IID_PPV_ARGS(&cc)), "capture client");

    const UINT32 rate = wf->nSamplesPerSec, ch = wf->nChannels;
    std::vector<float> mono;
    mono.reserve(rate * 5);
    CHECK(ac->Start(), "start");
    while (mono.size() < rate * 4) {
        Sleep(10);
        UINT32 pkt = 0;
        while (SUCCEEDED(cc->GetNextPacketSize(&pkt)) && pkt) {
            BYTE* data; UINT32 n; DWORD flags;
            if (FAILED(cc->GetBuffer(&data, &n, &flags, nullptr, nullptr))) break;
            const float* f = reinterpret_cast<const float*>(data);
            for (UINT32 i = 0; i < n; ++i) mono.push_back((flags & AUDCLNT_BUFFERFLAGS_SILENT) ? 0.f : f[i * ch]);
            cc->ReleaseBuffer(n);
        }
    }
    ac->Stop();

    // Goertzel per 50 ms block: estimated amplitude of the component at freq.
    const size_t N = rate / 20;
    const double w = 2 * 3.14159265358979 * freq / rate, coeff = 2 * cos(w);
    int strong = 0, blocks = 0; double maxAmp = 0;
    for (size_t b = 0; b + N <= mono.size(); b += N, ++blocks) {
        double s1 = 0, s2 = 0;
        for (size_t i = 0; i < N; ++i) { double s = mono[b + i] + coeff * s1 - s2; s2 = s1; s1 = s; }
        double amp = sqrt(s1 * s1 + s2 * s2 - coeff * s1 * s2) / (N / 2.0);
        if (amp > maxAmp) maxAmp = amp;
        if (amp > 0.08) ++strong;
    }
    double sq = 0;
    for (float v : mono) sq += (double)v * v;
    printf("freq=%.0f blocks=%d strong=%d maxAmp=%.4f rms=%.6f\n", freq, blocks, strong, maxAmp, mono.empty() ? 0 : sqrt(sq / mono.size()));
    const bool present = strong >= 3;
    const bool pass = present == expectPresent;
    printf("%s: tone %s (expected %s)\n", pass ? "PASS" : "FAIL", present ? "present" : "absent", expectPresent ? "present" : "absent");
    return pass ? 0 : 1;
}
