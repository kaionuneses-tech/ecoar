using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Xunit;

namespace Ecoar.Tests;

internal sealed class FakeSink : IRingSink
{
    public uint SampleRate { get; set; } = 48000;
    public List<float> Samples { get; } = new();
    public void Write(ReadOnlySpan<float> mono) => Samples.AddRange(mono.ToArray());
}

internal sealed class ConstantProvider : ISampleProvider
{
    private readonly float _value;
    private long _remaining;
    public ConstantProvider(float value, int rate, int channels, double seconds)
    {
        _value = value;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        _remaining = (long)(seconds * rate) * channels;
    }
    public WaveFormat WaveFormat { get; }
    public int Read(float[] buffer, int offset, int count)
    {
        int n = (int)Math.Min(count, _remaining);
        for (int i = 0; i < n; i++) buffer[offset + i] = _value;
        _remaining -= n;
        return n;
    }
}

public class AudioEngineTests
{
    private static int Drain(ISampleProvider p, int bufferSize = 4410)
    {
        var buf = new float[bufferSize];
        int total = 0, n;
        while ((n = p.Read(buf, 0, buf.Length)) > 0) total += n;
        return total;
    }

    [Fact]
    public void Resampler_44100_to_48000_keeps_duration()
    {
        var sink = new FakeSink { SampleRate = 48000 };
        var src = new SignalGenerator(44100, 2) { Frequency = 440, Gain = 0.3 }.Take(TimeSpan.FromSeconds(1));
        var tee = new MicTee(src) { Sink = sink };
        Drain(tee);
        Assert.InRange(sink.Samples.Count, 48000 * 0.99, 48000 * 1.01);
    }

    [Fact]
    public void MicTee_passes_audio_through_unchanged_count()
    {
        var src = new ConstantProvider(0.5f, 48000, 2, 0.1);
        var tee = new MicTee(src) { Sink = new FakeSink() };
        var buf = new float[960];
        Assert.Equal(960, tee.Read(buf, 0, 960));
    }

    [Fact]
    public void MicTee_applies_mic_volume()
    {
        var sink = new FakeSink();
        var tee = new MicTee(new ConstantProvider(0.5f, 48000, 2, 0.5)) { Sink = sink, MicVolume = 0.5f };
        Drain(tee, 960);
        // Skip resampler settling at the start and end.
        var middle = sink.Samples.Skip(sink.Samples.Count / 4).Take(sink.Samples.Count / 2);
        Assert.All(middle, v => Assert.InRange(v, 0.24f, 0.26f));
    }

    [Fact]
    public void MicTee_mutes_headphones_but_still_feeds_mic()
    {
        var sink = new FakeSink();
        var tee = new MicTee(new ConstantProvider(0.5f, 48000, 1, 0.2)) { Sink = sink, HeadphoneVolume = 0f };
        var buf = new float[4800];
        int n = tee.Read(buf, 0, buf.Length);
        Assert.Equal(4800, n);
        Assert.All(buf, v => Assert.Equal(0f, v));
        Drain(tee);
        Assert.Contains(sink.Samples, v => v != 0f);
    }

    [Fact, Trait("Category", "Desktop")]   // Media Foundation
    public void Play_bad_file_raises_error_and_writes_nothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ecoar-bad-{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var sink = new FakeSink();
        string? error = null;
        using (var engine = new AudioEngine(() => sink))
        {
            engine.Error += e => error = e;
            engine.Play(path);
        }
        File.Delete(path);
        Assert.NotNull(error);
        Assert.Empty(sink.Samples);
    }
}
