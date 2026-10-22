using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VRPLauncher;

// Models for https://virtualpinballspreadsheet.github.io/vps-db/db/vpsdb.json (only the fields we use).

public sealed class VpsGame
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public int? Year { get; set; }
    public string? Type { get; set; }
    public int? Players { get; set; }
    public List<string>? Theme { get; set; }
    public List<string>? Designers { get; set; }
    public string? ImgUrl { get; set; }
    public List<VpsFile>? TableFiles { get; set; }
    public List<VpsFile>? B2sFiles { get; set; }
}

public sealed class VpsFile
{
    public string? Version { get; set; }
    public List<string>? Authors { get; set; }
    public List<string>? Features { get; set; }
    public string? ImgUrl { get; set; }
    public string? TableFormat { get; set; }
}

/// <summary>What VPS knows about one local table.</summary>
public sealed record VpsMatch(VpsGame Game, VpsFile? File, string? UpdateVersion, string? PlayfieldUrl, string? BackglassUrl);

/// <summary>Downloads vpsdb.json when lastUpdated.json changes, and caches it and its images on disk.</summary>
public sealed class VpsClient
{
    const string BaseUrl = "https://virtualpinballspreadsheet.github.io/vps-db/";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    readonly string _dbPath;
    readonly string _stampPath;
    readonly string _imageDir;

    public VpsClient(string cacheDir)
    {
        _dbPath = Path.Combine(cacheDir, "vpsdb.json");
        _stampPath = Path.Combine(cacheDir, "lastUpdated.json");
        _imageDir = Path.Combine(cacheDir, "img");
    }

