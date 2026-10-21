using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace VRPLauncher;

public partial class MainWindow : Window
{
    sealed record Slot(int Offset, Border Border, Image Image, TextBlock Placeholder);

    static readonly Brush SelectedBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x00)));
    static readonly Brush SlotBackground = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)));

    readonly string _settingsPath;
    readonly string _statePath;
    readonly LauncherState _state;
    readonly List<Slot> _slots = [];
    Settings _settings;
    KeyMap _keys = null!;
    ThumbnailFinder _thumbnails = null!;
    List<TableEntry> _tables = [];
    readonly DispatcherTimer _exitTimer;
    readonly DispatcherTimer _refocusTimer;

    int _index;
    bool _running;
    DateTime _ignoreInputUntil = DateTime.MinValue;
    Key? _exitKey;
    DateTime _exitHoldStart;
    DateTime _refocusUntil;

    public MainWindow(string settingsPath)
    {
        InitializeComponent();

        _settingsPath = settingsPath;
        _statePath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "state.json");
        _settings = Settings.LoadOrCreate(settingsPath, out var settingsError);
        _state = LauncherState.Load(_statePath);

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
        _thumbnails = new ThumbnailFinder(_settings);
        _tables = TableScanner.Scan(_settings, out var scanError);

        HelpText.Text =
            $"{_keys.Describe(LauncherAction.Previous)} / {_keys.Describe(LauncherAction.Next)}: browse     " +
            $"{_keys.Describe(LauncherAction.PreviousLetter)} / {_keys.Describe(LauncherAction.NextLetter)}: jump letter     " +
            $"{_keys.Describe(LauncherAction.Launch)}: play     " +
            $"hold {_keys.Describe(LauncherAction.Exit)}: quit     " +
            $"{_keys.Describe(LauncherAction.Settings)}: settings";

        var errors = new List<string>();
        if (settingsError != null) errors.Add(settingsError);
        if (scanError != null) errors.Add(scanError);
        else if (_tables.Count == 0) errors.Add($"No .vpx tables found in {_settings.TablesFolder}");
        if (_keys.InvalidNames.Count > 0) errors.Add("Unknown key names in settings.json: " + string.Join(", ", _keys.InvalidNames));
        if (errors.Count > 0) ShowStatus(string.Join("\n", errors));
        else HideStatus();

        _index = Math.Max(0, _tables.FindIndex(t => t.FilePath.Equals(_state.LastTable, StringComparison.OrdinalIgnoreCase)));
        UpdateView();
    }

    void OpenSettings()
    {
        if (_running || OwnedWindows.Count > 0) return;
        CancelExitHold();
        _refocusTimer.Stop(); // it would pull focus away from the dialog
        var dialog = new SettingsWindow(_settings.Clone()) { Owner = this };
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
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20),
            };
            var border = new Border
            {
                Background = SlotBackground,
                CornerRadius = new CornerRadius(16),
                BorderBrush = SelectedBorder,
                BorderThickness = new Thickness(distance == 0 ? 8 : 0),
                Margin = distance switch { 0 => new Thickness(16, 10, 16, 10), 1 => new Thickness(12, 90, 12, 90), _ => new Thickness(10, 160, 10, 160) },
                Opacity = distance switch { 0 => 1.0, 1 => 0.5, _ => 0.25 },
                Child = new Grid { Children = { placeholder, image } },
            };
            Grid.SetColumn(border, offset + 2);
            Carousel.Children.Add(border);
            _slots.Add(new Slot(offset, border, image, placeholder));
        }
    }

    void UpdateView()
    {
        if (_tables.Count == 0)
        {
            foreach (var slot in _slots) slot.Border.Visibility = Visibility.Hidden;
            TitleText.Text = "No tables";
            DetailsText.Text = _settings.TablesFolder;
            CounterText.Text = "";
            LetterText.Text = "";
            return;
        }

        var current = _tables[_index];
        TitleText.Text = current.Title;
        DetailsText.Text = current.Details;
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
    }

    TableEntry TableAt(int offset) => _tables[Mod(_index + offset, _tables.Count)];

    void RequestImage(TableEntry table)
    {
        if (table.ImageRequested) return;
        table.ImageRequested = true;
        Task.Run(() => _thumbnails.Load(table)).ContinueWith(task =>
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

    // ---------- Input ----------

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        LastKeyText.Text = $"Last key: {key}";

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
            case LauncherAction.Launch when !e.IsRepeat: Launch(); break;
            case LauncherAction.Exit when !e.IsRepeat: StartExitHold(key); break;
            case LauncherAction.Settings when !e.IsRepeat: OpenSettings(); break;
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
            ShowStatus($"VPX not found: {_settings.VpxExePath}\nSet VpxExePath in settings.json");
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
        HideStatus();
        PlayingText.Text = table.Title;
        PlayingPanel.Visibility = Visibility.Visible;
        if (_settings.MinimizeWhileRunning)
            WindowState = WindowState.Minimized;

        using (process)
            await process.WaitForExitAsync();

        _running = false;
        PlayingPanel.Visibility = Visibility.Collapsed;
        _ignoreInputUntil = DateTime.UtcNow.AddMilliseconds(_settings.InputCooldownMs);
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

    static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
