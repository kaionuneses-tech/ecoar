using Xunit;

namespace Ecoar.Tests;

public class SoundLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ecoar-lib-{Guid.NewGuid():N}");
    private string ConfigPath => Path.Combine(_dir, "config.json");

    public SoundLibraryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Roundtrip_preserves_entries_and_volumes()
    {
        var c = new AppConfig
        {
            Sounds = { new SoundEntry("Risada", @"C:\sons\risada.mp3", "Ctrl+Alt+D1") },
            MicVolume = 0.7f, HeadphoneVolume = 0.3f, HearMyself = false, StopHotkey = "Ctrl+Alt+D0",
        };
        SoundLibrary.Save(c, ConfigPath);
        var back = SoundLibrary.Load(ConfigPath);
        Assert.Equal(c.Sounds, back.Sounds);
        Assert.Equal(0.7f, back.MicVolume);
        Assert.Equal(0.3f, back.HeadphoneVolume);
        Assert.False(back.HearMyself);
        Assert.Equal("Ctrl+Alt+D0", back.StopHotkey);
    }

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var c = SoundLibrary.Load(ConfigPath);
        Assert.Empty(c.Sounds);
        Assert.Equal(1f, c.MicVolume);
        Assert.Equal(0.8f, c.HeadphoneVolume);
        Assert.True(c.HearMyself);
    }

    [Fact]
    public void Corrupt_json_returns_defaults_and_keeps_backup()
    {
        File.WriteAllText(ConfigPath, "{ isto não é json");
        var c = SoundLibrary.Load(ConfigPath);
        Assert.Empty(c.Sounds);
        Assert.False(File.Exists(ConfigPath));
        var backup = Assert.Single(Directory.GetFiles(_dir, "config.json.bad-*"));
        Assert.Equal("{ isto não é json", File.ReadAllText(backup));
    }

    [Fact]
    public void FileMissing_true_for_deleted_sound()
    {
        var existing = Path.Combine(_dir, "som.wav");
        File.WriteAllBytes(existing, new byte[] { 0 });
        Assert.False(SoundLibrary.FileMissing(new SoundEntry("a", existing, null)));
        Assert.True(SoundLibrary.FileMissing(new SoundEntry("b", Path.Combine(_dir, "sumiu.wav"), null)));
    }
}
