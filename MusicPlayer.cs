using System.Windows.Media;
using System.Windows.Threading;

namespace VRPLauncher;

/// <summary>
/// Quiet background music. Plays MP3s from Media\Music in shuffled order, or the track built into the exe
/// when that folder is empty. Fades in and out so starting, stopping and looping are never abrupt.
/// </summary>
sealed class MusicPlayer
{
    const string BuiltInTrack = "vpinnexus.mp3";
    static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan LoopFade = TimeSpan.FromSeconds(1.5);

    readonly MediaPlayer _player = new();
    readonly DispatcherTimer _timer;
    readonly string _musicDir;
    readonly string _cacheDir;
    readonly Random _random = new();

    List<string> _tracks = [];
    int _track = -1;
    bool _enabled;
    bool _paused = true;
    double _volume;
    double _target;     // volume being faded towards
    double _step;       // change per timer tick
    bool _stopWhenSilent;

    public MusicPlayer(string musicDir, string cacheDir)
    {
        _musicDir = musicDir;
        _cacheDir = cacheDir;
        _player.MediaEnded += (_, _) => PlayNextTrack();
        _player.MediaFailed += (_, _) => PlayNextTrack();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        _timer.Stop();
    }

    /// <summary>Applies the music settings; picks up new files in Media\Music too.</summary>
    public void Configure(bool enabled, double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        _tracks = FindTracks();
        _enabled = enabled && _tracks.Count > 0;

        if (!_enabled)
        {
            FadeTo(0, stopWhenSilent: true);
            return;
        }
        if (!_paused)
            Play(); // resumes if it had been switched off, or just fades to the new volume
    }

    /// <summary>Starts or resumes, fading in.</summary>
    public void Play()
    {
        _paused = false;
        if (!_enabled) return;
        if (_track < 0 || _player.Source == null)
            PlayNextTrack();
        else
            _player.Play();
        FadeTo(_volume);
    }

    /// <summary>Fades out and pauses, e.g. while a table is being played.</summary>
    public void Pause()
    {
        _paused = true;
        FadeTo(0, stopWhenSilent: true);
    }

    public void ToggleEnabled(bool enabled)
    {
        _enabled = enabled && _tracks.Count > 0;
        if (_enabled && !_paused) Play();
        else FadeTo(0, stopWhenSilent: true);
    }

    void PlayNextTrack()
    {
        if (!_enabled || _tracks.Count == 0) return;

        // Shuffle without repeating the same track twice in a row (unless it is the only one).
        var next = _tracks.Count == 1 ? 0 : _random.Next(_tracks.Count - 1);
        if (_tracks.Count > 1 && next >= _track) next++;
        _track = next;

        var uri = new Uri(_tracks[_track]);
        if (_player.Source == uri)
            _player.Position = TimeSpan.Zero; // same track again: just rewind
        else
            _player.Open(uri);
        _player.Volume = 0;
        if (!_paused)
        {
            _player.Play();
            FadeTo(_volume);
        }
    }

    void FadeTo(double target, bool stopWhenSilent = false)
    {
        _target = target;
        _stopWhenSilent = stopWhenSilent;
        var ticks = FadeTime.TotalMilliseconds / _timer.Interval.TotalMilliseconds;
        _step = Math.Max(Math.Abs(_target - _player.Volume), 0.01) / ticks;
        _timer.Start();
    }

    void Tick()
    {
        // Fade down just before a track ends, so the loop or next track starts from silence.
        if (!_paused && _enabled && _player.NaturalDuration.HasTimeSpan
            && _player.NaturalDuration.TimeSpan - _player.Position < LoopFade && _target > 0)
        {
            _target = 0;
            _step = Math.Max(_player.Volume, 0.01) / (LoopFade.TotalMilliseconds / _timer.Interval.TotalMilliseconds);
        }

        var volume = _player.Volume;
        if (Math.Abs(volume - _target) <= _step)
        {
            _player.Volume = _target;
            if (_target == 0 && _stopWhenSilent)
                _player.Pause();
            // Keep ticking while playing so the end-of-track fade can kick in.
            if (_paused || !_enabled)
                _timer.Stop();
            return;
        }
        _player.Volume = volume + (volume < _target ? _step : -_step);
    }

    List<string> FindTracks()
    {
        var tracks = new List<string>();
        try
        {
            if (Directory.Exists(_musicDir))
                tracks.AddRange(Directory.EnumerateFiles(_musicDir)
                    .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".mp3" or ".m4a" or ".wma" or ".wav"));
        }
        catch
        {
            // Unreadable folder: fall back to the built-in track.
        }
        if (tracks.Count == 0 && ExtractBuiltIn() is { } builtIn)
            tracks.Add(builtIn);
        return tracks;
    }

    /// <summary>MediaPlayer can only play files, so the built-in track is copied out of the exe once.</summary>
    string? ExtractBuiltIn()
    {
        try
        {
            using var resource = typeof(MusicPlayer).Assembly.GetManifestResourceStream("Audio." + BuiltInTrack);
            if (resource == null) return null;
            var path = Path.Combine(_cacheDir, BuiltInTrack);
            if (!File.Exists(path) || new FileInfo(path).Length != resource.Length)
            {
                Directory.CreateDirectory(_cacheDir);
                using var file = File.Create(path);
                resource.CopyTo(file);
            }
            return path;
        }
        catch
        {
            return null;
        }
    }
}
