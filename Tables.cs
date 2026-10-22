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

    /// <summary>Title without "(Maker Year)", e.g. "AC-DC (LUCI Premium)".</summary>
    public required string Name { get; init; }
    public string? Manufacturer { get; init; }
    public int? Year { get; init; }

    /// <summary>Virtual Pinball Spreadsheet data, once the database has loaded and the table matched.</summary>
    public VpsMatch? Vps { get; set; }

    public string? Type => Vps?.Game.Type;
    public string? DisplayManufacturer => Vps?.Game.Manufacturer ?? Manufacturer;
    public int? DisplayYear => Vps?.Game.Year ?? Year;

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

    // "AC-DC (LUCI Premium) (Stern 2013)" -> "AC-DC (LUCI Premium)", "Stern", "2013"
    [GeneratedRegex(@"^(.*)\(([^()]*?)\s*(\d{4})\)$")]
    private static partial Regex MakerYearRegex();

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
        var title = match.Success ? match.Groups[1].Value.Trim() : stem;
        var makerYear = MakerYearRegex().Match(title);
        return new TableEntry
        {
            FilePath = path,
            FileStem = stem,
            Title = title,
            Details = match.Success ? match.Groups[2].Value.Trim() : "",
            Name = makerYear.Success ? makerYear.Groups[1].Value.Trim() : title,
            Manufacturer = makerYear.Success && makerYear.Groups[2].Value.Trim() is { Length: > 0 } maker ? maker : null,
            Year = makerYear.Success ? int.Parse(makerYear.Groups[3].Value) : null,
        };
    }
}

/// <summary>
/// Finds a picture for a table, in this order: an image beside the .vpx with the same name,
/// the configured wheel/image folders (exact name first, then any image whose name starts with
/// the table's "Name (Maker Year)"), the backglass inside a same-named .directb2s, then the VPS backglass.
/// </summary>
public sealed class ThumbnailFinder
{
    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];
    const int DecodeHeight = 600;

    readonly bool _useDirectB2S;
    readonly VpsClient? _vps;

    // Normalized file stem -> image path, across all image folders, first folder wins.
    readonly Dictionary<string, string> _byStem = new();
    readonly List<KeyValuePair<string, string>> _sortedStems;

    public ThumbnailFinder(Settings settings, VpsClient? vps)
    {
        _useDirectB2S = settings.UseDirectB2SImages;
        _vps = vps;
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

            if (_byStem.TryGetValue(Normalize(table.FileStem), out var exact))
                return FromFile(exact);

            var titleKey = Normalize(table.Title);
            var prefixed = _sortedStems.FirstOrDefault(kv => kv.Key.StartsWith(titleKey, StringComparison.Ordinal));
            if (prefixed.Value != null)
                return FromFile(prefixed.Value);

            return LoadBackglass(table, DecodeHeight);
        }
        catch
        {
            // A broken image just leaves the text placeholder.
        }
        return null;
    }

    /// <summary>The table's backglass: from a same-named .directb2s if there is one, otherwise from VPS.</summary>
    public ImageSource? LoadBackglass(TableEntry table, int decodeHeight)
    {
        try
        {
            if (_useDirectB2S)
            {
                var b2s = Path.Combine(Path.GetDirectoryName(table.FilePath)!, table.FileStem + ".directb2s");
                if (File.Exists(b2s) && FromDirectB2S(b2s, decodeHeight) is { } backglass)
                    return backglass;
            }
            return LoadVpsImage(table.Vps?.BackglassUrl, decodeHeight);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Downloads (once) and loads a VPS image. Blocking; call off the UI thread.</summary>
    public ImageSource? LoadVpsImage(string? url, int decodeHeight)
    {
        try
        {
            return _vps?.GetImage(url) is { } path ? FromFile(path, decodeHeight) : null;
        }
        catch
        {
            return null;
        }
    }

    static string Normalize(string name) =>
        string.Join(' ', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    static BitmapSource FromFile(string path, int decodeHeight = DecodeHeight)
    {
        using var stream = File.OpenRead(path);
        return FromStream(stream, decodeHeight);
    }

    static BitmapSource FromStream(Stream stream, int decodeHeight = DecodeHeight)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelHeight = decodeHeight;
        bmp.StreamSource = stream;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Reads the base64 BackglassImage (or ThumbnailImage as a fallback) from a .directb2s XML file.</summary>
    static BitmapSource? FromDirectB2S(string path, int decodeHeight)
    {
        string? thumbnail = null;
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            if (reader.Name == "BackglassImage" && reader.GetAttribute("Value") is { Length: > 0 } backglass)
                return FromBase64(backglass, decodeHeight);
            if (reader.Name == "ThumbnailImage" && thumbnail == null)
                thumbnail = reader.GetAttribute("Value");
        }
        return string.IsNullOrEmpty(thumbnail) ? null : FromBase64(thumbnail, decodeHeight);
    }

    static BitmapSource FromBase64(string data, int decodeHeight)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(data));
        return FromStream(stream, decodeHeight);
    }
}
