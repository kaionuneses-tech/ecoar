// Ecoar capture APO (SFX): adds the soundboard audio from the shared ring to the microphone stream.
// Structure (aggregation, registration, format checks) proven by beep-apo-spike/.
#include <windows.h>
#include <unknwn.h>
#include <olectl.h>
#include <mmreg.h>
#include <ks.h>
#include <ksmedia.h>
#include <audiomediatype.h>
#include <audioenginebaseapo.h>
#include <new>
#include <string.h>
#include <wchar.h>
#include "Log.h"
#include "Mixer.h"
#include "RingReader.h"
#include "SharedRing.h"

// {5E0C2A91-3B7D-4C6E-9F18-A2D4B6C8E0F1}
static const CLSID CLSID_EcoarApo = {0x5e0c2a91, 0x3b7d, 0x4c6e, {0x9f, 0x18, 0xa2, 0xd4, 0xb6, 0xc8, 0xe0, 0xf1}};
static const wchar_t kFriendlyName[] = L"Ecoar Mic Mixer";
static const wchar_t kCopyright[] = L"Ecoar (personal use)";

static HMODULE g_module = nullptr;
static volatile LONG g_objects = 0;
static volatile LONG g_locks = 0;

static bool GetFloat32Format(IAudioMediaType* t, UNCOMPRESSEDAUDIOFORMAT* out) {
    UNCOMPRESSEDAUDIOFORMAT f = {};
    if (!t || FAILED(t->GetUncompressedAudioFormat(&f))) return false;
    if (out) *out = f;
    return f.guidFormatType == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT && f.dwBytesPerSampleContainer == 4 &&
           f.dwSamplesPerFrame > 0 && f.fFramesPerSecond > 0;
}

// The audio engine creates APOs aggregated, so the object needs a non-delegating IUnknown
// (same vtable layout as IUnknown).
struct INonDelegatingUnknown {
    virtual HRESULT STDMETHODCALLTYPE NDQueryInterface(REFIID riid, void** ppv) = 0;
    virtual ULONG STDMETHODCALLTYPE NDAddRef() = 0;
    virtual ULONG STDMETHODCALLTYPE NDRelease() = 0;
};

