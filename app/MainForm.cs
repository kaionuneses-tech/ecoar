using System.Diagnostics;

namespace Ecoar;

public sealed class MainForm : Form
{
    private const int StopHotkeyId = 0;
    private static readonly string[] AudioExtensions = { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".ogg" };

    private readonly AppConfig _config = SoundLibrary.Load(SoundLibrary.DefaultPath);
    private readonly AudioEngine _engine = new();
    private readonly Hotkeys _hotkeys = new();
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill, AllowDrop = true, HideSelection = false };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(0, 6, 8, 0) };
    private readonly Button _install = new() { Text = "Instalar no microfone", AutoSize = true };
    private readonly ComboBox _mics = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Button _stopHotkey = new() { AutoSize = true };
    private static readonly Icon AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
    private readonly NotifyIcon _tray = new() { Text = "Ecoar", Icon = AppIcon, Visible = true };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };
    private bool _exiting;

    public MainForm()
    {
        Text = "Ecoar";
        Icon = AppIcon;
        Width = 640;
        Height = 480;
        Font = new Font("Segoe UI", 9.5f);

        _list.Columns.Add("Som", 380);
        _list.Columns.Add("Atalho", 200);
        _list.DoubleClick += (_, _) => PlaySelected();
        _list.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _list.DragDrop += (_, e) => AddFiles((string[]?)e.Data?.GetData(DataFormats.FileDrop) ?? Array.Empty<string>());

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        buttons.Controls.AddRange(new Control[]
        {
            MakeButton("Tocar", PlaySelected), MakeButton("Parar", () => _engine.Stop()),
            MakeButton("Adicionar...", AddWithDialog), MakeButton("Remover", RemoveSelected),
            MakeButton("Renomear", RenameSelected), MakeButton("Definir atalho", SetSelectedHotkey),
            MakeButton("Recortar...", TrimSelected),
        });

        var mic = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 160, Value = Pct(_config.MicVolume) };
        var hp = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 160, Value = Pct(_config.HeadphoneVolume) };
        var hear = new CheckBox { Text = "Ouvir eu mesmo", AutoSize = true, Checked = _config.HearMyself, Padding = new Padding(0, 6, 0, 0) };
        mic.ValueChanged += (_, _) => { _config.MicVolume = _engine.MicVolume = mic.Value / 100f; Save(); };
        hp.ValueChanged += (_, _) => { _config.HeadphoneVolume = _engine.HeadphoneVolume = hp.Value / 100f; Save(); };
        hear.CheckedChanged += (_, _) => { _config.HearMyself = _engine.HearMyself = hear.Checked; Save(); };
        var volumes = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
        volumes.Controls.AddRange(new Control[]
        {
            new Label { Text = "Volume no microfone", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, mic,
            new Label { Text = "Volume para mim", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, hp, hear,
        });

        _stopHotkey.Click += (_, _) => SetStopHotkey();
        _install.Click += (_, _) => InstallOnMic();
        _mics.SelectedIndexChanged += (_, _) =>
        {
            if (_mics.SelectedItem is MicItem m) { _config.MicEndpointId = m.Id; Save(); }
        };
        var statusBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
        statusBar.Controls.AddRange(new Control[] { _status, new Label { Text = "Microfone:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _mics, _install, _stopHotkey });

        Controls.Add(_list);
        Controls.Add(buttons);
        Controls.Add(volumes);
        Controls.Add(statusBar);

        _engine.MicVolume = _config.MicVolume;
        _engine.HeadphoneVolume = _config.HeadphoneVolume;
        _engine.HearMyself = _config.HearMyself;
        _engine.Error += msg => BeginInvoke(() => MessageBox.Show(this, msg, "Ecoar", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        _hotkeys.Pressed += OnHotkey;

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Abrir", null, (_, _) => ShowFromTray());
        trayMenu.Items.Add("Parar", null, (_, _) => _engine.Stop());
        trayMenu.Items.Add("Sair", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = trayMenu;
        _tray.DoubleClick += (_, _) => ShowFromTray();

        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        LoadMics();
        RegisterHotkeys();
        RefreshList();
        UpdateStatus();
    }

    private sealed record MicItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private static int Pct(float v) => (int)Math.Clamp(Math.Round(v * 100), 0, 100);

    private static Button MakeButton(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void Save() => SoundLibrary.Save(_config, SoundLibrary.DefaultPath);

    private void LoadMics()
    {
        _mics.Items.Clear();
        foreach (var (id, name) in Setup.ActiveCaptureEndpoints()) _mics.Items.Add(new MicItem(id, name));
        var selected = _mics.Items.Cast<MicItem>().FirstOrDefault(m => m.Id == _config.MicEndpointId);
        if (selected is not null) _mics.SelectedItem = selected;
        else if (_mics.Items.Count > 0) _mics.SelectedIndex = 0;
    }

    private void UpdateStatus()
    {
        bool active = _engine.EffectActive;
        bool installed = _mics.SelectedItem is MicItem m && Setup.IsInstalledOn(m.Id);
        _status.Text = active ? "● Efeito ativo" : installed ? "○ Efeito instalado (aguardando um programa usar o microfone)" : "○ Efeito inativo";
        _status.ForeColor = active ? Color.SeaGreen : installed ? Color.DarkGoldenrod : Color.Firebrick;
        _install.Visible = !installed;
        _stopHotkey.Text = "Atalho de parar: " + (_config.StopHotkey ?? "nenhum");
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var s in _config.Sounds)
        {
            string name = s.TrimStart is null && s.TrimEnd is null ? s.Name : s.Name + "  ✂";
            var item = new ListViewItem(new[] { name, s.Hotkey ?? "" }) { Tag = s };
            if (SoundLibrary.FileMissing(s))
            {
                item.Font = new Font(_list.Font, FontStyle.Strikeout);
                item.ForeColor = Color.Gray;
                item.ToolTipText = "Arquivo não encontrado: " + s.Path;
            }
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    private int SelectedIndex => _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;

    private void PlaySelected()
    {
        if (SelectedIndex >= 0) Play(_config.Sounds[SelectedIndex]);
    }

    private void Play(SoundEntry s)
    {
        if (SoundLibrary.FileMissing(s)) { MessageBox.Show(this, "Arquivo não encontrado:\n" + s.Path, "Ecoar"); return; }
        _engine.Play(s.Path, s.TrimStart, s.TrimEnd);
    }

    private void TrimSelected()
    {
        int i = SelectedIndex;
        if (i < 0) return;
        var s = _config.Sounds[i];
        if (SoundLibrary.FileMissing(s)) { MessageBox.Show(this, "Arquivo não encontrado:\n" + s.Path, "Ecoar"); return; }
        TrimDialog dlg;
        try { dlg = new TrimDialog(s, _engine); }
        catch (Exception e) { MessageBox.Show(this, $"Não foi possível abrir o som: {e.Message}", "Ecoar"); return; }
        using (dlg)
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _config.Sounds[i] = s with { TrimStart = dlg.TrimStart, TrimEnd = dlg.TrimEnd };
        }
        Save();
        RefreshList();
    }

    private void AddWithDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Arquivos de som|" + string.Join(";", AudioExtensions.Select(e => "*" + e)),
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths.Where(p => AudioExtensions.Contains(Path.GetExtension(p).ToLowerInvariant())))
            _config.Sounds.Add(new SoundEntry(Path.GetFileNameWithoutExtension(p), p, null));
        Save();
        RefreshList();
    }

    private void RemoveSelected()
    {
        if (SelectedIndex < 0) return;
        _config.Sounds.RemoveAt(SelectedIndex);
        Save();
        RegisterHotkeys();
        RefreshList();
    }

    private void RenameSelected()
    {
        int i = SelectedIndex;
        if (i < 0) return;
        string? name = Prompt.Text(this, "Renomear", "Novo nome:", _config.Sounds[i].Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        _config.Sounds[i] = _config.Sounds[i] with { Name = name.Trim() };
        Save();
        RefreshList();
    }

    private void SetSelectedHotkey()
    {
        int i = SelectedIndex;
        if (i < 0) return;
        if (!Prompt.Hotkey(this, out string? hotkey)) return;
        _config.Sounds[i] = _config.Sounds[i] with { Hotkey = hotkey };
        Save();
        RegisterHotkeys();
        RefreshList();
    }

    private void SetStopHotkey()
    {
        if (!Prompt.Hotkey(this, out string? hotkey)) return;
        _config.StopHotkey = hotkey;
        Save();
        RegisterHotkeys();
        UpdateStatus();
    }

    /// <summary>Re-registers every hotkey; a combo taken by another program is reported and cleared.</summary>
    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var failed = new List<string>();
        if (_config.StopHotkey is { } stop && !TryRegister(StopHotkeyId, stop)) { failed.Add(stop); _config.StopHotkey = null; }
        for (int i = 0; i < _config.Sounds.Count; i++)
        {
            if (_config.Sounds[i].Hotkey is not { } hk || TryRegister(i + 1, hk)) continue;
            failed.Add(hk);
            _config.Sounds[i] = _config.Sounds[i] with { Hotkey = null };
        }
        if (failed.Count == 0) return;
        Save();
        MessageBox.Show(this, "Atalho em uso por outro programa: " + string.Join(", ", failed), "Ecoar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private bool TryRegister(int id, string hotkey) =>
        HotkeyParser.TryParse(hotkey, out uint mods, out Keys key) && _hotkeys.Register(id, mods, key);

    private void OnHotkey(int id)
    {
        if (id == StopHotkeyId) { _engine.Stop(); return; }
        if (id - 1 < _config.Sounds.Count) Play(_config.Sounds[id - 1]);
    }

    private void InstallOnMic()
    {
        if (_mics.SelectedItem is not MicItem m) { MessageBox.Show(this, "Nenhum microfone ativo encontrado.", "Ecoar"); return; }
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--setup {m.Id}") { UseShellExecute = true, Verb = "runas" })!;
            p.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception) { /* UAC declined */ }
        UpdateStatus();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;   // keep hotkeys alive in the tray
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer.Dispose();
            _hotkeys.Dispose();
            _engine.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class Prompt
{
    public static string? Text(IWin32Window owner, string title, string label, string value)
    {
        using var f = new Form { Text = title, Width = 360, Height = 150, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var box = new TextBox { Text = value, Left = 12, Top = 32, Width = 320 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 176, Top = 64 };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Left = 256, Top = 64 };
        f.Controls.AddRange(new Control[] { new Label { Text = label, Left = 12, Top = 10, AutoSize = true }, box, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }

    /// <summary>Captures a key combo. Returns false on cancel; hotkey null means "remove".</summary>
    public static bool Hotkey(IWin32Window owner, out string? hotkey)
    {
        string? captured = null;
        using var f = new Form { Text = "Definir atalho", Width = 380, Height = 170, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, KeyPreview = true, MinimizeBox = false, MaximizeBox = false };
        var shown = new Label { Text = "Pressione uma tecla ou combinação (ex.: F5, NumPad1, Ctrl+Alt+1)", Left = 12, Top = 14, AutoSize = true };
        var warn = new Label { Left = 12, Top = 40, Width = 340, Height = 40, ForeColor = Color.DarkGoldenrod };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 116, Top = 92, Enabled = false };
        var clear = new Button { Text = "Remover", DialogResult = DialogResult.Yes, Left = 196, Top = 92 };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Left = 276, Top = 92 };
        f.Controls.AddRange(new Control[] { shown, warn, ok, clear, cancel });
        f.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            string? hk = HotkeyParser.FromKeyPress(e.KeyCode, e.Control, e.Alt, e.Shift);
            if (hk is null) return;   // only a modifier so far
            captured = hk;
            shown.Text = "Atalho: " + captured;
            bool typingKey = !e.Control && !e.Alt && !e.Shift &&
                             (e.KeyCode is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or Keys.Space or Keys.Enter);
            warn.Text = typingKey ? "Atenção: enquanto o Ecoar estiver aberto, essa tecla vai tocar o som em vez de digitar em outros programas." : "";
            ok.Enabled = true;
        };
        var result = f.ShowDialog(owner);
        hotkey = result == DialogResult.OK ? captured : null;
        return result is DialogResult.OK or DialogResult.Yes;
    }
}
