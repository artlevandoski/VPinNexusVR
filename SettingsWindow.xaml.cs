using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace VRPLauncher;

/// <summary>Edits settings.json: paths, display options and key bindings. Timings stay file-only.</summary>
public partial class SettingsWindow : Window
{
    static readonly Brush HintBrush = new SolidColorBrush(Color.FromRgb(0x91, 0xA2, 0xB8)); // Chrome Midtone
    static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

    readonly ObservableCollection<string> _imageFolders;
    readonly ObservableCollection<string> _rulesFolders;
    readonly string _wheelMediaDir;
    readonly string _rulesMediaDir;

    // Controls: the actions in the order shown, with their label and (if any) the cabinet button they are meant for.
    static readonly (LauncherAction Action, string Label)[] ControlRows =
    [
        (LauncherAction.Previous, "Previous table  (Left Flipper)"),
        (LauncherAction.Next, "Next table  (Right Flipper)"),
        (LauncherAction.PreviousLetter, "Previous letter  (Left Magna Save)"),
        (LauncherAction.NextLetter, "Next letter  (Right Magna Save)"),
        (LauncherAction.Launch, "Play  (Start)"),
        (LauncherAction.View, "Rules card  (Coin)"),
        (LauncherAction.NextFilter, "Next filter"),
        (LauncherAction.PreviousFilter, "Previous filter"),
        (LauncherAction.ToggleMusic, "Music on/off"),
        (LauncherAction.Exit, "Quit launcher  (hold)"),
        (LauncherAction.Settings, "Settings"),
    ];

    KeyBindings _keys;
    readonly Dictionary<LauncherAction, (TextBlock Keys, Button Add)> _controlRows = new();
    LauncherAction? _capturing;

    public Settings Result { get; }

    public SettingsWindow(Settings settings, string wheelMediaDir, string rulesMediaDir)
    {
        InitializeComponent();
        Result = settings;
        _wheelMediaDir = wheelMediaDir;
        _rulesMediaDir = rulesMediaDir;
        MaxHeight = SystemParameters.WorkArea.Height;

        VpxExeBox.Text = settings.VpxExePath;
        ArgumentsBox.Text = settings.LaunchArguments;
        TablesBox.Text = settings.TablesFolder;
        SubfoldersBox.IsChecked = settings.IncludeSubfolders;
        WheelMediaText.Text = wheelMediaDir;
        RulesMediaText.Text = rulesMediaDir;
        _imageFolders = new ObservableCollection<string>(settings.ImageFolders);
        ImageFoldersList.ItemsSource = _imageFolders;
        _imageFolders.CollectionChanged += (_, _) => UpdateStatus();
        _rulesFolders = new ObservableCollection<string>(settings.RulesFolders);
        RulesFoldersList.ItemsSource = _rulesFolders;
        _rulesFolders.CollectionChanged += (_, _) => UpdateStatus();
        DirectB2SBox.IsChecked = settings.UseDirectB2SImages;
        FullscreenBox.IsChecked = settings.Fullscreen;
        VpsBox.IsChecked = settings.UseVps;
        MusicBox.IsChecked = settings.MusicEnabled;
        MusicVolumeSlider.Value = Math.Round(settings.MusicVolume * 100);

        _keys = settings.Keys;
        BuildControlRows();
        PreviewKeyDown += CaptureKey;

        UpdateStatus();
    }

