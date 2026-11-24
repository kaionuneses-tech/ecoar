using System.Windows.Forms;
using Xunit;

namespace Ecoar.Tests;

public class HotkeysTests
{
    [Fact]
    public void Parse_ctrl_alt_f1()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+F1", out uint mods, out Keys key));
        Assert.Equal(3u, mods);   // MOD_ALT | MOD_CONTROL
        Assert.Equal(Keys.F1, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Banana")]
    [InlineData("F1+F2")]
    public void Parse_rejects_garbage(string text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _, out _));
    }

    [Fact]
    public void Format_roundtrips()
    {
        string s = HotkeyParser.Format(1 | 2 | 4, Keys.D7);
        Assert.Equal("Ctrl+Alt+Shift+D7", s);
        Assert.True(HotkeyParser.TryParse(s, out uint mods, out Keys key));
        Assert.Equal(7u, mods);
        Assert.Equal(Keys.D7, key);
    }

    [Theory]
    [InlineData(Keys.F5, "F5")]
    [InlineData(Keys.NumPad1, "NumPad1")]
    [InlineData(Keys.A, "A")]
    public void Single_key_without_modifiers_is_allowed(Keys key, string expected)
    {
        Assert.Equal(expected, HotkeyParser.FromKeyPress(key, ctrl: false, alt: false, shift: false));
        Assert.True(HotkeyParser.TryParse(expected, out uint mods, out Keys back));
        Assert.Equal(0u, mods);
        Assert.Equal(key, back);
    }

    [Fact]
    public void Key_press_with_modifiers_formats_combo()
    {
        Assert.Equal("Ctrl+Alt+S", HotkeyParser.FromKeyPress(Keys.S, ctrl: true, alt: true, shift: false));
    }

    [Theory]
    [InlineData(Keys.ControlKey)]
    [InlineData(Keys.ShiftKey)]
    [InlineData(Keys.Menu)]
    [InlineData(Keys.LWin)]
    public void Modifier_key_alone_is_not_a_hotkey(Keys key)
    {
        Assert.Null(HotkeyParser.FromKeyPress(key, ctrl: key == Keys.ControlKey, alt: key == Keys.Menu, shift: key == Keys.ShiftKey));
    }

    [Fact]
    public void Busy_hotkeys_are_reported_but_never_removed_from_config()
    {
        var c = new AppConfig
        {
            StopHotkey = "Ctrl+Alt+D0",
            Sounds = { new SoundEntry("a", "a.mp3", "Ctrl+Alt+C"), new SoundEntry("b", "b.mp3", null), new SoundEntry("c", "c.mp3", "F9") },
        };
        var calls = new List<(int Id, string Hotkey)>();
        var failed = HotkeyBinder.RegisterAll(c, (id, hk) => { calls.Add((id, hk)); return hk != "Ctrl+Alt+C"; });

        Assert.Equal(new[] { "Ctrl+Alt+C" }, failed);
        Assert.Equal(new[] { (0, "Ctrl+Alt+D0"), (1, "Ctrl+Alt+C"), (3, "F9") }, calls);
        Assert.Equal("Ctrl+Alt+C", c.Sounds[0].Hotkey);   // kept: the other program may release it later
        Assert.Equal("Ctrl+Alt+D0", c.StopHotkey);
    }

    [Fact, Trait("Category", "Desktop")]   // needs an interactive desktop
    public void Register_conflict_returns_false()
    {
        using var a = new Hotkeys();
        using var b = new Hotkeys();
        const uint mods = 1 | 2 | 4 | 8;   // Ctrl+Alt+Shift+Win: unlikely to be taken
        Assert.True(a.Register(1, mods, Keys.F12));
        Assert.False(b.Register(1, mods, Keys.F12));
    }
}
