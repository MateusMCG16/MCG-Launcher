using System.Text.Json;

namespace MCG.Launcher.Core;

public sealed record DiscoveryResult(IReadOnlyList<GameEntry> Games, IReadOnlyList<string> Warnings);

public static class GameDiscovery
{
    public static DiscoveryResult Scan(IEnumerable<string> steamRoots, string epicManifestDirectory)
    {
        var games = new List<GameEntry>();
        var warnings = new List<string>();
        foreach (var root in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            ReadSafely(libraryFile, () => libraries.UnionWith(ReadSteamLibraries(File.ReadAllText(libraryFile))));
            foreach (var library in libraries)
            {
                var steamApps = Path.Combine(library, "steamapps");
                foreach (var manifest in FilesIn(steamApps, "appmanifest_*.acf"))
                    ReadSafely(manifest, () =>
                    {
                        var game = ReadSteamManifest(File.ReadAllText(manifest), steamApps, root);
                        if (game is not null) games.Add(game);
                    });
            }
        }
        foreach (var manifest in FilesIn(epicManifestDirectory, "*.item"))
            ReadSafely(manifest, () =>
            {
                var game = ReadEpicManifest(File.ReadAllText(manifest));
                if (game is not null) games.Add(game);
            });
        return new DiscoveryResult(LibraryRules.Merge([], games), warnings);

        IEnumerable<string> FilesIn(string directory, string pattern)
        {
            try { return Directory.Exists(directory) ? Directory.GetFiles(directory, pattern) : []; }
            catch (Exception error) when (IsExpected(error))
            { warnings.Add($"Não foi possível ler {directory}: {error.Message}"); return []; }
        }
        void ReadSafely(string path, Action read)
        {
            if (!File.Exists(path)) return;
            try { read(); }
            catch (Exception error) when (IsExpected(error))
            { warnings.Add($"Não foi possível ler {Path.GetFileName(path)}: {error.Message}"); }
        }
    }

    private static bool IsExpected(Exception error) => error is IOException or UnauthorizedAccessException
        or FormatException or JsonException or InvalidOperationException or ArgumentException;

    public static IReadOnlyList<string> ReadSteamLibraries(string text)
    {
        var parsed = SteamVdf.Parse(text);
        if (!parsed.Children.TryGetValue("libraryfolders", out var folders)) return [];
        return folders.Children.Values.Select(n => n.Value("path"))
            .Concat(folders.Values.Where(p => int.TryParse(p.Key, out _)).Select(p => p.Value))
            .Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static GameEntry? ReadSteamManifest(string text, string steamApps, string steamRoot)
    {
        var parsed = SteamVdf.Parse(text);
        if (!parsed.Children.TryGetValue("AppState", out var app)) return null;
        var id = app.Value("appid");
        var name = app.Value("name");
        var folder = app.Value("installdir");
        if (!ulong.TryParse(id, out var appId) || appId == 0 || appId == 228980
            || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(folder)) return null;
        // FullyInstalled (4); skip manifests left by uninstalled or incomplete downloads.
        if (!uint.TryParse(app.Value("StateFlags"), out var flags) || (flags & 4) == 0) return null;
        var common = Path.GetFullPath(Path.Combine(steamApps, "common")) + Path.DirectorySeparatorChar;
        var install = Path.GetFullPath(Path.Combine(common, folder));
        if (!install.StartsWith(common, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(install)) return null;
        var steamArtwork = LocalArtwork.FindSteam(steamRoot, id);
        var installationArtwork = LocalArtwork.FindInInstallation(install);
        var artwork = steamArtwork?.Presentation == CoverPresentation.Icon
            ? installationArtwork ?? steamArtwork : steamArtwork ?? installationArtwork;
        return new GameEntry
        {
            Name = name, Source = GameSource.Steam, SourceId = id, LaunchKind = LaunchKind.Protocol,
            Target = $"steam://rungameid/{id}", WorkingDirectory = install,
            CoverPath = artwork?.Path ?? "", CoverPresentation = artwork?.Presentation ?? CoverPresentation.Artwork,
            IconPath = LocalArtwork.FindExecutable(install, name)
        };
    }

    public static GameEntry? ReadEpicManifest(string text)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Manifesto Epic inválido.");
        if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return null;
        string Read(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
        var appName = Read("AppName");
        var name = Read("DisplayName");
        var install = Read("InstallLocation");
        var executable = Read("LaunchExecutable");
        if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(name)
            || !Path.IsPathFullyQualified(install) || !Directory.Exists(install)
            || string.IsNullOrWhiteSpace(executable)) return null;
        var fullInstall = Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var launchPath = Path.GetFullPath(Path.Combine(fullInstall, executable));
        if (!launchPath.StartsWith(fullInstall, StringComparison.OrdinalIgnoreCase) || !File.Exists(launchPath)) return null;
        var catalogNamespace = Read("CatalogNamespace");
        var catalogId = Read("CatalogItemId");
        var launchId = !string.IsNullOrWhiteSpace(catalogNamespace) && !string.IsNullOrWhiteSpace(catalogId)
            ? $"{catalogNamespace}:{catalogId}:{appName}" : appName;
        var artwork = LocalArtwork.FindInInstallation(install);
        return new GameEntry
        {
            Name = name, Source = GameSource.Epic, SourceId = launchId, LaunchKind = LaunchKind.Protocol,
            Target = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(launchId)}?action=launch&silent=true",
            WorkingDirectory = install, IconPath = launchPath,
            CoverPath = artwork?.Path ?? "", CoverPresentation = artwork?.Presentation ?? CoverPresentation.Artwork
        };
    }
}
