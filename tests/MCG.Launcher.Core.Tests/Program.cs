using System.Text.Json;
using MCG.Launcher.Core;

// Small dependency-free regression runner. It never starts a real game or touches the user's library.
var fixtures = Path.Combine(Path.GetTempPath(), "MCGLauncher-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtures);
var failed = 0;
var passed = 0;
var steamRoot = Path.Combine(fixtures, "Steam");
var steamApps = Path.Combine(steamRoot, "steamapps");
Directory.CreateDirectory(Path.Combine(steamApps, "common", "Test Game"));
string SteamManifest(string id = "123", string state = "4", string folder = "Test Game") =>
    $"\"AppState\" {{ \"appid\" \"{id}\" \"name\" \"Test Game\" \"StateFlags\" \"{state}\" \"installdir\" \"{folder}\" }}";

await Check("Steam: comentários, aspas e caminhos escapados", () =>
{
    var root = SteamVdf.Parse("// comment\n\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Games\" \"label\" \"My \\\"Library\\\"\" } }");
    Equal(@"C:\Games", root.Children["libraryfolders"].Children["0"].Value("path"));
    Equal("My \"Library\"", root.Children["libraryfolders"].Children["0"].Value("label"));
    return Task.CompletedTask;
});
await Check("Steam: rejeita manifesto incompleto", () =>
{
    Throws<FormatException>(() => SteamVdf.Parse("\"AppState\" { \"name\" \"Game\""));
    return Task.CompletedTask;
});
await Check("Steam: lê bibliotecas antigas e atuais", () =>
{
    var libraries = GameDiscovery.ReadSteamLibraries("\"libraryfolders\" { \"0\" \"C:\\\\Steam\" \"1\" { \"path\" \"D:\\\\SteamLibrary\" } }");
    Equal(2, libraries.Count);
    True(libraries.Contains(@"D:\SteamLibrary"));
    return Task.CompletedTask;
});
await Check("Steam: detecta jogo instalado e usa protocolo", () =>
{
    var game = GameDiscovery.ReadSteamManifest(SteamManifest(), steamApps, steamRoot)!;
    Equal("steam://rungameid/123", game.Target);
    Equal(GameSource.Steam, game.Source);
    True(GameLauncher.CreateStartInfo(game).UseShellExecute);
    return Task.CompletedTask;
});
await Check("Steam: ignora download incompleto, ferramenta e pasta ausente", () =>
{
    True(GameDiscovery.ReadSteamManifest(SteamManifest(state: "2"), steamApps, steamRoot) is null);
    True(GameDiscovery.ReadSteamManifest(SteamManifest(id: "228980"), steamApps, steamRoot) is null);
    True(GameDiscovery.ReadSteamManifest(SteamManifest(folder: "Missing"), steamApps, steamRoot) is null);
    return Task.CompletedTask;
});
await Check("Steam: bloqueia pasta fora da biblioteca", () =>
{
    True(GameDiscovery.ReadSteamManifest(SteamManifest(folder: "..\\.."), steamApps, steamRoot) is null);
    return Task.CompletedTask;
});
var epicInstall = Path.Combine(fixtures, "Epic Game");
Directory.CreateDirectory(epicInstall);
await File.WriteAllTextAsync(Path.Combine(epicInstall, "game.exe"), "fixture, not executable");
string EpicManifest(bool incomplete = false, string executable = "game.exe") => JsonSerializer.Serialize(new
{
    DisplayName = "Epic Test", AppName = "artifact", CatalogNamespace = "sandbox", CatalogItemId = "catalog",
    InstallLocation = epicInstall, LaunchExecutable = executable, bIsIncompleteInstall = incomplete
});
await Check("Epic: constrói URI com IDs codificados", () =>
{
    var game = GameDiscovery.ReadEpicManifest(EpicManifest())!;
    Equal("sandbox:catalog:artifact", game.SourceId);
    Equal("com.epicgames.launcher://apps/sandbox%3Acatalog%3Aartifact?action=launch&silent=true", game.Target);
    _ = GameLauncher.CreateStartInfo(game);
    return Task.CompletedTask;
});
await Check("Epic: ignora instalação incompleta e executável ausente", () =>
{
    True(GameDiscovery.ReadEpicManifest(EpicManifest(incomplete: true)) is null);
    True(GameDiscovery.ReadEpicManifest(EpicManifest(executable: "missing.exe")) is null);
    return Task.CompletedTask;
});
await Check("Detecção: arquivo inválido não impede outros jogos", async () =>
{
    await File.WriteAllTextAsync(Path.Combine(steamApps, "appmanifest_123.acf"), SteamManifest());
    await File.WriteAllTextAsync(Path.Combine(steamApps, "appmanifest_bad.acf"), "\"AppState\" {");
    var result = GameDiscovery.Scan([steamRoot], Path.Combine(fixtures, "missingEpic"));
    Equal(1, result.Games.Count);
    Equal(1, result.Warnings.Count);
});
await Check("Merge: preserva personalização e deduplica por ID e destino", () =>
{
    var original = new GameEntry { Name = "Meu nome", Source = GameSource.Steam, SourceId = "123", Target = "steam://rungameid/123", IsFavorite = true, CoverPath = "my-cover.png" };
    var discovered = original with { Id = Guid.NewGuid(), Name = "Nome original", IsFavorite = false, CoverPath = "" };
    var manualDuplicate = discovered with { Id = Guid.NewGuid(), Source = GameSource.Manual, SourceId = "", Target = "STEAM://RUNGAMEID/123" };
    var merged = LibraryRules.Merge([original], [discovered, manualDuplicate, discovered]);
    Equal(1, merged.Count);
    Equal(original, merged[0]);
    return Task.CompletedTask;
});
await Check("Busca: ignora acentos e capitalização", () =>
{
    True(LibraryRules.SearchKey("Ação Épica").Contains(LibraryRules.SearchKey("acao epica")));
    return Task.CompletedTask;
});
await Check("Abertura: prepara executável sem shell intermediário", () =>
{
    var game = new GameEntry { Name = "Game", Target = Path.Combine(epicInstall, "game.exe"), Arguments = "--windowed" };
    var start = GameLauncher.CreateStartInfo(game);
    Equal(game.Target, start.FileName);
    Equal("--windowed", start.Arguments);
    Equal(epicInstall, start.WorkingDirectory);
    return Task.CompletedTask;
});
await Check("Abertura: rejeita arquivos ausentes e protocolos arbitrários", () =>
{
    Throws<FileNotFoundException>(() => GameLauncher.CreateStartInfo(new GameEntry { Target = Path.Combine(fixtures, "missing.exe") }));
    Throws<InvalidOperationException>(() => GameLauncher.CreateStartInfo(new GameEntry { LaunchKind = LaunchKind.Protocol, Source = GameSource.Steam, Target = "https://example.com" }));
    Throws<InvalidOperationException>(() => GameLauncher.CreateStartInfo(new GameEntry { LaunchKind = LaunchKind.Protocol, Source = GameSource.Manual, Target = "steam://rungameid/123" }));
    return Task.CompletedTask;
});
await Check("Atalho: respeita a pasta de trabalho definida no próprio atalho", async () =>
{
    var shortcut = Path.Combine(fixtures, "game.lnk");
    await File.WriteAllTextAsync(shortcut, "fixture, not a real shortcut");
    var start = GameLauncher.CreateStartInfo(new GameEntry { Name = "Shortcut", Target = shortcut, LaunchKind = LaunchKind.Shortcut });
    Equal("", start.WorkingDirectory);
});
await Check("Persistência: round-trip, favoritos, histórico e backup", async () =>
{
    var directory = Path.Combine(fixtures, "store");
    var store = new LibraryStore(directory);
    Equal(0, (await store.LoadAsync()).Count);
    var game = new GameEntry { Name = "Persistência", Target = Path.Combine(epicInstall, "game.exe"), IsFavorite = true, LastPlayedAt = DateTimeOffset.UtcNow };
    await store.SaveAsync([game]);
    await store.SaveAsync([game with { Name = "Atualizado" }]);
    var loaded = await new LibraryStore(directory).LoadAsync();
    Equal("Atualizado", loaded.Single().Name);
    True(loaded.Single().IsFavorite);
    Equal(game.LastPlayedAt, loaded.Single().LastPlayedAt);
    True((await File.ReadAllTextAsync(Path.Combine(directory, "library.backup.json"))).Contains("Persist"));
});
await Check("Persistência: biblioteca corrompida é preservada", async () =>
{
    var directory = Path.Combine(fixtures, "corrupt");
    Directory.CreateDirectory(directory);
    var store = new LibraryStore(directory);
    await File.WriteAllTextAsync(store.FilePath, "broken json");
    await ThrowsAsync<JsonException>(() => store.LoadAsync());
    await ThrowsAsync<InvalidOperationException>(() => store.SaveAsync([]));
    Equal("broken json", await File.ReadAllTextAsync(store.FilePath));
});
await Check("Persistência: rejeita registros inválidos sem sobrescrever", async () =>
{
    var directory = Path.Combine(fixtures, "invalid");
    Directory.CreateDirectory(directory);
    var store = new LibraryStore(directory);
    await File.WriteAllTextAsync(store.FilePath, "[{\"Name\":\"\",\"Target\":\"\"}]");
    await ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    await ThrowsAsync<InvalidOperationException>(() => store.SaveAsync([]));
});
await Check("Persistência: não grava antes de carregar", async () =>
{
    await ThrowsAsync<InvalidOperationException>(() => new LibraryStore(Path.Combine(fixtures, "unloaded")).SaveAsync([]));
});

await Check("Imagens locais: cache Steam atual e formato antigo", async () =>
{
    var root = Path.Combine(fixtures, "art-steam");
    var appCache = Path.Combine(root, "appcache", "librarycache", "123");
    Directory.CreateDirectory(appCache);
    var header = Path.Combine(appCache, "header.jpg");
    await File.WriteAllTextAsync(header, "path fixture");
    var artwork = LocalArtwork.FindSteam(root, "123")!;
    Equal(header, artwork.Path);
    Equal(CoverPresentation.Artwork, artwork.Presentation);
    var oldHeader = Path.Combine(root, "appcache", "librarycache", "456_header.jpg");
    await File.WriteAllTextAsync(oldHeader, "path fixture");
    Equal(oldHeader, LocalArtwork.FindSteam(root, "456")!.Path);
    True(LocalArtwork.FindSteam(root, "..\\123") is null);
});
await Check("Imagens locais: diferencia logo e ícone de capa", async () =>
{
    var root = Path.Combine(fixtures, "art-icons");
    var logoCache = Path.Combine(root, "appcache", "librarycache", "123");
    var iconCache = Path.Combine(root, "appcache", "librarycache", "456");
    Directory.CreateDirectory(logoCache);
    Directory.CreateDirectory(iconCache);
    await File.WriteAllTextAsync(Path.Combine(logoCache, "logo.png"), "path fixture");
    await File.WriteAllTextAsync(Path.Combine(iconCache, new string('a', 40) + ".jpg"), "path fixture");
    await File.WriteAllTextAsync(Path.Combine(iconCache, "unrelated.jpg"), "path fixture");
    Equal(CoverPresentation.Logo, LocalArtwork.FindSteam(root, "123")!.Presentation);
    Equal(CoverPresentation.Icon, LocalArtwork.FindSteam(root, "456")!.Presentation);
    True(LocalArtwork.FindSteam(root, "789") is null);
});
await Check("Imagens locais: não escolhe textura ou splash do anti-cheat", async () =>
{
    var root = Path.Combine(fixtures, "art-install");
    var antiCheat = Path.Combine(root, "Binaries", "EasyAntiCheat");
    Directory.CreateDirectory(antiCheat);
    await File.WriteAllTextAsync(Path.Combine(antiCheat, "SplashScreen.png"), "path fixture");
    await File.WriteAllTextAsync(Path.Combine(root, "texture.jpg"), "path fixture");
    True(LocalArtwork.FindInInstallation(root) is null);
    var images = Path.Combine(root, "Images");
    Directory.CreateDirectory(images);
    var cover = Path.Combine(images, "cover.png");
    await File.WriteAllTextAsync(cover, "path fixture");
    Equal(cover, LocalArtwork.FindInInstallation(root)!.Path);
});
await Check("Imagens locais: identifica executável sem executar o jogo", async () =>
{
    var root = Path.Combine(fixtures, "Blender");
    Directory.CreateDirectory(root);
    await File.WriteAllTextAsync(Path.Combine(root, "blender-launcher.exe"), "fixture");
    await File.WriteAllTextAsync(Path.Combine(root, "support.exe"), "fixture");
    var gameExe = Path.Combine(root, "blender.exe");
    await File.WriteAllTextAsync(gameExe, "fixture");
    Equal(gameExe, LocalArtwork.FindExecutable(root, "Blender"));
});
await Check("Merge: preenche imagens de jogos existentes e preserva favoritos", async () =>
{
    var cover = Path.Combine(fixtures, "found-cover.png");
    await File.WriteAllTextAsync(cover, "fixture");
    var original = new GameEntry { Name = "Meu título", Source = GameSource.Steam, SourceId = "123", Target = "steam://rungameid/123", IsFavorite = true };
    var discovered = original with { Id = Guid.NewGuid(), Name = "Título original", IsFavorite = false, CoverPath = cover };
    var merged = LibraryRules.Merge([original], [discovered]).Single();
    Equal(original.Id, merged.Id);
    Equal(original.Name, merged.Name);
    True(merged.IsFavorite);
    Equal(cover, merged.CoverPath);
    var custom = original with { CoverPath = "custom-cover.png" };
    Equal(custom.CoverPath, LibraryRules.Merge([custom], [discovered]).Single().CoverPath);
    var icon = original with { CoverPath = "icon.jpg", CoverPresentation = CoverPresentation.Icon };
    Equal(cover, LibraryRules.Merge([icon], [discovered]).Single().CoverPath);
});
await Check("Imagens locais: capa vertical da Steam vence o header", async () =>
{
    var root = Path.Combine(fixtures, "art-poster");
    var appCache = Path.Combine(root, "appcache", "librarycache", "123");
    Directory.CreateDirectory(appCache);
    await File.WriteAllTextAsync(Path.Combine(appCache, "header.jpg"), "path fixture");
    var poster = Path.Combine(appCache, "library_600x900.jpg");
    await File.WriteAllTextAsync(poster, "path fixture");
    var artwork = LocalArtwork.FindSteam(root, "123")!;
    Equal(poster, artwork.Path);
    Equal(CoverPresentation.Poster, artwork.Presentation);
});
await Check("Merge: capa vertical substitui header, capa personalizada nunca é trocada", async () =>
{
    var poster = Path.Combine(fixtures, "poster.jpg");
    await File.WriteAllTextAsync(poster, "fixture");
    var original = new GameEntry { Name = "Jogo", Source = GameSource.Steam, SourceId = "123", Target = "steam://rungameid/123", CoverPath = "header.jpg" };
    var discovered = original with { Id = Guid.NewGuid(), CoverPath = poster, CoverPresentation = CoverPresentation.Poster };
    Equal(poster, LibraryRules.Merge([original], [discovered]).Single().CoverPath);
    var custom = original with { CoverPath = "minha.png", CoverPresentation = CoverPresentation.Custom };
    Equal("minha.png", LibraryRules.Merge([custom], [discovered]).Single().CoverPath);
});
await Check("SteamGridDB primeiro: só capa baixada ou personalizada dispensa a busca", async () =>
{
    var covers = Path.Combine(fixtures, "covers-first");
    Directory.CreateDirectory(covers);
    var downloaded = Path.Combine(covers, "abc.png");
    await File.WriteAllTextAsync(downloaded, "fixture");
    var localPoster = Path.Combine(fixtures, "local-600x900.jpg");
    await File.WriteAllTextAsync(localPoster, "fixture");
    var game = new GameEntry { Name = "Jogo", Source = GameSource.Steam, SourceId = "123", Target = "steam://rungameid/123" };
    True(LibraryRules.NeedsRemoteCover(game, covers));
    True(LibraryRules.NeedsRemoteCover(game with { CoverPath = localPoster, CoverPresentation = CoverPresentation.Poster }, covers));
    True(!LibraryRules.NeedsRemoteCover(game with { CoverPath = downloaded, CoverPresentation = CoverPresentation.Poster }, covers));
    True(!LibraryRules.NeedsRemoteCover(game with { CoverPath = localPoster, CoverPresentation = CoverPresentation.Custom }, covers));
    // A later local scan finds the Steam poster but keeps the SteamGridDB download.
    var kept = game with { CoverPath = downloaded, CoverPresentation = CoverPresentation.Poster };
    var scanned = game with { Id = Guid.NewGuid(), CoverPath = localPoster, CoverPresentation = CoverPresentation.Poster };
    Equal(downloaded, LibraryRules.Merge([kept], [scanned]).Single().CoverPath);
});
await Check("SteamGridDB: usa o appid da Steam, envia a chave e baixa a capa", async () =>
{
    var requests = new List<HttpRequestMessage>();
    var http = new HttpClient(new FakeHandler(request =>
    {
        requests.Add(request);
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/grids/steam/123")) return Json("{\"success\":true,\"data\":[{\"url\":\"https://cdn.example/grid.png\"}]}");
        if (request.RequestUri.Host == "cdn.example")
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("image/png") } } };
        return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    }));
    var game = new GameEntry { Name = "Jogo", Source = GameSource.Steam, SourceId = "123", Target = "steam://rungameid/123" };
    var saved = await new SteamGridDb(http, " chave ").DownloadPosterAsync(game, Path.Combine(fixtures, "covers"));
    Equal(Path.Combine(fixtures, "covers", game.Id.ToString("N") + ".png"), saved);
    Equal(3L, new FileInfo(saved!).Length);
    Equal("chave", requests[0].Headers.Authorization!.Parameter);
    True(requests[0].RequestUri!.Query.Contains("600x900"));
});
await Check("SteamGridDB: busca por nome e recusa chave inválida", async () =>
{
    var found = new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath switch
    {
        var p when p.Contains("/search/autocomplete/") => Json("{\"data\":[{\"id\":42,\"name\":\"Valorant\"}]}"),
        var p when p.EndsWith("/grids/game/42") => Json("{\"data\":[{\"url\":\"https://cdn.example/v.jpg\"}]}"),
        _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
    }));
    var epic = new GameEntry { Name = "VALORANT", Source = GameSource.Epic, SourceId = "x:y:z", Target = "com.epicgames.launcher://apps/x" };
    Equal("https://cdn.example/v.jpg", await new SteamGridDb(found, "k").FindPosterUrlAsync(epic));
    var denied = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)));
    await ThrowsAsync<SteamGridDbException>(() => new SteamGridDb(denied, "errada").FindPosterUrlAsync(epic));
});