class EcoarApo : public INonDelegatingUnknown,
                 public IAudioProcessingObject,
                 public IAudioProcessingObjectRT,
                 public IAudioProcessingObjectConfiguration,
                 public IAudioSystemEffects {
public:
    explicit EcoarApo(IUnknown* outer)
        : m_outer(outer ? outer : reinterpret_cast<IUnknown*>(static_cast<INonDelegatingUnknown*>(this))) {
        InterlockedIncrement(&g_objects);
    }
    ~EcoarApo() {
        delete[] m_scratch;
        InterlockedDecrement(&g_objects);
    }

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override { return m_outer->QueryInterface(riid, ppv); }
    STDMETHODIMP_(ULONG) AddRef() override { return m_outer->AddRef(); }
    STDMETHODIMP_(ULONG) Release() override { return m_outer->Release(); }

    HRESULT STDMETHODCALLTYPE NDQueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == __uuidof(IUnknown)) {
            *ppv = static_cast<INonDelegatingUnknown*>(this);
            NDAddRef();
            return S_OK;
        }
        if (riid == __uuidof(IAudioProcessingObject)) *ppv = static_cast<IAudioProcessingObject*>(this);
        else if (riid == __uuidof(IAudioProcessingObjectRT)) *ppv = static_cast<IAudioProcessingObjectRT*>(this);
        else if (riid == __uuidof(IAudioProcessingObjectConfiguration)) *ppv = static_cast<IAudioProcessingObjectConfiguration*>(this);
        else if (riid == __uuidof(IAudioSystemEffects)) *ppv = static_cast<IAudioSystemEffects*>(this);
        else { *ppv = nullptr; return E_NOINTERFACE; }
        reinterpret_cast<IUnknown*>(*ppv)->AddRef();
        return S_OK;
    }
    ULONG STDMETHODCALLTYPE NDAddRef() override { return InterlockedIncrement(&m_ref); }
    ULONG STDMETHODCALLTYPE NDRelease() override {
        ULONG r = InterlockedDecrement(&m_ref);
        if (r == 0) delete this;
        return r;
    }

    // IAudioProcessingObject
    STDMETHODIMP Reset() override { m_reader = ReaderState(); return S_OK; }
    STDMETHODIMP GetLatency(HNSTIME* t) override { if (!t) return E_POINTER; *t = 0; return S_OK; }
    STDMETHODIMP GetRegistrationProperties(APO_REG_PROPERTIES** pp) override {
        if (!pp) return E_POINTER;
        auto* p = static_cast<APO_REG_PROPERTIES*>(CoTaskMemAlloc(sizeof(APO_REG_PROPERTIES)));
        if (!p) return E_OUTOFMEMORY;
        ZeroMemory(p, sizeof(*p));
        p->clsid = CLSID_EcoarApo;
        p->Flags = static_cast<APO_FLAG>(APO_FLAG_DEFAULT | APO_FLAG_INPLACE);
        wcscpy_s(p->szFriendlyName, kFriendlyName);
        wcscpy_s(p->szCopyrightInfo, kCopyright);
        p->u32MajorVersion = 1;
        p->u32MinInputConnections = p->u32MaxInputConnections = 1;
        p->u32MinOutputConnections = p->u32MaxOutputConnections = 1;
        p->u32MaxInstances = 0xFFFFFFFF;
        p->u32NumAPOInterfaces = 1;
        p->iidAPOInterfaceList[0] = __uuidof(IAudioProcessingObject);
        *pp = p;
        return S_OK;
    }
    STDMETHODIMP Initialize(UINT32, BYTE*) override { return S_OK; }
    STDMETHODIMP IsInputFormatSupported(IAudioMediaType* o, IAudioMediaType* r, IAudioMediaType** s) override { return CheckFormat(o, r, s); }
    STDMETHODIMP IsOutputFormatSupported(IAudioMediaType* o, IAudioMediaType* r, IAudioMediaType** s) override { return CheckFormat(o, r, s); }
    STDMETHODIMP GetInputChannelCount(UINT32* c) override { if (!c) return E_POINTER; *c = m_channels; return S_OK; }

    // IAudioProcessingObjectConfiguration (not real-time: may allocate and log)
    STDMETHODIMP LockForProcess(UINT32 nIn, APO_CONNECTION_DESCRIPTOR** in, UINT32 nOut, APO_CONNECTION_DESCRIPTOR**) override {
        if (nIn != 1 || nOut != 1 || !in || !in[0]) return E_INVALIDARG;
        UNCOMPRESSEDAUDIOFORMAT f;
        if (!GetFloat32Format(in[0]->pFormat, &f)) { LogLine("LockForProcess: format not float32, passthrough off"); return APOERR_FORMAT_NOT_SUPPORTED; }
        m_channels = f.dwSamplesPerFrame;
        m_rate = static_cast<UINT32>(f.fFramesPerSecond);
        delete[] m_scratch;
        m_scratchFrames = in[0]->u32MaxFrameCount;
        m_scratch = new (std::nothrow) float[m_scratchFrames];
        if (!m_scratch) return E_OUTOFMEMORY;
        m_reader = ReaderState();
        bool ok = m_ring.OpenOrCreate(m_rate, m_channels);
        LogLine("LockForProcess rate=%u ch=%u maxFrames=%u ring=%d backing=%ls", m_rate, m_channels, m_scratchFrames, ok, m_ring.BackingName());
        return S_OK;   // without a ring we still pass the mic through untouched
    }
    STDMETHODIMP UnlockForProcess() override { m_ring.Close(); return S_OK; }

    // IAudioProcessingObjectRT: no allocation, no locks, no logging.
    STDMETHODIMP_(void) APOProcess(UINT32 nIn, APO_CONNECTION_PROPERTY** in, UINT32 nOut, APO_CONNECTION_PROPERTY** out) override {
        if (nIn < 1 || nOut < 1) return;
        APO_CONNECTION_PROPERTY* i = in[0];
        APO_CONNECTION_PROPERTY* o = out[0];
        const UINT32 frames = i->u32ValidFrameCount;
        float* src = reinterpret_cast<float*>(i->pBuffer);
        float* dst = reinterpret_cast<float*>(o->pBuffer);
        const size_t samples = static_cast<size_t>(frames) * m_channels;
        if (i->u32BufferFlags == BUFFER_SILENT) memset(dst, 0, samples * sizeof(float));
        else if (dst != src) memcpy(dst, src, samples * sizeof(float));

        EcoarRingHeader* hdr = m_ring.Header();
        if (hdr && m_scratch && frames <= m_scratchFrames) {
            InterlockedIncrement64(&hdr->heartbeat);
            const int64_t write = hdr->writeIndex;   // volatile read = acquire on MSVC x64 (/volatile:ms)
            if (ReadRing(m_ring.Data(), write, m_rate, m_reader, m_scratch, frames) > 0)
                MixMono(dst, frames, m_channels, m_scratch);
        }
        o->u32ValidFrameCount = frames;
        o->u32BufferFlags = BUFFER_VALID;
    }
    STDMETHODIMP_(UINT32) CalcInputFrames(UINT32 n) override { return n; }
    STDMETHODIMP_(UINT32) CalcOutputFrames(UINT32 n) override { return n; }

