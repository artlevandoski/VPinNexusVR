using System.Windows.Input;

namespace VRPLauncher;

public enum LauncherAction { Previous, Next, PreviousLetter, NextLetter, Launch, Exit, Settings }

public sealed class KeyMap
{
    readonly Dictionary<Key, LauncherAction> _map = new();
    readonly Dictionary<LauncherAction, List<Key>> _keys = new();

    public List<string> InvalidNames { get; } = [];

    public KeyMap(KeyBindings bindings)
    {
        Add(LauncherAction.Previous, bindings.Previous);
        Add(LauncherAction.Next, bindings.Next);
        Add(LauncherAction.PreviousLetter, bindings.PreviousLetter);
        Add(LauncherAction.NextLetter, bindings.NextLetter);
        Add(LauncherAction.Launch, bindings.Launch);
        Add(LauncherAction.Exit, bindings.Exit);
        Add(LauncherAction.Settings, bindings.Settings);
    }

    public bool TryGet(Key key, out LauncherAction action) => _map.TryGetValue(key, out action);

    /// <summary>Readable name of the first key bound to an action, for the on-screen help.</summary>
    public string Describe(LauncherAction action) =>
        _keys.TryGetValue(action, out var keys) && keys.Count > 0 ? Describe(keys[0]) : "(unbound)";

    public static string Describe(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.LeftShift => "Left Shift",
        Key.RightShift => "Right Shift",
        Key.LeftCtrl => "Left Ctrl",
        Key.RightCtrl => "Right Ctrl",
        _ => key.ToString(),
    };

    void Add(LauncherAction action, IEnumerable<string> names)
    {
        var list = new List<Key>();
        foreach (var name in names)
        {
            if (!TryParse(name, out var key))
            {
                InvalidNames.Add(name);
                continue;
            }
            _map[key] = action;
            list.Add(key);
        }
        _keys[action] = list;
    }

    static bool TryParse(string name, out Key key)
    {
        // Allow "1" as shorthand for D1.
        if (name.Length == 1 && char.IsDigit(name[0]))
            name = "D" + name;
        return Enum.TryParse(name, ignoreCase: true, out key) && key != Key.None;
    }
}
