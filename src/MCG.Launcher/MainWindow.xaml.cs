using System.Collections.ObjectModel;
using System.Diagnostics;
using MCG.Launcher.Core;
using MCG.Launcher.Services;
using MCG.Launcher.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MCG.Launcher;

public sealed partial class MainWindow : Window
{
    public ObservableCollection<GameCard> Cards { get; } = [];
    private readonly LibraryStore _store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCGLauncher"));
    private readonly SettingsStore _settingsStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCGLauncher"));
    private static readonly string CoversDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCGLauncher", "covers");
    private static readonly HttpClient Http = SteamGridDb.CreateHttpClient();
    private readonly HashSet<Guid> _remoteMisses = [];
    private LauncherSettings _settings = new();
    private bool _canWriteSettings;
    private List<GameEntry> _games = [];
    private readonly DispatcherQueueTimer _searchTimer;
    private bool _ready;
    private bool _busy;
    private bool _canWrite;
    private bool _dialogOpen;
    private readonly string? _dialogCheckReport;
    private TaskCompletionSource<bool>? _dialogCheckCompletion;

    public MainWindow(string? dialogCheckReport = null)
    {
        _dialogCheckReport = dialogCheckReport;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 860;
            presenter.PreferredMinimumHeight = 620;
        }
        _searchTimer = DispatcherQueue.CreateTimer();
        _searchTimer.Interval = TimeSpan.FromMilliseconds(180);
        _searchTimer.IsRepeating = false;
        _searchTimer.Tick += (_, _) => Refresh();
        Closed += (_, _) => _searchTimer.Stop();
        SetBusy(true);
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (_ready) return;
        _ready = true;
        try
        {
            _settings = await _settingsStore.LoadAsync();
            _canWriteSettings = true;
        }
        catch (Exception error)
        {
            Notify("Não foi possível carregar as configurações", $"{error.Message}\nArquivo preservado: {_settingsStore.FilePath}", InfoBarSeverity.Error);
        }
        try
        {
            _games = await _store.LoadAsync();
            _canWrite = true;
        }
        catch (Exception error)
        {
            Notify("Não foi possível carregar sua biblioteca", $"{error.Message}\nArquivo preservado: {_store.FilePath}", InfoBarSeverity.Error);
        }
        finally { SetBusy(false); Refresh(); }
        if (_dialogCheckReport is not null)
        {
            _dialogCheckCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await DialogDiagnostics.CheckSettingsButtonAsync(SettingsButton, _dialogCheckCompletion.Task, _dialogCheckReport);
            Close();
            return;
        }
        if (_canWrite && _games.Count == 0) await DetectAsync();
        else if (_canWrite && !_settings.ImagesDisabled && _games.Any(g => g.Source != GameSource.Manual
            && string.IsNullOrWhiteSpace(g.CoverPath) && string.IsNullOrWhiteSpace(g.IconPath)))
            await DetectAsync(imagesOnly: true);
        await FetchRemoteCoversAsync(quiet: true);
    }

    // SteamGridDB is the first cover source. Downloads are kept in CoversDirectory and reused forever;
    // games it does not know keep their local art (Steam poster, logo or icon) as the fallback.
    private async Task FetchRemoteCoversAsync(bool quiet)
    {
        var key = _settings.SteamGridDbApiKey.Trim();
        if (_busy || !_canWrite || _settings.ImagesDisabled || key.Length == 0) return;
        // Games SteamGridDB did not know are asked again only on the next launch, not on every scan.
        var missing = _games.Where(g => LibraryRules.NeedsRemoteCover(g, CoversDirectory) && !_remoteMisses.Contains(g.Id)).ToList();
        if (missing.Count == 0) return;
        SetBusy(true);
        CountLabel.Text = $"Buscando capas no SteamGridDB para {missing.Count} jogo(s)…";
        var found = new Dictionary<Guid, string>();
        try
        {
            var client = new SteamGridDb(Http, key);
            foreach (var game in missing)
                if (await client.DownloadPosterAsync(game, CoversDirectory) is { } path) found[game.Id] = path;
                else _remoteMisses.Add(game.Id);
        }
        catch (Exception error) when (error is SteamGridDbException or HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException)
        {
            Notify("Não foi possível buscar capas no SteamGridDB", SteamGridDb.Describe(error), InfoBarSeverity.Warning);
        }
        try
        {
            if (found.Count > 0)
            {
                var updated = _games.Select(g => found.TryGetValue(g.Id, out var path)
                    ? g with { CoverPath = path, CoverPresentation = CoverPresentation.Poster } : g).ToList();
                await _store.SaveAsync(updated);
                _games = updated;
                Notify("Capas do SteamGridDB adicionadas", $"{found.Count} jogo(s) receberam capa vertical.", InfoBarSeverity.Success);
            }
            else if (!quiet && !Notice.IsOpen) Notify("SteamGridDB", "Nenhuma capa nova encontrada. Os jogos restantes usam as imagens do PC.", InfoBarSeverity.Informational);
        }
        catch (Exception error) { Notify("Não foi possível salvar as capas", error.Message, InfoBarSeverity.Error); }
        finally { SetBusy(false); Refresh(); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        AddGameButton.IsEnabled = !busy && _canWrite;
        DetectButton.IsEnabled = !busy && _canWrite;
        LocalImagesButton.IsEnabled = !busy && _canWrite && !_settings.ImagesDisabled;
        SettingsButton.IsEnabled = !busy && _canWriteSettings;
        GamesGrid.IsEnabled = !busy;
        ScanProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        if (!_ready) return;
        var search = LibraryRules.SearchKey(SearchBox.Text.Trim());
        var filter = SourceFilter.SelectedIndex;
        IEnumerable<GameEntry> filtered = filter switch
        {
            1 => _games.Where(g => g.IsFavorite),
            2 => _games.Where(g => g.LastPlayedAt is not null),
            3 => _games.Where(g => g.Source == GameSource.Steam),
            4 => _games.Where(g => g.Source == GameSource.Epic),
            5 => _games.Where(g => g.Source == GameSource.Manual),
            _ => _games
        };
        if (search.Length > 0) filtered = filtered.Where(g => LibraryRules.SearchKey(g.Name).Contains(search, StringComparison.Ordinal));
        var sorted = SortOrder.SelectedIndex switch
        {
            1 => filtered.OrderByDescending(g => g.LastPlayedAt).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => filtered.OrderByDescending(g => g.AddedAt).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        Cards.Clear();
        foreach (var game in sorted) Cards.Add(new GameCard(game, _settings.ImagesDisabled));
        CountLabel.Text = $"{Cards.Count} de {_games.Count} jogos · {_games.Count(g => g.IsFavorite)} favoritos";
        EmptyState.Visibility = Cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _games.Count == 0 ? "Sua próxima partida começa aqui" : "Nenhum jogo por aqui";
        EmptyDescription.Text = _games.Count == 0
            ? "Detecte os jogos da Steam e Epic ou adicione um executável ou atalho."
            : filter == 1 && search.Length == 0
                ? "Marque a estrela de um jogo para encontrá-lo aqui."
                : filter == 2 && search.Length == 0
                    ? "Os jogos abertos pelo launcher aparecem aqui."
                    : "Tente outro nome ou ajuste os filtros.";
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_ready) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, SourceFilter) && SourceFilter.SelectedIndex == 2 && SortOrder.SelectedIndex != 1) SortOrder.SelectedIndex = 1;
        else Refresh();
    }
    private void Library_Click(object sender, RoutedEventArgs e) { SearchBox.Text = ""; SourceFilter.SelectedIndex = 0; Refresh(); }

    private async void Detect_Click(object sender, RoutedEventArgs e) { await DetectAsync(); await FetchRemoteCoversAsync(quiet: true); }
    private async void LocalImages_Click(object sender, RoutedEventArgs e) { await DetectAsync(imagesOnly: true); await FetchRemoteCoversAsync(quiet: false); }
    private async Task DetectAsync(bool imagesOnly = false)
    {
        if (_busy || !_canWrite) return;
        SetBusy(true);
        CountLabel.Text = imagesOnly ? "Procurando imagens já presentes no PC…" : "Procurando instalações da Steam e Epic Games…";
        try
        {
            var result = await Task.Run(WindowsDiscovery.Scan);
            var discovered = imagesOnly ? result.Games.Where(g => _games.Any(existing =>
                string.Equals(existing.Identity, g.Identity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing.Target, g.Target, StringComparison.OrdinalIgnoreCase))) : result.Games;
            var manual = await Task.Run(() => _games.Where(g => g.Source == GameSource.Manual).Select(g =>
            {
                var directory = string.IsNullOrWhiteSpace(g.WorkingDirectory) ? Path.GetDirectoryName(g.Target) ?? "" : g.WorkingDirectory;
                var artwork = LocalArtwork.FindInInstallation(directory);
                return g with { CoverPath = artwork?.Path ?? "", CoverPresentation = artwork?.Presentation ?? CoverPresentation.Artwork, IconPath = g.Target };
            }).ToList());
            var merged = LibraryRules.Merge(_games, discovered.Concat(manual));
            var added = merged.Count - _games.Count;
            var updatedImages = merged.Count(g => _games.Any(original => original.Id == g.Id
                && (original.CoverPath != g.CoverPath || original.IconPath != g.IconPath)));
            if (!merged.SequenceEqual(_games))
            {
                await _store.SaveAsync(merged);
                _games = merged;
            }
            Notify(imagesOnly ? "Imagens locais atualizadas" : added > 0 ? $"{added} jogo(s) adicionado(s)" : "Biblioteca atualizada",
                result.Warnings.Count > 0
                    ? $"{result.Warnings.Count} arquivo(s) ou pasta(s) não puderam ser lidos. {result.Warnings[0]}"
                    : updatedImages > 0 ? $"Imagens ou ícones encontrados para {updatedImages} jogo(s)."
                        : imagesOnly ? "Nenhuma imagem nova encontrada. As capas personalizadas foram mantidas."
                        : added > 0 ? "Seus jogos estão prontos para abrir."
                        : "Nenhum jogo novo encontrado na Steam ou Epic. Você também pode adicionar jogos manualmente.",
                result.Warnings.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        catch (Exception error) { Notify("Não foi possível detectar os jogos", error.Message, InfoBarSeverity.Error); }
        finally { SetBusy(false); Refresh(); }
    }

    private async Task<bool> CommitAsync(List<GameEntry> games)
    {
        if (_busy || !_canWrite) return false;
        SetBusy(true);
        try
        {
            await _store.SaveAsync(games);
            _games = games;
            Refresh();
            return true;
        }
        catch (Exception error) { Notify("Não foi possível salvar", error.Message, InfoBarSeverity.Error); return false; }
        finally { SetBusy(false); }
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e) => await EditGameAsync(null);
    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFrom(sender) is { } game) await EditGameAsync(game);
    }
    private async Task EditGameAsync(GameEntry? game)
    {
        if (_busy || !_canWrite || _dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var dialog = new GameEditorDialog(this, game) { XamlRoot = Root.XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is not { } edited) return;
            if (_games.Any(g => g.Id != edited.Id && (string.Equals(g.Identity, edited.Identity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(g.Target, edited.Target, StringComparison.OrdinalIgnoreCase))))
            {
                Notify("Esse jogo já está na biblioteca", "Edite o cadastro existente para evitar duplicatas.", InfoBarSeverity.Warning);
                return;
            }
            var updated = game is null ? _games.Append(edited).ToList() : _games.Select(g => g.Id == edited.Id ? edited : g).ToList();
            if (await CommitAsync(updated)) Notify("Jogo salvo", $"{edited.Name} está na sua biblioteca.", InfoBarSeverity.Success);
        }
        catch (Exception error) { Notify("Não foi possível editar o jogo", error.Message, InfoBarSeverity.Error); }
        finally { _dialogOpen = false; }
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFrom(sender) is not { } game) return;
        await CommitAsync(_games.Select(g => g.Id == game.Id ? g with { IsFavorite = !g.IsFavorite } : g).ToList());
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || EntryFrom(sender) is not { } game) return;
        try
        {
            GameLauncher.Launch(game);
            var saved = await CommitAsync(_games.Select(g => g.Id == game.Id ? g with { LastPlayedAt = DateTimeOffset.UtcNow } : g).ToList());
            if (saved) Notify("Iniciando jogo", $"{game.Name} recebeu o comando de abertura.", InfoBarSeverity.Informational);
        }
        catch (Exception error) { Notify("Não foi possível abrir o jogo", error.Message, InfoBarSeverity.Error); }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_canWrite || _dialogOpen || EntryFrom(sender) is not { } game) return;
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = "Remover da biblioteca?",
                Content = $"Remover {game.Name} desta biblioteca? Os arquivos do jogo permanecem no seu PC. Jogos da Steam e Epic podem reaparecer ao detectar instalações.",
                PrimaryButtonText = "Remover", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await CommitAsync(_games.Where(g => g.Id != game.Id).ToList());
        }
        catch (Exception error) { Notify("Não foi possível remover o cadastro", error.Message, InfoBarSeverity.Error); }
        finally { _dialogOpen = false; }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFrom(sender) is not { } game) return;
        try
        {
            var directory = string.IsNullOrWhiteSpace(game.WorkingDirectory) ? Path.GetDirectoryName(game.Target) : game.WorkingDirectory;
            if (directory is null || !Directory.Exists(directory)) throw new DirectoryNotFoundException("A pasta do jogo não foi encontrada.");
            using var process = Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception error) { Notify("Não foi possível abrir a pasta", error.Message, InfoBarSeverity.Error); }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dialogOpen || !_canWriteSettings) return;
        _dialogOpen = true;
        try
        {
            var dialog = CreateSettingsDialog(out var imagesToggle, out var keyBox);
            if (_dialogCheckCompletion is not null && _dialogCheckReport is not null)
            {
                await DialogDiagnostics.CheckSettingsAsync(dialog, imagesToggle, _dialogCheckReport);
                _dialogCheckCompletion.TrySetResult(true);
                return;
            }
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var updated = _settings with { ImagesDisabled = imagesToggle.IsOn, SteamGridDbApiKey = keyBox.Password.Trim() };
            if (updated == _settings) return;
            await _settingsStore.SaveAsync(updated);
            _settings = updated;
            SetBusy(_busy);
            Refresh();
            _dialogOpen = false;
            await FetchRemoteCoversAsync(quiet: false);
        }
        catch (Exception error)
        {
            _dialogCheckCompletion?.TrySetException(error);
            Notify("Não foi possível abrir ou salvar as configurações", error.Message, InfoBarSeverity.Error);
        }
        finally { _dialogOpen = false; }
    }

    private ContentDialog CreateSettingsDialog(out ToggleSwitch imagesToggle, out PasswordBox keyBox)
    {
        imagesToggle = new ToggleSwitch
        {
            Header = "Desativar imagens",
            IsOn = _settings.ImagesDisabled,
            OnContent = "Ativado",
            OffContent = "Desativado"
        };
        keyBox = new PasswordBox
        {
            Header = "Chave da API do SteamGridDB",
            PlaceholderText = "Opcional",
            Password = _settings.SteamGridDbApiKey,
            Width = 360
        };
        var content = new StackPanel { Spacing = 20 };
        content.Children.Add(imagesToggle);
        var steamGrid = new StackPanel { Spacing = 6 };
        steamGrid.Children.Add(keyBox);
        steamGrid.Children.Add(new TextBlock
        {
            Text = "Usada só para jogos sem nenhuma imagem no PC. Cada usuário cria a própria chave, gratuita, no site do SteamGridDB.",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 360, FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"]
        });
        steamGrid.Children.Add(new HyperlinkButton { Content = "Gerar minha chave", NavigateUri = new Uri(SteamGridDb.KeyPage), Padding = new Thickness(0, 4, 0, 4) });
        content.Children.Add(steamGrid);
        return new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Configurações",
            Content = content,
            PrimaryButtonText = "Salvar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary
        };
    }

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            await new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = "MCG Launcher · 0.1",
                Content = "Uma biblioteca local para seus jogos.\n\nDetecção: Steam e Epic Games. Outros jogos podem ser adicionados por executável (.exe) ou atalho (.lnk).\n\nJogos dessas plataformas ainda usam seus clientes para autenticação e abertura.\n\nBiblioteca e backup:\n" + Path.GetDirectoryName(_store.FilePath),
                CloseButtonText = "Fechar"
            }.ShowAsync();
        }
        catch (Exception error) { Notify("Não foi possível abrir as informações", error.Message, InfoBarSeverity.Error); }
        finally { _dialogOpen = false; }
    }

    private static GameEntry? EntryFrom(object sender) => (sender as FrameworkElement)?.Tag as GameEntry;
    private void Notify(string title, string message, InfoBarSeverity severity)
    { Notice.Title = title; Notice.Message = message; Notice.Severity = severity; Notice.IsOpen = true; }
}

