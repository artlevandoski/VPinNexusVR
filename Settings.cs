using System.Text.Json;

namespace VRPLauncher;

/// <summary>Key names are WPF <see cref="System.Windows.Input.Key"/> names, e.g. "LeftShift", "D1", "Return".</summary>
public sealed class KeyBindings
{
    public List<string> Previous { get; set; } = ["LeftShift", "Left"];
    public List<string> Next { get; set; } = ["RightShift", "Right"];
    public List<string> PreviousLetter { get; set; } = ["LeftCtrl", "Up", "PageUp"];
    public List<string> NextLetter { get; set; } = ["RightCtrl", "Down", "PageDown"];
    public List<string> Launch { get; set; } = ["D1", "Return"];
    public List<string> Exit { get; set; } = ["Escape"];
    public List<string> Settings { get; set; } = ["F2"];
    public List<string> View { get; set; } = ["D5", "V"];
    public List<string> NextFilter { get; set; } = ["D2", "F"];
    public List<string> PreviousFilter { get; set; } = ["G"];
    public List<string> ToggleMusic { get; set; } = ["M"];

    /// <summary>The key names bound to an action.</summary>
    public List<string> For(LauncherAction action) => action switch
    {
        LauncherAction.Previous => Previous,
        LauncherAction.Next => Next,
        LauncherAction.PreviousLetter => PreviousLetter,
        LauncherAction.NextLetter => NextLetter,
        LauncherAction.Launch => Launch,
        LauncherAction.Exit => Exit,
        LauncherAction.Settings => Settings,
        LauncherAction.View => View,
        LauncherAction.NextFilter => NextFilter,
        LauncherAction.PreviousFilter => PreviousFilter,
        LauncherAction.ToggleMusic => ToggleMusic,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public void Set(LauncherAction action, List<string> keys)
    {
        switch (action)
        {
            case LauncherAction.Previous: Previous = keys; break;
            case LauncherAction.Next: Next = keys; break;
            case LauncherAction.PreviousLetter: PreviousLetter = keys; break;
            case LauncherAction.NextLetter: NextLetter = keys; break;
            case LauncherAction.Launch: Launch = keys; break;
            case LauncherAction.Exit: Exit = keys; break;
            case LauncherAction.Settings: Settings = keys; break;
            case LauncherAction.View: View = keys; break;
            case LauncherAction.NextFilter: NextFilter = keys; break;
            case LauncherAction.PreviousFilter: PreviousFilter = keys; break;
            case LauncherAction.ToggleMusic: ToggleMusic = keys; break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }
}

public sealed class Settings
{
    public string TablesFolder { get; set; } = @"I:\vPinball\VisualPinball\Tables";
    public bool IncludeSubfolders { get; set; } = false;

    public string VpxExePath { get; set; } = @"I:\vPinball\VisualPinball\VPinballX_GL64.exe";

    /// <summary>{table} is replaced with the full path of the selected .vpx file.</summary>
    public string LaunchArguments { get; set; } = "-Minimized -Play \"{table}\"";

    /// <summary>Folders searched (in order) for an image named like the table.</summary>
    public List<string> ImageFolders { get; set; } =
    [
        @"I:\vPinball\PinUPSystem\POPMedia\Visual Pinball X\Wheel",
        @"I:\vPinball\VisualPinball\vpxlauncher\wheels",
    ];

    /// <summary>Folders searched (in order) for a rules card named like the table.</summary>
    public List<string> RulesFolders { get; set; } = [];

    /// <summary>Use the backglass image inside a matching .directb2s file when one exists.</summary>
    public bool UseDirectB2SImages { get; set; } = true;

    /// <summary>Quiet background music (Media\Music, or the built-in track).</summary>
    public bool MusicEnabled { get; set; } = true;

    /// <summary>Music volume, 0 to 1.</summary>
    public double MusicVolume { get; set; } = 0.15;

    /// <summary>Use the Virtual Pinball Spreadsheet database for game info, images, filters and update checks.</summary>
    public bool UseVps { get; set; } = true;

    public bool Fullscreen { get; set; } = true;
    public bool MinimizeWhileRunning { get; set; } = true;

    /// <summary>Input is ignored this long after VPX exits, so the key that quit VPX doesn't act here.</summary>
    public int InputCooldownMs { get; set; } = 1500;

    /// <summary>How long an Exit key must be held to quit the launcher. 0 quits on press.</summary>
    public int ExitHoldMs { get; set; } = 1500;

    public KeyBindings Keys { get; set; } = new();

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>Writes the settings file. Any comments in the old file are not kept.</summary>
    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Loads the settings file, writing a default one first if it doesn't exist.</summary>
    public static Settings LoadOrCreate(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            var defaults = new Settings();
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOptions));
            }
            catch (Exception ex)
            {
                error = $"Could not write {path}: {ex.Message}";
            }
            return defaults;
        }

        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? new Settings();
        }
        catch (Exception ex)
        {
            error = $"settings.json is invalid, using defaults: {ex.Message}";
            return new Settings();
        }
    }
}

/// <summary>Small runtime state kept apart from settings.json so the user's file is never rewritten.</summary>
public sealed class LauncherState
{
    public string? LastTable { get; set; }

    public static LauncherState Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(path)) ?? new LauncherState();
        }
        catch
        {
            // A broken state file only loses the remembered selection.
        }
        return new LauncherState();
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Not worth interrupting play over.
        }
    }
}
