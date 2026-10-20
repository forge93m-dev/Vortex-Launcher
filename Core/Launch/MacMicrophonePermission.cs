using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Launch;

public static class MacMicrophonePermission
{
    [DllImport("libVortexPermissions.dylib", EntryPoint = "vortex_request_microphone")]
    private static extern int Request();
    internal static bool HasVoiceChat(string instance) => Directory.Exists(Path.Combine(instance,"mods")) &&
        Directory.EnumerateFiles(Path.Combine(instance,"mods"),"*.jar").Any(f => Path.GetFileName(f).Contains("voicechat",StringComparison.OrdinalIgnoreCase));
    public static async Task PrepareAsync(string instance, Action<string>? log, CancellationToken token)
    {
        if (!OperatingSystem.IsMacOS() || !HasVoiceChat(instance)) return;
        log?.Invoke("[MICROPHONE] Checking macOS permission for voice chat. Allow Vortex Launcher in the system dialog if you want to speak.");
        try
        {
            int result = await Task.Run(Request).WaitAsync(token);
            log?.Invoke(result == 0 ? "[MICROPHONE] macOS permission granted."
                : "[MICROPHONE] Permission was not granted. Enable Vortex Launcher in System Settings > Privacy & Security > Microphone, then restart Minecraft. Game launch will continue without microphone access.");
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { log?.Invoke("[MICROPHONE] Permission helper unavailable. Install the complete Vortex macOS application package; do not copy its executable out of the .app."); }
    }
}