    void MusicVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MusicVolumeText != null)
            MusicVolumeText.Text = $"{e.NewValue:0}%";
    }

    // ---------- Controls ----------

    void BuildControlRows()
    {
        foreach (var (action, label) in ControlRows)
        {
            var add = new Button { Content = "Add key…", Tag = action };
            add.Click += AddKey_Click;
            var clear = new Button { Content = "Clear", Tag = action };
            clear.Click += ClearKeys_Click;
            var keys = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("NexusCyan"), TextTrimming = TextTrimming.CharacterEllipsis };
            var name = new TextBlock { Text = label, Width = 330, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("SilverWhite") };

            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            DockPanel.SetDock(name, Dock.Left);
            DockPanel.SetDock(clear, Dock.Right);
            DockPanel.SetDock(add, Dock.Right);
            row.Children.Add(name);
            row.Children.Add(clear);
            row.Children.Add(add);
            row.Children.Add(keys);
            ControlsPanel.Children.Add(row);
            _controlRows[action] = (keys, add);
        }
        ShowKeys();
    }

    void ShowKeys()
    {
        ControlsStatus.Foreground = HintBrush;
        foreach (var (action, row) in _controlRows)
        {
            var names = _keys.For(action).Select(n => KeyMap.TryParse(n, out var key) ? KeyMap.Describe(key) : $"{n}?").ToList();
            row.Keys.Text = names.Count > 0 ? string.Join(",  ", names) : "(none)";
            row.Add.Content = _capturing == action ? "Press a key…" : "Add key…";
        }
    }

    void AddKey_Click(object sender, RoutedEventArgs e)
    {
        var action = (LauncherAction)((Button)sender).Tag;
        // Clicking again while waiting cancels.
        _capturing = _capturing == action ? null : action;
        ControlsStatus.Text = _capturing != null ? "Press the button or key to use…" : "";
        ShowKeys();
    }

    void ClearKeys_Click(object sender, RoutedEventArgs e)
    {
        var action = (LauncherAction)((Button)sender).Tag;
        _keys.Set(action, []);
        _capturing = null;
        ControlsStatus.Text = "";
        ShowKeys();
    }

    void ResetControls_Click(object sender, RoutedEventArgs e)
    {
        _keys = new KeyBindings();
        _capturing = null;
        ControlsStatus.Text = "Controls reset to the VPX default keys.";
        ShowKeys();
    }

    /// <summary>While waiting for a key, the next key press is bound (Esc and Enter included) instead of acting on the window.</summary>
    void CaptureKey(object sender, KeyEventArgs e)
    {
        if (_capturing is not { } action) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.None or Key.ImeProcessed or Key.DeadCharProcessed) return;
        e.Handled = true;

        // A key can only do one thing: take it off any other action first.
        var movedFrom = new List<string>();
        foreach (var (other, label) in ControlRows)
        {
            var list = _keys.For(other);
            if (list.RemoveAll(n => KeyMap.TryParse(n, out var k) && k == key) > 0 && other != action)
                movedFrom.Add(label.Split("  (")[0]);
        }
        _keys.For(action).Add(key.ToString());

        _capturing = null;
        ControlsStatus.Text = $"{KeyMap.Describe(key)} added" + (movedFrom.Count > 0 ? $" (moved from {string.Join(", ", movedFrom)})." : ".");
        ShowKeys();
    }

    void Field_Changed(object sender, RoutedEventArgs e) => UpdateStatus();

    void UpdateStatus()
    {
        if (!IsInitialized || _imageFolders == null || _rulesFolders == null) return;

        var exe = VpxExeBox.Text.Trim();
        VpxStatus.Text = File.Exists(exe) ? "Found." : "Not found. Pick the VPX exe you use for VR, e.g. VPinballX_GL64.exe.";
        VpxStatus.Foreground = File.Exists(exe) ? HintBrush : WarningBrush;

        var tables = TablesBox.Text.Trim();
        TablesStatus.Foreground = Directory.Exists(tables) ? HintBrush : WarningBrush;
        if (!Directory.Exists(tables))
            TablesStatus.Text = "Folder not found.";
        else
        {
            var option = SubfoldersBox.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            TablesStatus.Text = $"{Count(() => Directory.EnumerateFiles(tables, "*.vpx", option))} tables found.";
        }

        ShowFolderStatus(ImagesStatus, [_wheelMediaDir, .. _imageFolders]);
        ShowFolderStatus(RulesStatus, [_rulesMediaDir, .. _rulesFolders]);
    }

    static void ShowFolderStatus(TextBlock status, List<string> folders)
    {
        var missing = folders.Count(f => !Directory.Exists(f));
        var images = folders.Where(Directory.Exists).Sum(f => Count(() =>
            Directory.EnumerateFiles(f).Where(p => ImageIndex.Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))));
        status.Text = $"{images} images found" + (missing > 0 ? $", {missing} folder(s) not found." : ".");
        status.Foreground = missing > 0 ? WarningBrush : HintBrush;
    }

    static int Count(Func<IEnumerable<string>> files)
    {
        try { return files().Count(); }
        catch { return 0; }
    }

    void BrowseVpx_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the Visual Pinball X executable",
            Filter = "Visual Pinball X (*.exe)|*.exe",
        };
        var current = VpxExeBox.Text.Trim();
        if (Directory.Exists(Path.GetDirectoryName(current)))
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        if (dialog.ShowDialog(this) == true)
            VpxExeBox.Text = dialog.FileName;
    }

    void BrowseTables_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Choose the tables folder", TablesBox.Text.Trim()) is { } folder)
            TablesBox.Text = folder;
    }

    void OpenWheelMedia_Click(object sender, RoutedEventArgs e) => OpenInExplorer(_wheelMediaDir);

    void OpenRulesMedia_Click(object sender, RoutedEventArgs e) => OpenInExplorer(_rulesMediaDir);

    static void OpenInExplorer(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch
        {
            // Nothing useful to do if Explorer won't start.
        }
    }

    /// <summary>The folder list a button belongs to, from its Tag ("Wheels" or "Rules").</summary>
    (ListBox List, ObservableCollection<string> Folders) ListFor(object sender) =>
        (sender as FrameworkElement)?.Tag as string == "Rules"
            ? (RulesFoldersList, _rulesFolders)
            : (ImageFoldersList, _imageFolders);

    void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var (list, folders) = ListFor(sender);
        var start = list.SelectedItem as string ?? folders.LastOrDefault() ?? TablesBox.Text.Trim();
        var title = folders == _rulesFolders ? "Choose a folder of rules cards" : "Choose a folder of wheel images";
        if (PickFolder(title, start) is { } folder && !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(folder);
            list.SelectedItem = folder;
        }
    }

    void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        var (list, folders) = ListFor(sender);
        var i = list.SelectedIndex;
        if (i < 0) return;
        folders.RemoveAt(i);
        list.SelectedIndex = Math.Min(i, folders.Count - 1);
    }

    void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var (list, folders) = ListFor(sender);
        var i = list.SelectedIndex;
        if (i <= 0) return;
        folders.Move(i, i - 1);
        list.SelectedIndex = i - 1;
    }

    string? PickFolder(string title, string start)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (Directory.Exists(start))
            dialog.InitialDirectory = start;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        // Without these there would be no way back into settings or out of the launcher.
        var missing = new[] { (LauncherAction.Settings, "Settings"), (LauncherAction.Exit, "Quit launcher") }
            .Where(a => _keys.For(a.Item1).Count == 0).Select(a => a.Item2).ToList();
        if (missing.Count > 0)
        {
            ControlsStatus.Text = $"Give {string.Join(" and ", missing)} at least one key before saving.";
            ControlsStatus.Foreground = WarningBrush;
            Tabs.SelectedItem = ControlsTab;
            return;
        }

        Result.VpxExePath = VpxExeBox.Text.Trim();
        Result.LaunchArguments = ArgumentsBox.Text.Trim();
        Result.TablesFolder = TablesBox.Text.Trim();
        Result.IncludeSubfolders = SubfoldersBox.IsChecked == true;
        Result.ImageFolders = [.. _imageFolders];
        Result.RulesFolders = [.. _rulesFolders];
        Result.UseDirectB2SImages = DirectB2SBox.IsChecked == true;
        Result.Fullscreen = FullscreenBox.IsChecked == true;
        Result.UseVps = VpsBox.IsChecked == true;
        Result.Keys = _keys;
        Result.MusicEnabled = MusicBox.IsChecked == true;
        Result.MusicVolume = MusicVolumeSlider.Value / 100;
        DialogResult = true;
    }
}
