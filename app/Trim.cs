using NAudio.Vorbis;
using NAudio.Wave;

namespace Ecoar;

/// <summary>Opens any supported sound file as a seekable stream.</summary>
public static class AudioFile
{
    public static WaveStream Open(string path) =>
        Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(path)
            : new MediaFoundationReader(path);
}

/// <summary>Plays only [start, end) of a file without changing the file.</summary>
public static class AudioTrim
{
    public static ISampleProvider Apply(WaveStream reader, double? start, double? end)
    {
        double from = Math.Max(0, start ?? 0);
        if (from > 0) reader.CurrentTime = TimeSpan.FromSeconds(from);
        ISampleProvider sp = reader.ToSampleProvider();
        return end is double to && to > from ? sp.Take(TimeSpan.FromSeconds(to - from)) : sp;
    }
}

/// <summary>Pixel/second conversions for the trim editor's two handles.</summary>
public static class TrimMath
{
    public const double MinGapSeconds = 0.1;

    public static double XToSeconds(int x, int width, double duration) =>
        width <= 0 ? 0 : Math.Clamp((double)x / width * duration, 0, duration);

    public static int SecondsToX(double seconds, int width, double duration) =>
        duration <= 0 ? 0 : (int)Math.Round(seconds / duration * width);

    public static double ClampStart(double start, double end) => Math.Clamp(start, 0, Math.Max(0, end - MinGapSeconds));

    public static double ClampEnd(double end, double start, double duration) =>
        Math.Clamp(end, Math.Min(duration, start + MinGapSeconds), duration);
}

/// <summary>Peak amplitude per bucket, for drawing the waveform.</summary>
public static class Waveform
{
    public static float[] ComputePeaks(string path, int buckets)
    {
        using var reader = AudioFile.Open(path);
        var sp = reader.ToSampleProvider();
        int ch = sp.WaveFormat.Channels;
        long totalFrames = Math.Max(1, reader.Length / reader.WaveFormat.BlockAlign);
        var peaks = new float[buckets];
        var buf = new float[ch * 4096];
        long frame = 0;
        int n;
        while ((n = sp.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                int b = (int)Math.Min(buckets - 1, (frame + i / ch) * buckets / totalFrames);
                float v = Math.Abs(buf[i]);
                if (v > peaks[b]) peaks[b] = v;
            }
            frame += n / ch;
        }
        return peaks;
    }
}