    public List<VpsGame>? LoadCached()
    {
        try
        {
            using var stream = File.OpenRead(_dbPath);
            return JsonSerializer.Deserialize<List<VpsGame>>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the new database if VPS has a newer one than the cache, otherwise null. Throws on network errors.</summary>
    public async Task<List<VpsGame>?> RefreshAsync()
    {
        var stamp = (await Http.GetStringAsync(BaseUrl + "lastUpdated.json")).Trim();
        if (File.Exists(_dbPath) && File.Exists(_stampPath) && File.ReadAllText(_stampPath).Trim() == stamp)
            return null;

        var json = await Http.GetByteArrayAsync(BaseUrl + "db/vpsdb.json");
        var games = JsonSerializer.Deserialize<List<VpsGame>>(json, JsonOptions);
        if (games == null || games.Count == 0)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        await File.WriteAllBytesAsync(_dbPath, json);
        await File.WriteAllTextAsync(_stampPath, stamp);
        return games;
    }

    /// <summary>Local copy of a VPS image, downloading it the first time. Blocking; call off the UI thread.</summary>
    public string? GetImage(string? url)
    {
        if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;

        var path = Path.Combine(_imageDir, Path.GetFileName(uri.AbsolutePath));
        if (File.Exists(path))
            return path;

        try
        {
            var bytes = Http.GetByteArrayAsync(uri).GetAwaiter().GetResult();
            Directory.CreateDirectory(_imageDir);
            var temp = path + ".part";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Matches local table file names ("Name (Maker Year) author version ...") to VPS games and releases.</summary>
public sealed partial class VpsIndex
{
    readonly List<VpsGame> _games;
    readonly Dictionary<string, List<VpsGame>> _byName = new();

    public VpsIndex(List<VpsGame> games)
    {
        _games = games;
        foreach (var game in games)
        {
            var key = Normalize(game.Name);
            if (!_byName.TryGetValue(key, out var list))
                _byName[key] = list = [];
            list.Add(game);
        }
    }

    public VpsMatch? Match(TableEntry table)
    {
        var game = FindGame(table);
        if (game == null)
            return null;

        var vpxFiles = (game.TableFiles ?? []).Where(f => f.TableFormat is null or "VPX").ToList();

        // Releases whose main author appears in the file name are taken to be the same table line as ours.
        var sameAuthor = vpxFiles
            .Where(f => f.Authors is { Count: > 0 } a && a[0].Length > 2
                        && table.Details.Contains(a[0], StringComparison.OrdinalIgnoreCase))
            .ToList();

        var localVersion = ParseVersion(LocalVersionRegex().Match(table.Details) is { Success: true } m ? m.Groups[1].Value : null);
        var file = sameAuthor.FirstOrDefault(f => localVersion != null && CompareVersions(ParseVersion(f.Version), localVersion) == 0)
                   ?? sameAuthor.MaxBy(f => ParseVersion(f.Version), VersionComparer.Instance);

        string? update = null;
        if (localVersion != null)
        {
            var newest = sameAuthor.MaxBy(f => ParseVersion(f.Version), VersionComparer.Instance);
            if (newest != null && CompareVersions(ParseVersion(newest.Version), localVersion) > 0)
                update = newest.Version;
        }

        var playfield = file?.ImgUrl ?? vpxFiles.FirstOrDefault(f => f.ImgUrl != null)?.ImgUrl;
        var backglass = game.B2sFiles?.FirstOrDefault(f => f.ImgUrl != null)?.ImgUrl ?? game.ImgUrl;
        return new VpsMatch(game, file, update, playfield, backglass);
    }

    VpsGame? FindGame(TableEntry table)
    {
        var name = Normalize(table.Name);
        if (name.Length == 0)
            return null;
        var maker = Normalize(table.Manufacturer ?? "");

        if (_byName.TryGetValue(name, out var exact))
        {
            var candidates = table.Year is { } year ? exact.Where(g => g.Year == year).ToList() : exact;
            if (candidates.Count > 0)
                return candidates.OrderByDescending(g => MakerMatches(g, maker)).First();
        }

        // Loose match ("Back To The Future - The Pinball" vs "Back to the Future"): same year, one name contains the other.
        if (table.Year == null || name.Length < 4)
            return null;
        return _games
            .Where(g => g.Year == table.Year)
            .Select(g => (Game: g, Key: Normalize(g.Name)))
            .Where(x => x.Key.Length >= 4 && (x.Key.Contains(name) || name.Contains(x.Key)))
            .OrderByDescending(x => MakerMatches(x.Game, maker))
            .ThenBy(x => Math.Abs(x.Key.Length - name.Length))
            .Select(x => x.Game)
            .FirstOrDefault();
    }

    static bool MakerMatches(VpsGame game, string maker) =>
        maker.Length > 0 && Normalize(game.Manufacturer) is var m && (m.Contains(maker) || maker.Contains(m));

    static string Normalize(string text) =>
        NonAlphanumericRegex().Replace(text.ToLowerInvariant().Replace("&", "and"), "");

    /// <summary>Leading numbers of a version string: "2.4.42_bw" -> [2,4,42], "v1.0" -> [1,0].</summary>
    static int[]? ParseVersion(string? version)
    {
        if (version == null) return null;
        var match = VersionNumbersRegex().Match(version);
        if (!match.Success) return null;
        return match.Groups[1].Value.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
    }

    /// <summary>Numeric comparison where missing parts count as 0, so 1.0 == 1.0.0.</summary>
    static int CompareVersions(int[]? a, int[]? b)
    {
        if (a == null || b == null) return a == null ? (b == null ? 0 : -1) : 1;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var diff = (i < a.Length ? a[i] : 0).CompareTo(i < b.Length ? b[i] : 0);
            if (diff != 0) return diff;
        }
        return 0;
    }

    sealed class VersionComparer : IComparer<int[]?>
    {
        public static readonly VersionComparer Instance = new();
        public int Compare(int[]? x, int[]? y) => CompareVersions(x, y);
    }

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex(@"^\D*?(\d+(?:\.\d+)*)")]
    private static partial Regex VersionNumbersRegex();

    // A standalone version in the file name: "1.1.0", "v3.2", "v.2.3", "V10" but not the "02" in "Marty02".
    [GeneratedRegex(@"(?<![A-Za-z0-9.])[vV]?\.?(\d+(?:\.\d+)*)(?![A-Za-z0-9])")]
    private static partial Regex LocalVersionRegex();
}