private:
    HRESULT CheckFormat(IAudioMediaType* opposite, IAudioMediaType* req, IAudioMediaType** sup) {
        if (!req || !sup) return E_POINTER;
        *sup = nullptr;
        UNCOMPRESSEDAUDIOFORMAT r, o;
        if (!GetFloat32Format(req, &r)) return APOERR_FORMAT_NOT_SUPPORTED;
        if (opposite && GetFloat32Format(opposite, &o) &&
            (o.dwSamplesPerFrame != r.dwSamplesPerFrame || o.fFramesPerSecond != r.fFramesPerSecond))
            return APOERR_FORMAT_NOT_SUPPORTED;   // in-place: input and output identical
        req->AddRef();
        *sup = req;
        return S_OK;
    }

    IUnknown* m_outer;   // not AddRef'd: the outer object owns us
    volatile LONG m_ref = 1;
    UINT32 m_channels = 1, m_rate = 48000;
    float* m_scratch = nullptr;
    UINT32 m_scratchFrames = 0;
    ReaderState m_reader;
    SharedRing m_ring;
};

class Factory : public IClassFactory {
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IClassFactory)) { *ppv = this; return S_OK; }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return 2; }   // static instance
    STDMETHODIMP_(ULONG) Release() override { return 1; }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (outer && riid != __uuidof(IUnknown)) return CLASS_E_NOAGGREGATION;   // COM aggregation rule
        EcoarApo* apo = new (std::nothrow) EcoarApo(outer);
        if (!apo) return E_OUTOFMEMORY;
        HRESULT hr = apo->NDQueryInterface(riid, ppv);
        apo->NDRelease();
        return hr;
    }
    STDMETHODIMP LockServer(BOOL lock) override {
        if (lock) InterlockedIncrement(&g_locks); else InterlockedDecrement(&g_locks);
        return S_OK;
    }
};
static Factory g_factory;

static LONG SetStr(HKEY k, const wchar_t* name, const wchar_t* v) {
    return RegSetValueExW(k, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(v), static_cast<DWORD>((wcslen(v) + 1) * sizeof(wchar_t)));
}
static LONG SetDw(HKEY k, const wchar_t* name, DWORD v) {
    return RegSetValueExW(k, name, 0, REG_DWORD, reinterpret_cast<const BYTE*>(&v), sizeof(v));
}

STDAPI DllRegisterServer() {
    wchar_t clsid[64], iid[64], path[MAX_PATH], key[256];
    StringFromGUID2(CLSID_EcoarApo, clsid, 64);
    StringFromGUID2(__uuidof(IAudioProcessingObject), iid, 64);
    if (!GetModuleFileNameW(g_module, path, MAX_PATH)) return HRESULT_FROM_WIN32(GetLastError());

    HKEY k;
    swprintf_s(key, L"SOFTWARE\\Classes\\CLSID\\%s", clsid);
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, key, 0, nullptr, 0, KEY_WRITE, nullptr, &k, nullptr)) return SELFREG_E_CLASS;
    SetStr(k, nullptr, kFriendlyName);
    RegCloseKey(k);
    swprintf_s(key, L"SOFTWARE\\Classes\\CLSID\\%s\\InprocServer32", clsid);
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, key, 0, nullptr, 0, KEY_WRITE, nullptr, &k, nullptr)) return SELFREG_E_CLASS;
    SetStr(k, nullptr, path);
    SetStr(k, L"ThreadingModel", L"Both");
    RegCloseKey(k);

    swprintf_s(key, L"SOFTWARE\\Classes\\AudioEngine\\AudioProcessingObjects\\%s", clsid);
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, key, 0, nullptr, 0, KEY_WRITE, nullptr, &k, nullptr)) return SELFREG_E_CLASS;
    SetStr(k, L"FriendlyName", kFriendlyName);
    SetStr(k, L"Copyright", kCopyright);
    SetDw(k, L"MajorVersion", 1);
    SetDw(k, L"MinorVersion", 0);
    SetDw(k, L"Flags", APO_FLAG_DEFAULT | APO_FLAG_INPLACE);
    SetDw(k, L"MinInputConnections", 1);
    SetDw(k, L"MaxInputConnections", 1);
    SetDw(k, L"MinOutputConnections", 1);
    SetDw(k, L"MaxOutputConnections", 1);
    SetDw(k, L"MaxInstances", 0xFFFFFFFF);
    SetDw(k, L"NumAPOInterfaces", 1);
    SetStr(k, L"APOInterface0", iid);
    RegCloseKey(k);
    return S_OK;
}

STDAPI DllUnregisterServer() {
    wchar_t clsid[64], key[256];
    StringFromGUID2(CLSID_EcoarApo, clsid, 64);
    swprintf_s(key, L"SOFTWARE\\Classes\\AudioEngine\\AudioProcessingObjects\\%s", clsid);
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, key);
    swprintf_s(key, L"SOFTWARE\\Classes\\CLSID\\%s", clsid);
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, key);
    return S_OK;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv) {
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (rclsid != CLSID_EcoarApo) return CLASS_E_CLASSNOTAVAILABLE;
    return g_factory.QueryInterface(riid, ppv);
}

STDAPI DllCanUnloadNow() { return (g_objects == 0 && g_locks == 0) ? S_OK : S_FALSE; }

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { g_module = h; DisableThreadLibraryCalls(h); }
    return TRUE;
}
