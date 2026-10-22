using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace VRPLauncher;

/// <summary>Edits the paths in settings.json. Key bindings and timings stay file-only.</summary>
public partial class SettingsWindow : Window
{
    static readonly Brush HintBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
    static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

    readonly ObservableCollection<string> _imageFolders;
    readonly ObservableCollection<string> _rulesFolders;
    readonly string _wheelMediaDir;
    readonly string _playfieldMediaDir;
    readonly string _rulesMediaDir;

    public Settings Result { get; }

    public SettingsWindow(Settings settings, string wheelMediaDir, string playfieldMediaDir, string rulesMediaDir)
    {
        InitializeComponent();
        Result = settings;
        _wheelMediaDir = wheelMediaDir;
        _playfieldMediaDir = playfieldMediaDir;
        _rulesMediaDir = rulesMediaDir;
        MaxHeight = SystemParameters.WorkArea.Height;

        VpxExeBox.Text = settings.VpxExePath;
        ArgumentsBox.Text = settings.LaunchArguments;
        TablesBox.Text = settings.TablesFolder;
        SubfoldersBox.IsChecked = settings.IncludeSubfolders;
        WheelMediaText.Text = wheelMediaDir;
        PlayfieldMediaText.Text = playfieldMediaDir;
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

        UpdateStatus();
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
        ShowFolderStatus(PlayfieldStatus, [_playfieldMediaDir]);
        PlayfieldStatus.Text += " Used before the VPS playfield. Name them like the wheel images.";
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

    void OpenPlayfieldMedia_Click(object sender, RoutedEventArgs e) => OpenInExplorer(_playfieldMediaDir);

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
        Result.VpxExePath = VpxExeBox.Text.Trim();
        Result.LaunchArguments = ArgumentsBox.Text.Trim();
        Result.TablesFolder = TablesBox.Text.Trim();
        Result.IncludeSubfolders = SubfoldersBox.IsChecked == true;
        Result.ImageFolders = [.. _imageFolders];
        Result.RulesFolders = [.. _rulesFolders];
        Result.UseDirectB2SImages = DirectB2SBox.IsChecked == true;
        Result.Fullscreen = FullscreenBox.IsChecked == true;
        Result.UseVps = VpsBox.IsChecked == true;
        DialogResult = true;
    }
}
