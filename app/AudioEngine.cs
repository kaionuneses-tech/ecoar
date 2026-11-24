using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Ecoar;

/// <summary>Plays one sound at a time to the headphones and, through MicTee, into the mic ring.</summary>
public sealed class AudioEngine : IDisposable
{
    private readonly Func<IRingSink?> _openSink;
    private readonly System.Threading.Timer _timer;
    private readonly object _lock = new();
    private IRingSink? _sink;
    private WaveStream? _reader;
    private WasapiOut? _output;
    private MicTee? _tee;
    private long _lastHeartbeat = -1;
    private DateTime _lastBeatChange = DateTime.MinValue;
    private float _micVolume = 1f, _headphoneVolume = 0.8f;
    private bool _hearMyself = true;
    private bool _toMic = true;

    public AudioEngine() : this(RingWriter.TryOpen) { }

    internal AudioEngine(Func<IRingSink?> openSink)
    {
        _openSink = openSink;
        _sink = openSink();
        _timer = new System.Threading.Timer(_ => Tick(), null, 1000, 1000);
    }

    public event Action<string>? Error;

    public float MicVolume { get => _micVolume; set { _micVolume = value; Apply(); } }
    public float HeadphoneVolume { get => _headphoneVolume; set { _headphoneVolume = value; Apply(); } }
    public bool HearMyself { get => _hearMyself; set { _hearMyself = value; Apply(); } }
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    /// <summary>True when the ring is open and an APO heartbeat moved in the last second.</summary>
    public bool EffectActive
    {
        get
        {
            lock (_lock)
                return _sink is not null && DateTime.UtcNow - _lastBeatChange < TimeSpan.FromSeconds(1.5);
        }
    }

    /// <summary>Plays [start, end) of the file; toMic false = headphone-only preview (trim editor).</summary>
    public void Play(string path, double? start = null, double? end = null, bool toMic = true)
    {
        Stop();
        WaveStream reader;
        ISampleProvider source;
        try
        {
            reader = AudioFile.Open(path);
            source = AudioTrim.Apply(reader, start, end);
        }
        catch (Exception e)
        {
            Error?.Invoke($"Não foi possível abrir o arquivo \"{Path.GetFileName(path)}\": {e.Message}");
            return;
        }
        var tee = new MicTee(source);
        try
        {
            var output = new WasapiOut(AudioClientShareMode.Shared, 50);
            output.Init(tee);
            lock (_lock)
            {
                _reader = reader;
                _tee = tee;
                _output = output;
                _toMic = toMic;
                Apply();
            }
            // Only the sound that finished may stop itself (an old output's event must not kill a newer sound);
            // dispose off the callback thread.
            output.PlaybackStopped += (_, _) => Task.Run(() => StopIfCurrent(output));
            output.Play();
        }
        catch (Exception e)
        {
            reader.Dispose();
            Error?.Invoke($"Não foi possível tocar nos fones: {e.Message}");
        }
    }

    public void Stop() => StopIfCurrent(null);

    private void StopIfCurrent(WasapiOut? expected)
    {
        WasapiOut? output; WaveStream? reader;
        lock (_lock)
        {
            if (expected is not null && !ReferenceEquals(expected, _output)) return;
            output = _output; reader = _reader;
            _output = null; _reader = null; _tee = null;
        }
        if (output is not null)
        {
            output.Stop();
            output.Dispose();
        }
        reader?.Dispose();
    }

    private void Apply()
    {
        lock (_lock)
        {
            if (_tee is null) return;
            _tee.MicVolume = _micVolume;
            // A preview must be audible even when "Ouvir eu mesmo" is off.
            _tee.HeadphoneVolume = _hearMyself || !_toMic ? _headphoneVolume : 0f;
            _tee.Sink = _toMic ? _sink : null;
        }
    }

    private void Tick()
    {
        lock (_lock)
        {
            _sink ??= _openSink();
            if (_sink is RingWriter ring)
            {
                long beat = ring.Heartbeat;
                if (beat != _lastHeartbeat) { _lastHeartbeat = beat; _lastBeatChange = DateTime.UtcNow; }
            }
            else if (_sink is not null)
            {
                _lastBeatChange = DateTime.UtcNow;   // test sinks have no heartbeat
            }
            if (_tee is not null) _tee.Sink = _toMic ? _sink : null;
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        Stop();
        (_sink as IDisposable)?.Dispose();
    }
}
