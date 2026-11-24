namespace Ecoar;

/// <summary>Discord-style trim editor: waveform with start/end handles and a headphone-only preview.</summary>
internal sealed class TrimDialog : Form
{
    private const int HandleGrab = 10;   // px around a handle that grabs it

    private readonly AudioEngine _engine;
    private readonly string _path;
    private readonly float[] _peaks;
    private readonly double _duration;
    private readonly WavePanel _wave = new() { Dock = DockStyle.Fill, BackColor = Color.Black, Cursor = Cursors.SizeWE };
    private readonly Label _length = new() { AutoSize = true, ForeColor = Color.FromArgb(87, 242, 135), BackColor = Color.Black, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
    private double _start, _end;
    private int _dragging;   // 0 none, 1 start handle, 2 end handle

    public double? TrimStart => _start <= 0.005 ? null : Math.Round(_start, 3);
    public double? TrimEnd => _end >= _duration - 0.005 ? null : Math.Round(_end, 3);

    public TrimDialog(SoundEntry sound, AudioEngine engine)
    {
        _engine = engine;
        _path = sound.Path;
        using (var reader = AudioFile.Open(sound.Path)) _duration = Math.Max(0.1, reader.TotalTime.TotalSeconds);
        _peaks = Waveform.ComputePeaks(sound.Path, 600);
        _start = TrimMath.ClampStart(sound.TrimStart ?? 0, _duration);
        _end = TrimMath.ClampEnd(sound.TrimEnd ?? _duration, _start, _duration);

        Text = "Recortar — " + sound.Name;
        Width = 640;
        Height = 260;
        MinimumSize = new Size(420, 220);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(17, 18, 20);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);

        var play = new Button { Text = "▶", Size = new Size(44, 40), Location = new Point(6, 0), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.Black, Font = new Font("Segoe UI", 12f) };
        play.FlatAppearance.BorderSize = 0;
        play.Click += (_, _) => _engine.Play(_path, _start, _end, toMic: false);
        var left = new Panel { Dock = DockStyle.Left, Width = 56, BackColor = Color.Black };
        _length.Location = new Point(6, 0);
        left.Controls.Add(play);
        left.Resize += (_, _) => { play.Top = (left.Height - play.Height) / 2 - 10; _length.Top = play.Bottom + 2; };
        left.Controls.Add(_length);

        var waveArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 10, 10), BackColor = Color.Black };
        waveArea.Controls.Add(_wave);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        body.Controls.Add(waveArea);
        body.Controls.Add(left);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        var save = new Button { Text = "Salvar", DialogResult = DialogResult.OK, AutoSize = true, BackColor = Color.FromArgb(88, 101, 242), FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true, FlatStyle = FlatStyle.Flat };
        var reset = new Button { Text = "Som inteiro", AutoSize = true, FlatStyle = FlatStyle.Flat };
        reset.Click += (_, _) => { _start = 0; _end = _duration; Changed(); };
        buttons.Controls.AddRange(new Control[] { save, cancel, reset });
        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(body);
        Controls.Add(new Label { Text = "Arraste as barras brancas para escolher o trecho. ▶ toca só nos seus fones.", Dock = DockStyle.Top, Padding = new Padding(12, 10, 0, 0), AutoSize = true, ForeColor = Color.Gainsboro });
        Controls.Add(buttons);

        _wave.Paint += (_, e) => PaintWave(e.Graphics);
        _wave.MouseDown += (_, e) => { _dragging = PickHandle(e.X); MoveHandle(e.X); };
        _wave.MouseMove += (_, e) => { if (_dragging != 0) MoveHandle(e.X); };
        _wave.MouseUp += (_, _) => _dragging = 0;
        FormClosed += (_, _) => _engine.Stop();
        Changed();
    }

    private int StartX => TrimMath.SecondsToX(_start, _wave.Width, _duration);
    private int EndX => TrimMath.SecondsToX(_end, _wave.Width, _duration);

    /// <summary>Grab the handle under the pointer, or else the nearer one (a click moves it there).</summary>
    private int PickHandle(int x)
    {
        int ds = Math.Abs(x - StartX), de = Math.Abs(x - EndX);
        if (ds <= HandleGrab && ds <= de) return 1;
        if (de <= HandleGrab) return 2;
        return ds < de ? 1 : 2;
    }

    private void MoveHandle(int x)
    {
        double t = TrimMath.XToSeconds(x, _wave.Width, _duration);
        if (_dragging == 1) _start = TrimMath.ClampStart(t, _end);
        else if (_dragging == 2) _end = TrimMath.ClampEnd(t, _start, _duration);
        Changed();
    }

    private void Changed()
    {
        _length.Text = $"{_end - _start:0.00}s";
        _wave.Invalidate();
    }

    private void PaintWave(Graphics g)
    {
        int w = _wave.Width, h = _wave.Height, mid = h / 2;
        if (w <= 0) return;
        int sx = StartX, ex = EndX;
        using var on = new Pen(Color.White);
        using var off = new Pen(Color.FromArgb(70, 70, 75));
        for (int x = 0; x < w; x += 2)   // Discord-like spaced bars
        {
            float peak = _peaks[Math.Min(_peaks.Length - 1, x * _peaks.Length / w)];
            int half = Math.Max(1, (int)(peak * (h / 2 - 4)));
            g.DrawLine(x >= sx && x <= ex ? on : off, x, mid - half, x, mid + half);
        }
        using var handle = new SolidBrush(Color.White);
        using var grip = new SolidBrush(Color.Gray);
        foreach (int hx in new[] { sx, ex })
        {
            int left = Math.Clamp(hx - 3, 0, w - 6);
            g.FillRectangle(handle, left, 0, 6, h);
            g.FillRectangle(grip, left + 2, mid - 8, 2, 16);
        }
    }

    private sealed class WavePanel : Panel
    {
        public WavePanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }
}
