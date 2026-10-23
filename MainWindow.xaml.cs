using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace VRPLauncher;

public partial class MainWindow : Window
{
    sealed record Slot(int Offset, Border Border, Image Image, TextBlock Placeholder);
    sealed record TableFilter(string Name, Func<TableEntry, bool> Matches);
    enum ViewerMode { Closed, Rules }

    const int ViewerDecodeHeight = 1000;


    readonly string _settingsPath;
    readonly string _statePath;
    readonly string _vpsCacheDir;
    readonly string _wheelMediaDir;
    readonly string _rulesMediaDir;
    readonly MusicPlayer _music;
    readonly LauncherState _state;
    readonly List<Slot> _slots = [];
    readonly DispatcherTimer _exitTimer;
    readonly DispatcherTimer _refocusTimer;
    Settings _settings;
    KeyMap _keys = null!;
    ThumbnailFinder _thumbnails = null!;
    VpsClient? _vps;
    VpsIndex? _vpsIndex;
    bool _vpsLoading;

    /// <summary>Every table found on disk.</summary>
    List<TableEntry> _allTables = [];

    /// <summary>The tables the current filter lets through; this is what the carousel browses.</summary>
    List<TableEntry> _tables = [];

    List<TableFilter> _filters = [];
    int _filterIndex;
    int _index;
    ViewerMode _viewer;
    int _viewerRequest;
    bool _running;
    DateTime _ignoreInputUntil = DateTime.MinValue;
    Key? _exitKey;
    DateTime _exitHoldStart;
    DateTime _refocusUntil;

