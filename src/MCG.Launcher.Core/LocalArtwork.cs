namespace MCG.Launcher.Core;

public sealed record ArtworkFile(string Path, CoverPresentation Presentation);

public static class LocalArtwork
{
    public static ArtworkFile? FindSteam(string steamRoot, string appId)
    {
        if (!ulong.TryParse(appId, out var id) || id == 0) return null;
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        var appCache = Path.Combine(cache, appId);
        // Steam now keeps assets inside a directory per app. Earlier clients used flat names.
        foreach (var name in new[] { "header.jpg", "library_hero.jpg", "library_hero_2x.jpg", "library_600x900.jpg", "library_600x900_2x.jpg" })
            foreach (var path in new[] { Path.Combine(appCache, name), Path.Combine(cache, $"{appId}_{name}") })
                if (File.Exists(path)) return new(path, CoverPresentation.Artwork);
        foreach (var path in new[] { Path.Combine(appCache, "logo.png"), Path.Combine(cache, $"{appId}_logo.png") })
            if (File.Exists(path)) return new(path, CoverPresentation.Logo);
        // Hashed JPGs in this directory are often small app icons, not cover art.
        try
        {
            if (Directory.Exists(appCache))
            {
                var icon = Directory.EnumerateFiles(appCache, "*.jpg")
                    .Where(p => Path.GetFileNameWithoutExtension(p) is { Length: 40 } hash && hash.All(Uri.IsHexDigit))
                    .Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (icon is not null) return new(icon, CoverPresentation.Icon);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return null;
    }

    public static ArtworkFile? FindInInstallation(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) return null;
        // Only known artwork names in a few conventional folders. Never traverse game archives or texture trees.
        foreach (var name in new[] { "cover", "header", "capsule", "boxart", "banner", "logo" })
            foreach (var folder in new[] { "", "Artwork", "Images", "Media" })
                foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" })
                {
                    var path = Path.Combine(directory, folder, name + extension);
                    if (File.Exists(path)) return new(path, name == "logo" ? CoverPresentation.Logo : CoverPresentation.Artwork);
                }
        return null;
    }

    public static string FindExecutable(string directory, string gameName)
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) return "";
        try
        {
            string Key(string value) => string.Concat(LibraryRules.SearchKey(value).Where(char.IsLetterOrDigit));
            var name = Key(gameName);
            var folderName = Key(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)));
            return Directory.EnumerateFiles(directory, "*.exe")
                .Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(path => Key(Path.GetFileNameWithoutExtension(path)) is { } candidate
                    && (candidate == name || candidate == folderName)) ?? "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return ""; }
    }
}
