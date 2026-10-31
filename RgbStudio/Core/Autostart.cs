using System.Diagnostics;

namespace RgbStudio.Core;

/// <summary>
/// Starts the app at logon as administrator with no UAC prompt: a scheduled
/// task with the highest run level (a Run key would prompt every time).
/// </summary>
public static class Autostart
{
    const string TaskName = "RGB Studio";

    public static bool IsEnabled() => Schtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static void Enable(string exePath) =>
        Check(Schtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F"));

    public static void Disable()
    {
        if (IsEnabled()) Check(Schtasks($"/Delete /TN \"{TaskName}\" /F"));
    }

    static int Schtasks(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        p.WaitForExit(10_000);
        return p.ExitCode;
    }

    static void Check(int code)
    {
        if (code != 0) throw new InvalidOperationException($"schtasks terminou com código {code}.");
    }
}