    public MainWindow(string settingsPath)
    {
        InitializeComponent();

        _settingsPath = settingsPath;
        var dataDir = Path.GetDirectoryName(settingsPath)!;
        _statePath = Path.Combine(dataDir, "state.json");
        _vpsCacheDir = Path.Combine(dataDir, "vps");

        // Built-in image folders next to the exe: drop "Table Name (Maker Year).png" files in to add or replace pictures.
        _wheelMediaDir = Path.Combine(dataDir, "Media", "Wheels");
        _rulesMediaDir = Path.Combine(dataDir, "Media", "Rules");
        var musicDir = Path.Combine(dataDir, "Media", "Music");
        try
        {
            Directory.CreateDirectory(_wheelMediaDir);
            Directory.CreateDirectory(_rulesMediaDir);
            Directory.CreateDirectory(musicDir);
        }
        catch
        {
            // Read-only install location: the configured folders still work.
        }
        _settings = Settings.LoadOrCreate(settingsPath, out var settingsError);
        _state = LauncherState.Load(_statePath);

        _music = new MusicPlayer(musicDir, Path.Combine(dataDir, "cache"));
        CreateSlots();
        ApplySettings(settingsError);

        _exitTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Normal, OnExitTimer, Dispatcher);
        _exitTimer.Stop();
        _refocusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, OnRefocusTimer, Dispatcher);
        _refocusTimer.Stop();

        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        Deactivated += (_, _) => CancelExitHold();
        Loaded += (_, _) =>
        {
            BringToFront();
            _music.Play();
            // First run, or paths that no longer exist: go straight to setup.
            if (!File.Exists(_settings.VpxExePath) || !Directory.Exists(_settings.TablesFolder))
                Dispatcher.BeginInvoke(OpenSettings, DispatcherPriority.ApplicationIdle);
        };
    }

    /// <summary>(Re)applies the current settings: window mode, key map, table list and thumbnails.</summary>
    void ApplySettings(string? settingsError = null)
    {
        if (_settings.Fullscreen)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Cursor = Cursors.None;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Cursor = null;
        }

        _keys = new KeyMap(_settings.Keys);
        _music.Configure(_settings.MusicEnabled, _settings.MusicVolume);
        _vps = _settings.UseVps ? new VpsClient(_vpsCacheDir) : null;
        _thumbnails = new ThumbnailFinder(_settings, _vps,
            [_wheelMediaDir, .. _settings.ImageFolders],
            [_rulesMediaDir, .. _settings.RulesFolders]);
        _allTables = TableScanner.Scan(_settings, out var scanError);

        HelpText.Text =
            "Flippers: browse    " +
            "Magna Saves: jump letter    " +
            $"{ButtonName(LauncherAction.Launch)}: play    " +
            $"{ButtonName(LauncherAction.View)}: rules    " +
            $"{_keys.Describe(LauncherAction.NextFilter)}: filter    " +
            $"hold {_keys.Describe(LauncherAction.Exit)}: quit    " +
            $"{_keys.Describe(LauncherAction.Settings)}: settings";

        var errors = new List<string>();
        if (settingsError != null) errors.Add(settingsError);
        if (scanError != null) errors.Add(scanError);
        else if (_allTables.Count == 0) errors.Add($"No .vpx tables found in {_settings.TablesFolder}");
        if (_keys.InvalidNames.Count > 0) errors.Add("Unknown key names in settings.json: " + string.Join(", ", _keys.InvalidNames));
        if (errors.Count > 0) ShowStatus(string.Join("\n", errors));
        else HideStatus();

        if (_vps != null && _vpsIndex != null)
            MatchVps();
        BuildFilters();
        ApplyFilter(_state.LastTable);

        if (_vps != null && _vpsIndex == null)
            _ = LoadVpsAsync();
    }

    void OpenSettings()
    {
        if (_running || OwnedWindows.Count > 0) return;
        CancelExitHold();
        _refocusTimer.Stop(); // it would pull focus away from the dialog
        var dialog = new SettingsWindow(_settings.Clone(), _wheelMediaDir, _rulesMediaDir) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            string? saveError = null;
            try
            {
                dialog.Result.Save(_settingsPath);
            }
            catch (Exception ex)
            {
                saveError = $"Could not save {_settingsPath}: {ex.Message}";
            }
            _settings = dialog.Result;
            ApplySettings(saveError);
        }
        _ignoreInputUntil = DateTime.UtcNow.AddMilliseconds(300);
        BringToFront();
    }

    // ---------- Virtual Pinball Spreadsheet ----------

    /// <summary>Uses the cached database straight away, then checks VPS for a newer one in the background.</summary>
    async Task LoadVpsAsync()
    {
        if (_vpsLoading || _vps == null) return;
        _vpsLoading = true;
        var client = _vps;
        try
        {
            if (await Task.Run(client.LoadCached) is { } cached)
                await UseVpsDatabase(cached);
            try
            {
                if (await Task.Run(client.RefreshAsync) is { } fresh)
                    await UseVpsDatabase(fresh);
            }
            catch (Exception ex)
            {
                if (_vpsIndex == null)
                    ShowStatus($"Could not download the VPS database: {ex.Message}");
            }
        }
        finally
        {
            _vpsLoading = false;
        }
    }

    async Task UseVpsDatabase(List<VpsGame> games)
    {
        _vpsIndex = await Task.Run(() => new VpsIndex(games));
        if (_vps == null) return; // VPS was switched off while loading
        MatchVps();
        BuildFilters();
        ApplyFilter(null);
    }

    void MatchVps()
    {
        foreach (var table in _allTables)
        {
            table.Vps = _vpsIndex?.Match(table);
            // Tables that had no local picture may get a VPS backglass now.
            if (table.Image == null)
                table.ImageRequested = false;
        }
    }

    static string? DescribeType(string? type) => type switch
    {
        "EM" => "Electro-mechanical",
        "SS" => "Solid state",
        "PM" => "Pure mechanical",
        _ => type,
    };

    // ---------- Filters ----------

    /// <summary>All tables, VPS updates, machine type, decade, then each manufacturer with at least 3 tables.</summary>
    void BuildFilters()
    {
        var current = _filters.Count > 0 ? _filters[_filterIndex].Name : null;
        var filters = new List<TableFilter> { new("All tables", _ => true) };

        if (_allTables.Any(t => t.Vps?.UpdateVersion != null))
            filters.Add(new("Updates on VPS", t => t.Vps?.UpdateVersion != null));

        foreach (var type in new[] { "EM", "SS", "PM" })
            if (_allTables.Any(t => t.Type == type))
                filters.Add(new(DescribeType(type)!, t => t.Type == type));

        foreach (var decade in _allTables.Where(t => t.DisplayYear != null).Select(t => t.DisplayYear!.Value / 10 * 10).Distinct().Order())
            filters.Add(new($"{decade}s", t => t.DisplayYear / 10 * 10 == decade));

        foreach (var maker in _allTables
                     .Where(t => !string.IsNullOrWhiteSpace(t.DisplayManufacturer))
                     .GroupBy(t => t.DisplayManufacturer!, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() >= 3)
                     .Select(g => g.Key)
                     .Order(StringComparer.OrdinalIgnoreCase))
            filters.Add(new(maker, t => string.Equals(t.DisplayManufacturer, maker, StringComparison.OrdinalIgnoreCase)));

        _filters = filters;
        _filterIndex = Math.Max(0, filters.FindIndex(f => f.Name == current));
    }

    /// <summary>Rebuilds the browsed list from the current filter, keeping the given (or current) table selected if it is still in it.</summary>
    void ApplyFilter(string? selectPath)
    {
        selectPath ??= _tables.Count > 0 ? _tables[_index].FilePath : null;
        _tables = _allTables.Where(_filters[_filterIndex].Matches).ToList();
        _index = Math.Max(0, _tables.FindIndex(t => t.FilePath.Equals(selectPath, StringComparison.OrdinalIgnoreCase)));
        UpdateView();
    }

    void ChangeFilter(int delta)
    {
        if (_filters.Count < 2) return;
        _filterIndex = Mod(_filterIndex + delta, _filters.Count);
        HideStatus();
        ApplyFilter(null);
    }

    // ---------- View ----------

    void CreateSlots()
    {
        for (var offset = -2; offset <= 2; offset++)
        {
            var distance = Math.Abs(offset);
            var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(10) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var placeholder = new TextBlock
            {
                FontSize = distance == 0 ? 56 : 34,
                FontFamily = (FontFamily)FindResource("TitleFont"),
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("SilverWhite"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20),
            };
            // Just the coloured outline, so the background shows through the square. It is a separate
            // element so the selected table's glow comes from the outline only, not the wheel art.
            var frame = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderBrush = (Brush)FindResource("NexusCyan"),
                BorderThickness = new Thickness(distance == 0 ? 6 : 3),
                Effect = distance == 0 ? new DropShadowEffect { Color = Color.FromRgb(0x00, 0xD8, 0xFF), BlurRadius = 40, ShadowDepth = 0, Opacity = 0.8 } : null,
            };
            var border = new Border
            {
                Margin = distance switch { 0 => new Thickness(16, 10, 16, 10), 1 => new Thickness(12, 80, 12, 80), _ => new Thickness(10, 140, 10, 140) },
                Opacity = distance switch { 0 => 1.0, 1 => 0.5, _ => 0.25 },
                Child = new Grid { Children = { frame, placeholder, image } },
            };
            Grid.SetColumn(border, offset + 2);
            Carousel.Children.Add(border);
            _slots.Add(new Slot(offset, border, image, placeholder));
        }
    }

    void UpdateView()
    {
        FilterPanel.Visibility = _filterIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        FilterText.Text = _filters.Count > 0 ? _filters[_filterIndex].Name.ToUpperInvariant() : "";

        if (_tables.Count == 0)
        {
            foreach (var slot in _slots) slot.Border.Visibility = Visibility.Hidden;
            TitleText.Text = "No tables";
            InfoText.Text = _settings.TablesFolder;
            UpdatePanel.Visibility = Visibility.Collapsed;
            CounterText.Text = "";
            LetterText.Text = "";
            _viewer = ViewerMode.Closed;
            UpdateViewer();
            return;
        }

        var current = _tables[_index];
        TitleText.Text = current.Name; // maker and year are on the info line below
        InfoText.Text = DescribeGame(current);
        UpdatePanel.Visibility = current.Vps?.UpdateVersion != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateText.Text = $"Newer version on VPS: {current.Vps?.UpdateVersion}";
        CounterText.Text = $"{_index + 1} / {_tables.Count}";
        LetterText.Text = current.Group.ToString();

        foreach (var slot in _slots)
        {
            // Hide neighbours that would just repeat tables when the list is short.
            if (_tables.Count <= Math.Abs(slot.Offset) * 2)
            {
                slot.Border.Visibility = Visibility.Hidden;
                continue;
            }
            var table = TableAt(slot.Offset);
            slot.Border.Visibility = Visibility.Visible;
            slot.Border.Tag = table;
            slot.Image.Source = table.Image;
            slot.Placeholder.Text = table.Image == null ? table.Title : "";
            RequestImage(table);
        }

        // Warm up the next few in each direction so fast scrolling still shows pictures.
        for (var offset = 3; offset <= 5 && offset * 2 < _tables.Count; offset++)
        {
            RequestImage(TableAt(offset));
            RequestImage(TableAt(-offset));
        }

        UpdateViewer();
    }

    /// <summary>"Williams · 1985 · Solid state" from VPS, or maker and year from the file name.</summary>
    static string DescribeGame(TableEntry table)
    {
        if (table.Vps?.Game is not { } game)
            return string.Join("  ·  ", new[] { table.Manufacturer, table.Year?.ToString() }.Where(s => !string.IsNullOrEmpty(s)));

        var parts = new List<string?>
        {
            game.Manufacturer,
            game.Year?.ToString(),
            DescribeType(game.Type),
        };
        return string.Join("  ·  ", parts.Where(s => !string.IsNullOrEmpty(s)));
    }

    TableEntry TableAt(int offset) => _tables[Mod(_index + offset, _tables.Count)];

    void RequestImage(TableEntry table)
    {
        if (table.ImageRequested) return;
        table.ImageRequested = true;
        var thumbnails = _thumbnails;
        Task.Run(() => thumbnails.Load(table)).ContinueWith(task =>
        {
            table.Image = task.Result;
            if (table.Image == null) return;
            foreach (var slot in _slots.Where(s => s.Border.Tag == table))
            {
                slot.Image.Source = table.Image;
                slot.Placeholder.Text = "";
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusPanel.Visibility = Visibility.Visible;
    }

    void HideStatus() => StatusPanel.Visibility = Visibility.Collapsed;

    /// <summary>
    /// The stub button an action is on, for the on-screen help. These name the cabinet buttons rather than
    /// the keys they send; actions without a stub button yet fall back to the key name.
    /// </summary>
    string ButtonName(LauncherAction action) => action switch
    {
        LauncherAction.Launch => "Start",
        LauncherAction.View => "Coin",
        _ => _keys.Describe(action),
    };

    // ---------- Rules viewer ----------

    /// <summary>Opens or closes the rules card.</summary>
    void CycleViewer()
    {
        if (_tables.Count == 0) return;
        _viewer = _viewer == ViewerMode.Closed ? ViewerMode.Rules : ViewerMode.Closed;
        UpdateViewer();
    }

    void CloseViewer()
    {
        _viewer = ViewerMode.Closed;
        UpdateViewer();
    }

    /// <summary>Shows the current table's rules card, loading it in the background.</summary>
    void UpdateViewer()
    {
        var request = ++_viewerRequest;
        if (_viewer == ViewerMode.Closed || _tables.Count == 0)
        {
            ViewerPanel.Visibility = Visibility.Collapsed;
            ViewerImage.Source = null;
            return;
        }

        var table = _tables[_index];
        ViewerPanel.Visibility = Visibility.Visible;
        ViewerCaption.Text = $"RULES  ·  {table.Title}";
        ViewerHint.Text =
            $"{ButtonName(LauncherAction.View)}: close    " +
            "Flippers: browse    " +
            $"{ButtonName(LauncherAction.Launch)}: play";
        ViewerImage.Source = null;
        ViewerMessage.Text = "Loading…";

        var thumbnails = _thumbnails;
        Task.Run(() => thumbnails.LoadRules(table, ViewerDecodeHeight))
            .ContinueWith(task =>
            {
                if (request != _viewerRequest) return; // the user has moved on
                ViewerImage.Source = task.Result;
                ViewerMessage.Text = task.Result != null ? ""
                    : $"No rules card for this table\nAdd one to Media\\Rules as \"{table.Title}.png\"";
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ---------- Input ----------

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (!_keys.TryGet(key, out var action))
            return;
        e.Handled = true;

        if (_running || DateTime.UtcNow < _ignoreInputUntil)
            return;

        switch (action)
        {
            case LauncherAction.Previous: Move(-1); break;
            case LauncherAction.Next: Move(1); break;
            case LauncherAction.PreviousLetter: JumpLetter(-1); break;
            case LauncherAction.NextLetter: JumpLetter(1); break;
            case LauncherAction.NextFilter when !e.IsRepeat: ChangeFilter(1); break;
            case LauncherAction.PreviousFilter when !e.IsRepeat: ChangeFilter(-1); break;
            case LauncherAction.View when !e.IsRepeat: CycleViewer(); break;
            case LauncherAction.Launch when !e.IsRepeat: Launch(); break;
            // In the viewer, Exit just closes the viewer.
            case LauncherAction.Exit when !e.IsRepeat && _viewer != ViewerMode.Closed: CloseViewer(); break;
            case LauncherAction.Exit when !e.IsRepeat: StartExitHold(key); break;
            case LauncherAction.Settings when !e.IsRepeat: OpenSettings(); break;
            case LauncherAction.ToggleMusic when !e.IsRepeat: ToggleMusic(); break;
        }
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == _exitKey)
            CancelExitHold();
    }

    void Move(int delta)
    {
        if (_tables.Count == 0) return;
        _index = Mod(_index + delta, _tables.Count);
        HideStatus();
        UpdateView();
    }

    /// <summary>Back: to the first table of this letter, or of the previous letter if already there. Forward: to the next letter.</summary>
    void JumpLetter(int direction)
    {
        if (_tables.Count == 0) return;
        var count = _tables.Count;
        var group = _tables[_index].Group;
        var i = _index;

        if (direction > 0)
        {
            for (var step = 1; step < count; step++)
            {
                i = Mod(_index + step, count);
                if (_tables[i].Group != group) break;
            }
        }
        else
        {
            if (_tables[Mod(i - 1, count)].Group != group)
                i = Mod(i - 1, count); // already at the start of this letter: step into the previous one
            var target = _tables[i].Group;
            for (var step = 0; step < count && _tables[Mod(i - 1, count)].Group == target; step++)
                i = Mod(i - 1, count);
        }

        _index = i;
        HideStatus();
        UpdateView();
    }

    /// <summary>Turns the music on or off and remembers the choice.</summary>
    void ToggleMusic()
    {
        _settings.MusicEnabled = !_settings.MusicEnabled;
        _music.ToggleEnabled(_settings.MusicEnabled);
        try { _settings.Save(_settingsPath); } catch { /* still toggled for this session */ }
    }

    void StartExitHold(Key key)
    {
        if (_settings.ExitHoldMs <= 0)
        {
            Close();
            return;
        }
        _exitKey = key;
        _exitHoldStart = DateTime.UtcNow;
        ExitProgress.Value = 0;
        ExitPanel.Visibility = Visibility.Visible;
        _exitTimer.Start();
    }

    void OnExitTimer(object? sender, EventArgs e)
    {
        var progress = (DateTime.UtcNow - _exitHoldStart).TotalMilliseconds / _settings.ExitHoldMs;
        ExitProgress.Value = Math.Min(1, progress);
        if (progress >= 1)
        {
            _exitTimer.Stop();
            Close();
        }
    }

    void CancelExitHold()
    {
        _exitKey = null;
        _exitTimer?.Stop();
        ExitPanel.Visibility = Visibility.Collapsed;
    }

    // ---------- Launching ----------

    async void Launch()
    {
        if (_tables.Count == 0 || _running) return;
        var table = _tables[_index];

        if (!File.Exists(_settings.VpxExePath))
        {
            ShowStatus($"VPX not found: {_settings.VpxExePath}\nPress {_keys.Describe(LauncherAction.Settings)} to set it");
            return;
        }

        var startInfo = new ProcessStartInfo(_settings.VpxExePath, _settings.LaunchArguments.Replace("{table}", table.FilePath))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(_settings.VpxExePath)!,
        };

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not start VPX: {ex.Message}");
            return;
        }
        if (process == null)
        {
            ShowStatus("Could not start VPX.");
            return;
        }

        _state.LastTable = table.FilePath;
        _state.Save(_statePath);

        _running = true;
        CancelExitHold();
        CloseViewer();
        HideStatus();
        PlayingText.Text = table.Title;
        PlayingPanel.Visibility = Visibility.Visible;
        _music.Pause();
        if (_settings.MinimizeWhileRunning)
            WindowState = WindowState.Minimized;

        // Wait for VPX to exit, checking once a second for a VR start-up error box it would otherwise sit behind.
        string? vrError = null;
        var errorClosedAt = DateTime.MaxValue;
        using (process)
        {
            var exited = process.WaitForExitAsync();
            while (await Task.WhenAny(exited, Task.Delay(1000)) != exited)
            {
                if (vrError == null && VrErrorWatcher.CloseVrError(process) is { } error)
                {
                    vrError = error;
                    errorClosedAt = DateTime.UtcNow;
                }
                // Closing the error box normally ends VPX; if it doesn't within a few seconds, end it.
                else if (DateTime.UtcNow - errorClosedAt > TimeSpan.FromSeconds(5))
                {
                    try { process.Kill(); } catch { /* already gone */ }
                }
            }
        }

        _running = false;
        PlayingPanel.Visibility = Visibility.Collapsed;
        _ignoreInputUntil = DateTime.UtcNow.AddMilliseconds(_settings.InputCooldownMs);
        if (vrError != null)
            ShowStatus(vrError);
        _music.Play();
        BringToFront();
    }

    void BringToFront()
    {
        WindowState = _settings.Fullscreen ? WindowState.Maximized : WindowState.Normal;
        ForceFocus();

        // Other windows (B2S backglass, VPX shutting down) can steal focus back for a moment, so keep checking briefly.
        _refocusUntil = DateTime.UtcNow.AddSeconds(4);
        _refocusTimer.Start();
    }

    void OnRefocusTimer(object? sender, EventArgs e)
    {
        if (_running || DateTime.UtcNow > _refocusUntil)
        {
            _refocusTimer.Stop();
            return;
        }
        if (!ForegroundWindow.IsForeground(new WindowInteropHelper(this).Handle))
            ForceFocus();
    }

    void ForceFocus()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        ForegroundWindow.Force(handle);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
