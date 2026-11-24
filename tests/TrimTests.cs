using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Xunit;

namespace Ecoar.Tests;

public class TrimTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ecoar-trim-{Guid.NewGuid():N}");

    public TrimTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>2 s mono WAV: first second silent, second second a 0.5-amplitude tone.</summary>
    private string MakeWav()
    {
        string path = Path.Combine(_dir, "t.wav");
        var silence = new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)).ToSampleProvider().Take(TimeSpan.FromSeconds(1));
        var tone = new SignalGenerator(48000, 1) { Frequency = 440, Gain = 0.5 }.Take(TimeSpan.FromSeconds(1));
        WaveFileWriter.CreateWaveFile16(path, new ConcatenatingSampleProvider(new[] { silence, tone }));
        return path;
    }

    private static int CountFrames(ISampleProvider p)
    {
        var buf = new float[4800];
        int total = 0, n;
        while ((n = p.Read(buf, 0, buf.Length)) > 0) total += n;
        return total / p.WaveFormat.Channels;
    }

    [Fact]
    public void Handle_x_maps_to_seconds_and_clamps()
    {
        Assert.Equal(1.0, TrimMath.XToSeconds(100, 400, 4.0), 6);
        Assert.Equal(0.0, TrimMath.XToSeconds(-20, 400, 4.0), 6);
        Assert.Equal(4.0, TrimMath.XToSeconds(999, 400, 4.0), 6);
        Assert.Equal(100, TrimMath.SecondsToX(1.0, 400, 4.0));
    }

    [Fact]
    public void Handles_keep_a_minimum_gap()
    {
        // Dragging start past end stops 0.1 s before it; dragging end before start stops 0.1 s after it.
        Assert.Equal(1.9, TrimMath.ClampStart(3.0, end: 2.0), 6);
        Assert.Equal(1.1, TrimMath.ClampEnd(0.5, start: 1.0, duration: 4.0), 6);
        Assert.Equal(4.0, TrimMath.ClampEnd(9.0, start: 1.0, duration: 4.0), 6);
    }

    [Fact, Trait("Category", "Desktop")]   // Media Foundation
    public void Peaks_show_where_the_sound_is()
    {
        float[] peaks = Waveform.ComputePeaks(MakeWav(), buckets: 10);
        Assert.Equal(10, peaks.Length);
        Assert.All(peaks.Take(4), p => Assert.True(p < 0.01f));            // silent first second
        Assert.All(peaks.Skip(6), p => Assert.InRange(p, 0.45f, 0.55f));   // 0.5 tone
    }

    [Fact]
    public void Trimmed_reader_plays_only_the_selection()
    {
        using var reader = new WaveFileReader(MakeWav());
        var trimmed = AudioTrim.Apply(reader, start: 0.5, end: 1.25);
        Assert.InRange(CountFrames(trimmed), 48000 * 0.75 - 2, 48000 * 0.75 + 2);
    }

    [Fact]
    public void No_trim_plays_the_whole_file()
    {
        using var reader = new WaveFileReader(MakeWav());
        Assert.InRange(CountFrames(AudioTrim.Apply(reader, null, null)), 96000 - 2, 96000 + 2);
    }

    [Fact, Trait("Category", "Desktop")]   // shows a window, Media Foundation
    public void Trim_dialog_opens_on_saved_selection_and_renders()
    {
        var sound = new SoundEntry("t", MakeWav(), null) { TrimStart = 0.5, TrimEnd = 1.25 };
        using var engine = new AudioEngine(() => new FakeSink());
        using var dlg = new TrimDialog(sound, engine) { Width = 640, Height = 260 };
        Assert.Equal(0.5, dlg.TrimStart);
        Assert.Equal(1.25, dlg.TrimEnd);
        dlg.StartPosition = FormStartPosition.Manual;
        dlg.Location = new Point(-4000, -4000);   // off screen
        dlg.Show();
        Application.DoEvents();
        using var bmp = new Bitmap(dlg.Width, dlg.Height);
        dlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        string png = Path.Combine(Path.GetTempPath(), "ecoar-trimdialog.png");
        bmp.Save(png);   // kept for a visual check
    }

    [Fact]
    public void Trim_is_saved_in_config_and_old_configs_still_load()
    {
        string cfg = Path.Combine(_dir, "config.json");
        var c = new AppConfig { Sounds = { new SoundEntry("a", "a.wav", null) { TrimStart = 0.5, TrimEnd = 1.25 } } };
        SoundLibrary.Save(c, cfg);
        var back = SoundLibrary.Load(cfg).Sounds[0];
        Assert.Equal(0.5, back.TrimStart);
        Assert.Equal(1.25, back.TrimEnd);

        File.WriteAllText(cfg, "{\"Sounds\":[{\"Name\":\"x\",\"Path\":\"x.wav\",\"Hotkey\":null}]}");
        var old = SoundLibrary.Load(cfg).Sounds[0];
        Assert.Null(old.TrimStart);
        Assert.Null(old.TrimEnd);
    }
}
