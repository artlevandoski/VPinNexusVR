using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VRPLauncher;

/// <summary>
/// Spots the error box OpenComposite shows when VR can't start (for example the headset isn't connected),
/// which would otherwise leave VPX waiting for a mouse click nobody can make from inside the headset.
/// </summary>
static class VrErrorWatcher
{
    const uint WM_CLOSE = 0x0010;

    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// If the process is showing an OpenComposite error box, closes it (which makes VPX quit) and
    /// returns a message for the user. Otherwise returns null.
    /// </summary>
    public static string? CloseVrError(Process process)
    {
        var dialog = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == process.Id && IsWindowVisible(hWnd) && ClassOf(hWnd) == "#32770"
                && TextOf(hWnd).StartsWith("OpenComposite", StringComparison.OrdinalIgnoreCase))
            {
                dialog = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        if (dialog == IntPtr.Zero)
            return null;

        var details = new StringBuilder();
        EnumChildWindows(dialog, (child, _) =>
        {
            if (ClassOf(child) == "Static")
                details.AppendLine(TextOf(child));
            return true;
        }, IntPtr.Zero);

        PostMessage(dialog, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        return Describe(details.ToString());
    }

    static string Describe(string details)
    {
        if (details.Contains("XR_ERROR_FORM_FACTOR_UNAVAILABLE"))
            return "VR headset not found.\nPut the headset on, check Virtual Desktop is connected, then try again.";

        // Any other OpenComposite failure: show its error code line if there is one.
        var code = details.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Error code", StringComparison.OrdinalIgnoreCase));
        return "VR could not start." + (code != null ? $"\n{code}" : "") + "\nSee the OpenComposite log for details.";
    }

    static string ClassOf(IntPtr hWnd)
    {
        var name = new StringBuilder(64);
        GetClassName(hWnd, name, name.Capacity);
        return name.ToString();
    }

    static string TextOf(IntPtr hWnd)
    {
        var text = new StringBuilder(1024);
        GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }
}
