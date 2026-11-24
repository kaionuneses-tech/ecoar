using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ecoar;

/// <summary>"Ctrl+Alt+F1" &lt;-&gt; RegisterHotKey modifiers + key.</summary>
public static class HotkeyParser
{
    public const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModWin = 8;

    private static readonly (string Name, uint Mod)[] Modifiers =
        { ("Ctrl", ModControl), ("Alt", ModAlt), ("Shift", ModShift), ("Win", ModWin) };

    public static bool TryParse(string text, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var mod = Array.Find(Modifiers, m => m.Name.Equals(raw, StringComparison.OrdinalIgnoreCase));
            if (mod.Name is not null) { modifiers |= mod.Mod; continue; }
            if (key != Keys.None) return false;   // only one non-modifier key
            if (char.IsDigit(raw[0]) || !Enum.TryParse(raw, true, out Keys k)) return false;   // reject "123"
            if ((k & Keys.Modifiers) != 0 || k == Keys.None) return false;
            key = k;
        }
        return key != Keys.None;
    }

    /// <summary>Hotkey text for a key press in the capture dialog; null while only a modifier is held.
    /// A single key without modifiers is allowed (the user asked for one-key hotkeys).</summary>
    public static string? FromKeyPress(Keys keyCode, bool ctrl, bool alt, bool shift)
    {
        if (keyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
            or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu)
            return null;
        uint mods = (ctrl ? ModControl : 0) | (alt ? ModAlt : 0) | (shift ? ModShift : 0);
        return Format(mods, keyCode);
    }

    public static string Format(uint modifiers, Keys key)
    {
        var parts = Modifiers.Where(m => (modifiers & m.Mod) != 0).Select(m => m.Name).ToList();
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}

/// <summary>Registers the stop hotkey (id 0) and each sound's hotkey (id index+1).</summary>
public static class HotkeyBinder
{
    /// <summary>Returns the hotkeys another program holds. Never edits the config: losing a user's hotkey is worse than a warning.</summary>
    public static List<string> RegisterAll(AppConfig config, Func<int, string, bool> tryRegister)
    {
        var failed = new List<string>();
        if (config.StopHotkey is { } stop && !tryRegister(0, stop)) failed.Add(stop);
        for (int i = 0; i < config.Sounds.Count; i++)
            if (config.Sounds[i].Hotkey is { } hk && !tryRegister(i + 1, hk)) failed.Add(hk);
        return failed;
    }
}

/// <summary>Global hotkeys via RegisterHotKey; raises Pressed(id) on WM_HOTKEY.</summary>
public sealed class Hotkeys : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private readonly HashSet<int> _ids = new();

    public Hotkeys() => CreateHandle(new CreateParams());

    public event Action<int>? Pressed;

    public bool Register(int id, uint modifiers, Keys key)
    {
        if (_ids.Contains(id)) { UnregisterHotKey(Handle, id); _ids.Remove(id); }
        if (!RegisterHotKey(Handle, id, modifiers | ModNoRepeat, (uint)key)) return false;
        _ids.Add(id);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _ids) UnregisterHotKey(Handle, id);
        _ids.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey) Pressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
