using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCG.Launcher.Core;

public sealed class LibraryStore(string directory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;
    private bool _writable;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string FilePath => Path.Combine(directory, "library.json");

    public async Task<List<GameEntry>> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            List<GameEntry> games = [];
            if (File.Exists(FilePath))
            {
                await using var stream = File.OpenRead(FilePath);
                games = await JsonSerializer.DeserializeAsync<List<GameEntry>>(stream, JsonOptions)
                    ?? throw new InvalidDataException("A biblioteca está vazia ou inválida.");
                if (games.Any(g => g is null || g.Id == Guid.Empty || string.IsNullOrWhiteSpace(g.Name)
                    || string.IsNullOrWhiteSpace(g.Target) || !Enum.IsDefined(g.Source)
                    || !Enum.IsDefined(g.LaunchKind) || !Enum.IsDefined(g.CoverPresentation)) || games.Select(g => g.Id).Distinct().Count() != games.Count)
                    throw new InvalidDataException("A biblioteca contém registros inválidos.");
            }
            _loaded = true;
            _writable = true;
            return games;
        }
        catch
        {
            _loaded = true;
            _writable = false;
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(IEnumerable<GameEntry> games)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_loaded || !_writable)
                throw new InvalidOperationException("A biblioteca não foi carregada com segurança. O arquivo original foi preservado.");
            Directory.CreateDirectory(directory);
            // Write the complete new library before atomically replacing the previous version.
            // A interrupted write never truncates library.json; keep one backup for recovery.
            var temporary = Path.Combine(directory, "library.pending.json");
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, games.ToList(), JsonOptions);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, Path.Combine(directory, "library.backup.json"));
            else File.Move(temporary, FilePath);
        }
        finally { _gate.Release(); }
    }
}
