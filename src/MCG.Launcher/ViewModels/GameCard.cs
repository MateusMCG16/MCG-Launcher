using System.ComponentModel;
using MCG.Launcher.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace MCG.Launcher.ViewModels;

public sealed class GameCard : INotifyPropertyChanged
{
    public GameEntry Entry { get; }
    private ImageSource? _cover;
    private CoverPresentation _presentation;
    private bool _iconAttempted;
    public event PropertyChangedEventHandler? PropertyChanged;
    public GameCard(GameEntry entry, bool imagesDisabled = false)
    {
        Entry = entry;
        _presentation = entry.CoverPresentation;
        if (imagesDisabled) return;
        if (!string.IsNullOrWhiteSpace(entry.CoverPath) && Path.IsPathFullyQualified(entry.CoverPath) && File.Exists(entry.CoverPath))
        {
            var bitmap = new BitmapImage(new Uri(entry.CoverPath)) { DecodePixelWidth = 512 };
            bitmap.ImageFailed += (_, _) =>
            {
                if (ReferenceEquals(_cover, bitmap))
                {
                    SetCover(null, CoverPresentation.Icon);
                    _ = LoadIconAsync();
                }
            };
            _cover = bitmap;
        }
        if (_cover is null || _presentation == CoverPresentation.Icon) _ = LoadIconAsync();
    }
    public string Name => Entry.Name;
    public string Platform => Entry.Source switch { GameSource.Steam => "Steam", GameSource.Epic => "Epic Games", _ => "Adicionado por você" };
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[..1])).ToUpperInvariant();
    public string FavoriteGlyph => Entry.IsFavorite ? "\uE735" : "\uE734";
    public string FavoriteLabel => Entry.IsFavorite ? "Remover dos favoritos" : "Adicionar aos favoritos";
    public string PlayLabel => $"Jogar {Name}";
    public ImageSource? Cover => _cover;
    public Stretch CoverStretch => _presentation == CoverPresentation.Artwork ? Stretch.UniformToFill : Stretch.Uniform;
    public double CoverWidth => _presentation switch { CoverPresentation.Icon => 64, CoverPresentation.Logo => 210, _ => 240 };
    public double CoverHeight => _presentation switch { CoverPresentation.Icon => 64, CoverPresentation.Logo => 104, _ => 142 };
    public Visibility InitialsVisibility => Cover is null ? Visibility.Visible : Visibility.Collapsed;
    public Brush ArtworkBrush => new SolidColorBrush(Entry.Source switch
    {
        GameSource.Steam => ColorHelper.FromArgb(255, 35, 61, 82),
        GameSource.Epic => ColorHelper.FromArgb(255, 67, 45, 83),
        _ => ColorHelper.FromArgb(255, 39, 65, 47)
    });
    public string LastPlayed => Entry.LastPlayedAt is { } played ? $"Última abertura: {played.ToLocalTime():dd/MM HH:mm}" : "Pronto para jogar";

    private async Task LoadIconAsync()
    {
        if (_iconAttempted) return;
        _iconAttempted = true;
        var path = !string.IsNullOrWhiteSpace(Entry.IconPath) ? Entry.IconPath
            : Entry.Source == GameSource.Manual ? Entry.Target : "";
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 128);
            if (thumbnail is null || thumbnail.Size == 0) return;
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            SetCover(bitmap, CoverPresentation.Icon);
        }
        catch (Exception) { /* Missing or unreadable icons keep the cached image or initials. */ }
    }

    private void SetCover(ImageSource? image, CoverPresentation presentation)
    {
        _cover = image;
        _presentation = presentation;
        foreach (var property in new[] { nameof(Cover), nameof(InitialsVisibility), nameof(CoverStretch), nameof(CoverWidth), nameof(CoverHeight) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
