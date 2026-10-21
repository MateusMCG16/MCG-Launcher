using System.Text.Json;

namespace MCG.Launcher.Core;

public sealed record LauncherSettings
{
    public bool ImagesDisabled { get; init; }
    public string SteamGridDbApiKey { get; init; } = "";
}

public sealed class SettingsStore(string directory)
{
    public string FilePath => Path.Combine(directory, "settings.json");

    public async Task<LauncherSettings> LoadAsync()
    {
        if (!File.Exists(FilePath)) return new LauncherSettings();
        await using var stream = File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream)
            ?? throw new InvalidDataException("As configurações estão inválidas.");
    }

    public async Task SaveAsync(LauncherSettings settings)
    {
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "settings.pending.json");
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true });
            await stream.FlushAsync();
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
        else File.Move(temporary, FilePath);
    }
}
