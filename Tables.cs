using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

namespace VRPLauncher;

public sealed class TableEntry
{
    public required string FilePath { get; init; }
    public required string FileStem { get; init; }

    /// <summary>"Name (Maker Year)" part of the file name.</summary>
    public required string Title { get; init; }

    /// <summary>Whatever follows the title: author, version, "VR", etc.</summary>
    public required string Details { get; init; }

    /// <summary>Uppercase first letter of the title, or '#' for digits and symbols.</summary>
    public char Group => char.IsLetter(Title[0]) ? char.ToUpperInvariant(Title[0]) : '#';

    public ImageSource? Image { get; set; }
    public bool ImageRequested { get; set; }
}

public static partial class TableScanner
{
    // "AC-DC (LUCI Premium) (Stern 2013) VPW 1.13 VR" -> "AC-DC (LUCI Premium) (Stern 2013)" + "VPW 1.13 VR"
    [GeneratedRegex(@"^(.*?\([^()]*\d{4}\))\s*(.*)$")]
    internal static partial Regex TitleRegex();

    public static List<TableEntry> Scan(Settings settings, out string? error)
    {
        error = null;
        if (!Directory.Exists(settings.TablesFolder))
        {
            error = $"Tables folder not found: {settings.TablesFolder}";
            return [];
        }

        var option = settings.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(settings.TablesFolder, "*.vpx", option)
            .Select(CreateEntry)
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Details, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static TableEntry CreateEntry(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var match = TitleRegex().Match(stem);
        return new TableEntry
        {
            FilePath = path,
            FileStem = stem,
            Title = match.Success ? match.Groups[1].Value.Trim() : stem,
            Details = match.Success ? match.Groups[2].Value.Trim() : "",
        };
    }
}

/// <summary>
/// Finds a picture for a table, in this order: an image beside the .vpx with the same name,
/// the backglass inside a same-named .directb2s, then the configured image folders
/// (exact name first, then any image whose name starts with the table's "Name (Maker Year)").
/// </summary>
public sealed class ThumbnailFinder
{
    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];
    const int DecodeHeight = 600;

    readonly bool _useDirectB2S;

    // Normalized file stem -> image path, across all image folders, first folder wins.
    readonly Dictionary<string, string> _byStem = new();
    readonly List<KeyValuePair<string, string>> _sortedStems;

    public ThumbnailFinder(Settings settings)
    {
        _useDirectB2S = settings.UseDirectB2SImages;
        foreach (var folder in settings.ImageFolders.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (!ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;
                _byStem.TryAdd(Normalize(Path.GetFileNameWithoutExtension(file)), file);
            }
        }
        _sortedStems = _byStem.OrderBy(kv => kv.Key.Length).ToList();
    }

    /// <summary>Loads a frozen image for the table, or null. Safe to call off the UI thread.</summary>
    public ImageSource? Load(TableEntry table)
    {
        try
        {
            var dir = Path.GetDirectoryName(table.FilePath)!;
            foreach (var ext in ImageExtensions)
            {
                var sidecar = Path.Combine(dir, table.FileStem + ext);
                if (File.Exists(sidecar))
                    return FromFile(sidecar);
            }

            if (_useDirectB2S)
            {
                var b2s = Path.Combine(dir, table.FileStem + ".directb2s");
                if (File.Exists(b2s) && FromDirectB2S(b2s) is { } backglass)
                    return backglass;
            }

            if (_byStem.TryGetValue(Normalize(table.FileStem), out var exact))
                return FromFile(exact);

            var titleKey = Normalize(table.Title);
            var prefixed = _sortedStems.FirstOrDefault(kv => kv.Key.StartsWith(titleKey, StringComparison.Ordinal));
            if (prefixed.Value != null)
                return FromFile(prefixed.Value);
        }
        catch
        {
            // A broken image just leaves the text placeholder.
        }
        return null;
    }

    static string Normalize(string name) =>
        string.Join(' ', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    static BitmapSource FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return FromStream(stream);
    }

    static BitmapSource FromStream(Stream stream)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelHeight = DecodeHeight;
        bmp.StreamSource = stream;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Reads the base64 BackglassImage (or ThumbnailImage as a fallback) from a .directb2s XML file.</summary>
    static BitmapSource? FromDirectB2S(string path)
    {
        string? thumbnail = null;
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            if (reader.Name == "BackglassImage" && reader.GetAttribute("Value") is { Length: > 0 } backglass)
                return FromBase64(backglass);
            if (reader.Name == "ThumbnailImage" && thumbnail == null)
                thumbnail = reader.GetAttribute("Value");
        }
        return string.IsNullOrEmpty(thumbnail) ? null : FromBase64(thumbnail);
    }

    static BitmapSource FromBase64(string data)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(data));
        return FromStream(stream);
    }
}