await Check("Configurações: preferência persiste entre aberturas e pode ser reativada", async () =>
{
    var directory = Path.Combine(fixtures, "settings");
    var store = new SettingsStore(directory);
    True(!(await store.LoadAsync()).ImagesDisabled);
    Directory.CreateDirectory(directory);
    var library = Path.Combine(directory, "library.json");
    await File.WriteAllTextAsync(library, "biblioteca preservada");
    await store.SaveAsync(new LauncherSettings { ImagesDisabled = true });
    True((await new SettingsStore(directory).LoadAsync()).ImagesDisabled);
    await store.SaveAsync(new LauncherSettings { ImagesDisabled = false });
    True(!(await new SettingsStore(directory).LoadAsync()).ImagesDisabled);
    Equal("biblioteca preservada", await File.ReadAllTextAsync(library));
});
await Check("Configurações: arquivo inválido é preservado na leitura", async () =>
{
    var directory = Path.Combine(fixtures, "settings-invalid");
    Directory.CreateDirectory(directory);
    var store = new SettingsStore(directory);
    await File.WriteAllTextAsync(store.FilePath, "{invalid}");
    await ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
    Equal("{invalid}", await File.ReadAllTextAsync(store.FilePath));
});

Console.WriteLine($"\n{passed} passaram; {failed} falharam. Fixtures preservadas: {fixtures}");
return failed == 0 ? 0 : 1;

async Task Check(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine($"OK  {name}"); }
    catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error}"); }
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Esperado: {expected}; recebido: {actual}");
}
static void True(bool condition) { if (!condition) throw new Exception("A condição não foi atendida."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Esperava {typeof(T).Name}.");
}
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Esperava {typeof(T).Name}.");
}
static HttpResponseMessage Json(string body) =>
    new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
