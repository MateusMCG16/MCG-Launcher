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
    private LauncherSettings _settings = new();
    private bool _canWriteSettings;
    private List<GameEntry> _games = [];
    private readonly DispatcherQueueTimer _searchTimer;
    private string _section = "library";
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
            presenter.PreferredMinimumWidth = 1020;
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
        IEnumerable<GameEntry> filtered = _games;
        if (_section == "favorites") filtered = filtered.Where(g => g.IsFavorite);
        if (_section == "recent") filtered = filtered.Where(g => g.LastPlayedAt is not null);
        filtered = SourceFilter.SelectedIndex switch
        {
            1 => filtered.Where(g => g.Source == GameSource.Steam),
            2 => filtered.Where(g => g.Source == GameSource.Epic),
            3 => filtered.Where(g => g.Source == GameSource.Manual),
            _ => filtered
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
        PageTitle.Text = _section switch { "favorites" => "Seus favoritos", "recent" => "Jogados recentemente", _ => "Sua biblioteca" };
        PageSubtitle.Text = _section switch
        {
            "favorites" => "Os jogos que você quer ter sempre por perto.",
            "recent" => "Continue de onde parou.",
            _ => "Todos os seus jogos. Um lugar para começar."
        };
        var active = new SolidColorBrush(ColorHelper.FromArgb(255, 45, 55, 38));
        var inactive = new SolidColorBrush(Colors.Transparent);
        LibraryNav.Background = _section == "library" ? active : inactive;
        FavoritesNav.Background = _section == "favorites" ? active : inactive;
        RecentNav.Background = _section == "recent" ? active : inactive;
        CountLabel.Text = $"{Cards.Count} de {_games.Count} jogos · {_games.Count(g => g.IsFavorite)} favoritos";
        EmptyState.Visibility = Cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _games.Count == 0 ? "Sua próxima partida começa aqui" : "Nenhum jogo por aqui";
        EmptyDescription.Text = _games.Count == 0
            ? "Detecte os jogos da Steam e Epic ou adicione um executável ou atalho."
            : _section == "favorites" && search.Length == 0 && SourceFilter.SelectedIndex == 0
                ? "Marque a estrela de um jogo para encontrá-lo aqui."
                : _section == "recent" && search.Length == 0 && SourceFilter.SelectedIndex == 0
                    ? "Os jogos abertos pelo launcher aparecem aqui."
                    : "Tente outro nome ou ajuste os filtros.";
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_ready) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => Refresh();
    private void Library_Click(object sender, RoutedEventArgs e) { _section = "library"; Refresh(); }
    private void Favorites_Click(object sender, RoutedEventArgs e) { _section = "favorites"; Refresh(); }
    private void Recent_Click(object sender, RoutedEventArgs e) { _section = "recent"; SortOrder.SelectedIndex = 1; Refresh(); }

    private async void Detect_Click(object sender, RoutedEventArgs e) => await DetectAsync();
    private async void LocalImages_Click(object sender, RoutedEventArgs e) => await DetectAsync(imagesOnly: true);
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
            var dialog = CreateSettingsDialog(out var imagesToggle);
            if (_dialogCheckCompletion is not null && _dialogCheckReport is not null)
            {
                await DialogDiagnostics.CheckSettingsAsync(dialog, _dialogCheckReport);
                _dialogCheckCompletion.TrySetResult(true);
                return;
            }
            if (await dialog.ShowAsync() != ContentDialogResult.Primary
                || imagesToggle.IsOn == _settings.ImagesDisabled) return;
            var updated = _settings with { ImagesDisabled = imagesToggle.IsOn };
            await _settingsStore.SaveAsync(updated);
            _settings = updated;
            SetBusy(_busy);
            Refresh();
        }
        catch (Exception error)
        {
            _dialogCheckCompletion?.TrySetException(error);
            Notify("Não foi possível abrir ou salvar as configurações", error.Message, InfoBarSeverity.Error);
        }
        finally { _dialogOpen = false; }
    }

    private ContentDialog CreateSettingsDialog(out ToggleSwitch imagesToggle)
    {
        imagesToggle = new ToggleSwitch
        {
            Header = "Desativar imagens",
            IsOn = _settings.ImagesDisabled,
            OnContent = "Ativado",
            OffContent = "Desativado"
        };
        return new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Configurações",
            Content = imagesToggle,
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
