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
/// Images in a list of folders, looked up by table: exact file name first, then any image whose
/// name starts with the table's "Name (Maker Year)". Earlier folders win.
/// </summary>
public sealed class ImageIndex
{
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp"];

    /// <summary>File name (without extension) of the picture shown when a table has none of its own.</summary>
    public const string DefaultName = "_default";

    // Normalized file stem -> image path.
    readonly Dictionary<string, string> _byStem = new();
    readonly List<KeyValuePair<string, string>> _sortedStems;

    public ImageIndex(IEnumerable<string> folders)
    {
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;
                _byStem.TryAdd(Normalize(Path.GetFileNameWithoutExtension(file)), file);
            }
        }
        _sortedStems = _byStem.OrderBy(kv => kv.Key.Length).ToList();
    }

    public string? Find(TableEntry table)
    {
        if (_byStem.TryGetValue(Normalize(table.FileStem), out var exact))
            return exact;
        var titleKey = Normalize(table.Title);
        return _sortedStems.FirstOrDefault(kv => kv.Key.StartsWith(titleKey, StringComparison.Ordinal)).Value;
    }

    /// <summary>The "_default" image from the first folder that has one, or null.</summary>
    public string? FindDefault() => _byStem.GetValueOrDefault(DefaultName);

    static string Normalize(string name) =>
        string.Join(' ', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// Finds a picture for a table, in this order: an image beside the .vpx with the same name,
/// the wheel folders, the backglass inside a same-named .directb2s, then the VPS backglass.
/// Also loads playfields (local folder first, then VPS) and rules cards.
/// </summary>
public sealed class ThumbnailFinder
{
    const int DecodeHeight = 600;

    readonly bool _useDirectB2S;
    readonly VpsClient? _vps;
    readonly ImageIndex _wheels;
    readonly ImageIndex _playfields;
    readonly ImageIndex _rules;

    public ThumbnailFinder(Settings settings, VpsClient? vps,
        IEnumerable<string> wheelFolders, IEnumerable<string> playfieldFolders, IEnumerable<string> rulesFolders)
    {
        _useDirectB2S = settings.UseDirectB2SImages;
        _vps = vps;
        _wheels = new ImageIndex(wheelFolders);
        _playfields = new ImageIndex(playfieldFolders);
        _rules = new ImageIndex(rulesFolders);
    }

    /// <summary>Loads a frozen image for the table, or null. Safe to call off the UI thread.</summary>
    public ImageSource? Load(TableEntry table)
    {
        try
        {
            var dir = Path.GetDirectoryName(table.FilePath)!;
            foreach (var ext in ImageIndex.Extensions)
            {
                var sidecar = Path.Combine(dir, table.FileStem + ext);
                if (File.Exists(sidecar))
                    return FromFile(sidecar);
            }

            if (_wheels.Find(table) is { } wheel)
                return FromFile(wheel);

            return LoadBackglass(table, DecodeHeight);
        }
        catch
        {
            // A broken image just leaves the text placeholder.
        }
        return null;
    }

    /// <summary>The table's playfield: from the playfield folders if there is one, otherwise from VPS.</summary>
    public ImageSource? LoadPlayfield(TableEntry table, int decodeHeight)
    {
        try
        {
            if (_playfields.Find(table) is { } path)
                return FromFile(path, decodeHeight);
            return LoadVpsImage(table.Vps?.PlayfieldUrl, decodeHeight);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The table's rules card from the rules folders, else a "_default" image in those folders,
    /// else the default built into the exe.
    /// </summary>
    public ImageSource? LoadRules(TableEntry table, int decodeHeight)
    {
        try
        {
            return (_rules.Find(table) ?? _rules.FindDefault()) is { } path
                ? FromFile(path, decodeHeight)
                : FromBuiltIn("rules_default.png", decodeHeight);
        }
        catch
        {
            return null;
        }
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

    static BitmapSource? FromBuiltIn(string name, int decodeHeight)
    {
        using var stream = typeof(ThumbnailFinder).Assembly.GetManifestResourceStream("Defaults." + name);
        return stream == null ? null : FromStream(stream, decodeHeight);
    }

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
