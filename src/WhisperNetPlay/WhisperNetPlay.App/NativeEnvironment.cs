using System.Runtime.InteropServices;

namespace WhisperNetPlay.App;

/// <summary>
/// Prepares the process environment for the Whisper.net native runtime, before any native library
/// is loaded.
/// </summary>
internal static class NativeEnvironment
{
    public static void Configure()
    {
        // Avoids a whisper.cpp/ggml crash when both the CPU and CUDA ggml backends install their
        // terminate handler.
        SetEnvironmentVariable("GGML_NO_BACKTRACE", "1");

        try
        {
            var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            if (string.IsNullOrWhiteSpace(cudaPath))
                return;

            var cudaBin = Path.Combine(cudaPath, "bin");
            if (!Directory.Exists(cudaBin))
                return;

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var alreadyPresent = path.Split(Path.PathSeparator).Any(
                entry => string.Equals(entry.Trim().TrimEnd('\\'), cudaBin.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase));
            if (!alreadyPresent)
                Environment.SetEnvironmentVariable("PATH", cudaBin + Path.PathSeparator + path);
        }
        catch
        {
            // Best-effort: GPU acceleration simply won't engage if the CUDA path can't be resolved.
        }
    }

    private static void SetEnvironmentVariable(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            _ = PutEnv(name, value);
        }
        catch
        {
            // ucrtbase not present / not the active CRT — the managed set above is the fallback.
        }
    }

    [DllImport("ucrtbase.dll", EntryPoint = "_putenv_s", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int PutEnv(string name, string value);
}
