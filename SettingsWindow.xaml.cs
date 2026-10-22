using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace VRPLauncher;

/// <summary>Edits the paths in settings.json. Key bindings and timings stay file-only.</summary>
public partial class SettingsWindow : Window
{
    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];
    static readonly Brush HintBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
    static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

    readonly ObservableCollection<string> _imageFolders;

    public Settings Result { get; }

    public SettingsWindow(Settings settings)
    {
        InitializeComponent();
        Result = settings;

        VpxExeBox.Text = settings.VpxExePath;
        ArgumentsBox.Text = settings.LaunchArguments;
        TablesBox.Text = settings.TablesFolder;
        SubfoldersBox.IsChecked = settings.IncludeSubfolders;
        _imageFolders = new ObservableCollection<string>(settings.ImageFolders);
        ImageFoldersList.ItemsSource = _imageFolders;
        _imageFolders.CollectionChanged += (_, _) => UpdateStatus();
        DirectB2SBox.IsChecked = settings.UseDirectB2SImages;
        FullscreenBox.IsChecked = settings.Fullscreen;
        VpsBox.IsChecked = settings.UseVps;

        UpdateStatus();
    }

    void Field_Changed(object sender, RoutedEventArgs e) => UpdateStatus();

    void UpdateStatus()
    {
        if (!IsInitialized || _imageFolders == null) return;

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

        var missing = _imageFolders.Count(f => !Directory.Exists(f));
        var images = _imageFolders.Where(Directory.Exists).Sum(f => Count(() =>
            Directory.EnumerateFiles(f).Where(p => ImageExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))));
        ImagesStatus.Text = $"{images} images found" + (missing > 0 ? $", {missing} folder(s) not found." : ".");
        ImagesStatus.Foreground = missing > 0 ? WarningBrush : HintBrush;
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

    void AddImageFolder_Click(object sender, RoutedEventArgs e)
    {
        var start = ImageFoldersList.SelectedItem as string ?? _imageFolders.LastOrDefault() ?? TablesBox.Text.Trim();
        if (PickFolder("Choose a folder of wheel or table images", start) is { } folder
            && !_imageFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            _imageFolders.Add(folder);
            ImageFoldersList.SelectedItem = folder;
        }
    }

    void RemoveImageFolder_Click(object sender, RoutedEventArgs e)
    {
        var i = ImageFoldersList.SelectedIndex;
        if (i < 0) return;
        _imageFolders.RemoveAt(i);
        ImageFoldersList.SelectedIndex = Math.Min(i, _imageFolders.Count - 1);
    }

    void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

    void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    void MoveSelected(int delta)
    {
        var i = ImageFoldersList.SelectedIndex;
        var target = i + delta;
        if (i < 0 || target < 0 || target >= _imageFolders.Count) return;
        _imageFolders.Move(i, target);
        ImageFoldersList.SelectedIndex = target;
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
        Result.UseDirectB2SImages = DirectB2SBox.IsChecked == true;
        Result.Fullscreen = FullscreenBox.IsChecked == true;
        Result.UseVps = VpsBox.IsChecked == true;
        DialogResult = true;
    }
}
