using MCG.Launcher.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace MCG.Launcher;

internal sealed class GameEditorDialog : ContentDialog
{
    private readonly Window _owner;
    private readonly GameEntry? _original;
    private readonly TextBox _name = new() { Header = "Nome do jogo", PlaceholderText = "Como o jogo aparece na biblioteca", MaxLength = 120 };
    private readonly TextBox _target = new() { Header = "Executável ou atalho", PlaceholderText = @"C:\Jogos\MeuJogo\jogo.exe" };
    private readonly TextBox _arguments = new() { Header = "Argumentos (opcional)", PlaceholderText = "Parâmetros usados ao iniciar o jogo", MaxLength = 2048 };
    private readonly TextBox _workingDirectory = new() { Header = "Pasta de trabalho (opcional)", PlaceholderText = "Por padrão, a pasta do executável" };
    private readonly TextBox _cover = new() { Header = "Capa local (opcional)", PlaceholderText = "Imagem JPG, PNG, BMP ou WebP" };
    private readonly InfoBar _error = new() { Severity = InfoBarSeverity.Error, IsClosable = false };
    private readonly Button _browseTarget = new() { Content = "Escolher arquivo", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _browseCover = new() { Content = "Escolher capa" };
    public GameEntry? Result { get; private set; }

    public GameEditorDialog(Window owner, GameEntry? original)
    {
        _owner = owner;
        _original = original;
        Title = original is null ? "Adicionar jogo" : "Editar jogo";
        PrimaryButtonText = "Salvar";
        CloseButtonText = "Cancelar";
        DefaultButton = ContentDialogButton.Primary;
        var imported = original is { Source: not GameSource.Manual };
        _name.Text = original?.Name ?? "";
        _target.Text = original?.Target ?? "";
        _target.IsReadOnly = imported;
        _arguments.Text = original?.Arguments ?? "";
        _workingDirectory.Text = original?.WorkingDirectory ?? "";
        _cover.Text = original?.CoverPath ?? "";
        _browseTarget.Visibility = imported ? Visibility.Collapsed : Visibility.Visible;
        _arguments.Visibility = imported ? Visibility.Collapsed : Visibility.Visible;
        _workingDirectory.Visibility = imported ? Visibility.Collapsed : Visibility.Visible;
        if (imported) _target.Header = "Endereço da plataforma";
        var stack = new StackPanel { Spacing = 12, MinWidth = 380, MaxWidth = 460 };
        stack.Children.Add(new TextBlock
        {
            Text = imported ? "O jogo continua usando seu cliente Steam ou Epic para abrir."
                : "Escolha um executável ou atalho de um jogo instalado no PC.",
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(_error);
        stack.Children.Add(_name);
        stack.Children.Add(_target);
        stack.Children.Add(_browseTarget);
        stack.Children.Add(_arguments);
        stack.Children.Add(_workingDirectory);
        stack.Children.Add(_cover);
        var coverButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        coverButtons.Children.Add(_browseCover);
        var clearCover = new Button { Content = "Sem capa" };
        clearCover.Click += (_, _) => _cover.Text = "";
        coverButtons.Children.Add(clearCover);
        stack.Children.Add(coverButtons);
        Content = new ScrollViewer { Content = stack, MaxHeight = 550, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _browseTarget.Click += BrowseTarget_Click;
        _browseCover.Click += BrowseCover_Click;
        PrimaryButtonClick += Save_Click;
    }

    private async void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        await PickAsync([".exe", ".lnk"], path =>
        {
            _target.Text = path;
            if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = Path.GetFileNameWithoutExtension(path);
            _workingDirectory.Text = Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                ? "" : Path.GetDirectoryName(path) ?? "";
        });
    }
    private async void BrowseCover_Click(object sender, RoutedEventArgs e)
        => await PickAsync([".jpg", ".jpeg", ".png", ".bmp", ".webp"], path => _cover.Text = path);

    private async Task PickAsync(string[] extensions, Action<string> apply)
    {
        IsPrimaryButtonEnabled = false;
        _browseTarget.IsEnabled = false;
        _browseCover.IsEnabled = false;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_owner));
            foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
            if (await picker.PickSingleFileAsync() is { } file) apply(file.Path);
        }
        catch (Exception error) { ShowError(error.Message); }
        finally
        {
            IsPrimaryButtonEnabled = true;
            _browseTarget.IsEnabled = true;
            _browseCover.IsEnabled = true;
        }
    }

    private void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            var name = _name.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Informe o nome do jogo.");
            var imported = _original is { Source: not GameSource.Manual };
            var target = imported ? _original!.Target : NormalizePath(_target.Text);
            var cover = NormalizePath(_cover.Text);
            if (cover.Length > 0 && (!Path.IsPathFullyQualified(cover) || !File.Exists(cover)
                || !new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" }.Contains(Path.GetExtension(cover), StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Escolha uma imagem local existente para a capa.");
            var game = (_original ?? new GameEntry()) with
            {
                Name = name, Target = target, CoverPath = cover,
                CoverPresentation = string.Equals(cover, _original?.CoverPath, StringComparison.OrdinalIgnoreCase)
                    ? _original!.CoverPresentation : cover.Length > 0 ? CoverPresentation.Custom : CoverPresentation.Artwork,
                IconPath = imported ? _original!.IconPath : target,
                LaunchKind = imported ? _original!.LaunchKind
                    : Path.GetExtension(target).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? LaunchKind.Shortcut : LaunchKind.Executable,
                Arguments = imported ? _original!.Arguments : _arguments.Text.Trim(),
                WorkingDirectory = imported ? _original!.WorkingDirectory : NormalizePath(_workingDirectory.Text)
            };
            _ = GameLauncher.CreateStartInfo(game);
            Result = game;
        }
        catch (Exception error)
        {
            args.Cancel = true;
            ShowError(error.Message);
        }
    }
    private static string NormalizePath(string path) => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
    private void ShowError(string message) { _error.Title = "Confira o cadastro"; _error.Message = message; _error.IsOpen = true; }
}
