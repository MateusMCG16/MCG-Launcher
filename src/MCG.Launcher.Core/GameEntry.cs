using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace MCG.Launcher.Core;

public enum GameSource { Manual, Steam, Epic }
public enum LaunchKind { Executable, Shortcut, Protocol }
// Poster: vertical 2:3 art (Steam library_600x900 or SteamGridDB). Custom: chosen by the user, never replaced.
// New values go at the end because the library stores the numeric value.
public enum CoverPresentation { Artwork, Logo, Icon, Poster, Custom }

public sealed record GameEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public GameSource Source { get; init; }
    public string SourceId { get; init; } = "";
    public LaunchKind LaunchKind { get; init; }
    public string Target { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string CoverPath { get; init; } = "";
    public CoverPresentation CoverPresentation { get; init; }
    public string IconPath { get; init; } = "";
    public bool IsFavorite { get; init; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayedAt { get; init; }

    [JsonIgnore]
    public string Identity => Source != GameSource.Manual && !string.IsNullOrWhiteSpace(SourceId)
        ? $"{Source}:{SourceId}"
        : $"target:{Target.Trim()}";
}

public static class LibraryRules
{
    // A SteamGridDB download is stored as Poster; local discovery has the same rank at best, so it never replaces it.
    public static List<GameEntry> Merge(IEnumerable<GameEntry> existing, IEnumerable<GameEntry> discovered)
    {
        var games = existing.ToList();
        var identities = games.Select((g, i) => (g.Identity, i))
            .GroupBy(p => p.Identity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p.Key, p => p.First().i, StringComparer.OrdinalIgnoreCase);
        var targets = games.Select((g, i) => (Target: g.Target.Trim(), i))
            .GroupBy(p => p.Target, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p.Key, p => p.First().i, StringComparer.OrdinalIgnoreCase);
        foreach (var game in discovered)
        {
            if (identities.TryGetValue(game.Identity, out var index) || targets.TryGetValue(game.Target.Trim(), out index))
            {
                var original = games[index];
                var fillCover = (string.IsNullOrWhiteSpace(original.CoverPath)
                    || CoverRank(game.CoverPresentation) > CoverRank(original.CoverPresentation))
                    && File.Exists(game.CoverPath);
                var fillIcon = string.IsNullOrWhiteSpace(original.IconPath) && File.Exists(game.IconPath);
                games[index] = original with
                {
                    CoverPath = fillCover ? game.CoverPath : original.CoverPath,
                    CoverPresentation = fillCover ? game.CoverPresentation : original.CoverPresentation,
                    IconPath = fillIcon ? game.IconPath : original.IconPath
                };
                continue;
            }
            identities[game.Identity] = games.Count;
            targets[game.Target.Trim()] = games.Count;
            games.Add(game);
        }
        return games;
    }

    public static int CoverRank(CoverPresentation presentation) => presentation switch
    {
        CoverPresentation.Custom => 4,
        CoverPresentation.Poster => 3,
        CoverPresentation.Artwork => 2,
        CoverPresentation.Logo => 1,
        _ => 0
    };

    // SteamGridDB is the first source: every game asks it once, unless the user chose a cover or a downloaded
    // one is already kept in the covers directory. Local art (Steam poster, logo, icon) is the fallback.
    public static bool NeedsRemoteCover(GameEntry game, string coversDirectory) =>
        game.CoverPresentation != CoverPresentation.Custom && !IsDownloadedCover(game, coversDirectory);

    public static bool IsDownloadedCover(GameEntry game, string coversDirectory) =>
        !string.IsNullOrWhiteSpace(game.CoverPath) && File.Exists(game.CoverPath)
        && Path.GetFullPath(game.CoverPath).StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(coversDirectory)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static string SearchKey(string value)
    {
        var result = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                result.Append(char.ToUpperInvariant(character));
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
