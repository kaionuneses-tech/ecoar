using NAudio.Dsp;
using NAudio.Wave;

namespace Ecoar;

/// <summary>Destination for the microphone copy of the audio (the shared ring in production).</summary>
public interface IRingSink
{
    uint SampleRate { get; }
    void Write(ReadOnlySpan<float> mono);
}

/// <summary>
/// Pass-through sample provider for the headphones that also sends a mono, resampled copy
/// to the mic sink. Both copies come from the same pull, so they stay in step.
/// </summary>
public sealed class MicTee : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly WdlResampler _resampler = new();
    private uint _sinkRate;
    private float[] _out = Array.Empty<float>();

    public MicTee(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _resampler.SetMode(true, 2, false);
        _resampler.SetFilterParms();
        _resampler.SetFeedMode(true);   // we push input, it returns what it can
    }

    public WaveFormat WaveFormat => _source.WaveFormat;
    public float MicVolume { get; set; } = 1f;
    public float HeadphoneVolume { get; set; } = 1f;
    public IRingSink? Sink { get; set; }

    public int Read(float[] buffer, int offset, int count)
    {
        int n = _source.Read(buffer, offset, count);
        if (n <= 0) return n;
        var sink = Sink;
        if (sink is not null) SendToMic(sink, buffer, offset, n);
        float hp = HeadphoneVolume;
        if (hp != 1f)
            for (int i = 0; i < n; i++) buffer[offset + i] *= hp;
        return n;
    }

    private void SendToMic(IRingSink sink, float[] buffer, int offset, int n)
    {
        uint rate = sink.SampleRate;
        if (rate == 0) return;
        if (rate != _sinkRate)
        {
            _resampler.SetRates(WaveFormat.SampleRate, rate);
            _sinkRate = rate;
        }
        int frames = n / _channels;
        _resampler.ResamplePrepare(frames, 1, out float[] inBuf, out int inOff);
        float gain = MicVolume / _channels;
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < _channels; c++) sum += buffer[offset + f * _channels + c];
            inBuf[inOff + f] = sum * gain;
        }
        int maxOut = (int)((long)frames * rate / WaveFormat.SampleRate) + 64;
        if (_out.Length < maxOut) _out = new float[maxOut];
        int produced = _resampler.ResampleOut(_out, 0, frames, maxOut, 1);
        if (produced > 0) sink.Write(_out.AsSpan(0, produced));
    }
}
