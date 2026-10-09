using MCG.Launcher.Core;
using Microsoft.Win32;

namespace MCG.Launcher.Services;

internal static class WindowsDiscovery
{
    public static DiscoveryResult Scan()
    {
        var roots = new List<string>();
        var warnings = new List<string>();
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(@"Software\Valve\Steam");
                    if ((key?.GetValue("SteamPath") ?? key?.GetValue("InstallPath")) is string path && Directory.Exists(path))
                        roots.Add(path);
                }
                catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                { warnings.Add($"Não foi possível consultar a instalação Steam: {error.Message}"); }
        var defaultSteam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(defaultSteam)) roots.Add(defaultSteam);
        var epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        var result = GameDiscovery.Scan(roots, epic);
        return result with { Warnings = result.Warnings.Concat(warnings).ToList() };
    }
}
