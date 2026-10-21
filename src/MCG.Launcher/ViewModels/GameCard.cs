using System.ComponentModel;
using MCG.Launcher.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace MCG.Launcher.ViewModels;

// Every card is 200 × 290. A vertical (2:3) image fills it; anything else becomes a composite cover:
// the same image blurred across the card as a backdrop, with the original centered on top.
public sealed class GameCard : INotifyPropertyChanged
{
    private const double CardWidth = 200, CardHeight = 290;
    // A tiny decode stretched across the card reads as a soft blur without an extra effect pipeline.
    private const int BackdropDecodeWidth = 20;
    public GameEntry Entry { get; }
    private ImageSource? _cover;
    private ImageSource? _backdrop;
    private CoverPresentation _presentation;
    private double? _aspect;
    private bool _iconAttempted;
    public event PropertyChangedEventHandler? PropertyChanged;
    public GameCard(GameEntry entry, bool imagesDisabled = false)
    {
        Entry = entry;
        _presentation = entry.CoverPresentation;
        if (imagesDisabled) return;
        if (!string.IsNullOrWhiteSpace(entry.CoverPath) && Path.IsPathFullyQualified(entry.CoverPath) && File.Exists(entry.CoverPath))
        {
            var uri = new Uri(entry.CoverPath);
            var bitmap = new BitmapImage(uri) { DecodePixelWidth = 512 };
            bitmap.ImageOpened += (_, _) =>
            {
                if (!ReferenceEquals(_cover, bitmap) || bitmap.PixelHeight == 0) return;
                _aspect = (double)bitmap.PixelWidth / bitmap.PixelHeight;
                Notify();
            };
            bitmap.ImageFailed += (_, _) =>
            {
                if (ReferenceEquals(_cover, bitmap))
                {
                    SetCover(null, null, CoverPresentation.Icon);
                    _ = LoadIconAsync();
                }
            };
            _cover = bitmap;
            _backdrop = new BitmapImage(uri) { DecodePixelWidth = BackdropDecodeWidth };
        }
        if (_cover is null || _presentation == CoverPresentation.Icon) _ = LoadIconAsync();
    }
    public string Name => Entry.Name;
    public string Platform => Entry.Source switch { GameSource.Steam => "Steam", GameSource.Epic => "Epic Games", _ => "Adicionado por você" };
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[..1])).ToUpperInvariant();
    public string FavoriteGlyph => Entry.IsFavorite ? "" : "";
    public string FavoriteLabel => Entry.IsFavorite ? "Remover dos favoritos" : "Adicionar aos favoritos";
    public string PlayLabel => $"Jogar {Name}";
    public ImageSource? Cover => _cover;
    public ImageSource? Backdrop => _backdrop;

    private bool Fills => _presentation switch
    {
        // Trust the declared format until the real pixel size arrives, then trust the pixels.
        CoverPresentation.Poster => _aspect is null or (>= 0.55 and <= 0.8),
        CoverPresentation.Artwork or CoverPresentation.Custom => _aspect is >= 0.55 and <= 0.8,
        _ => false
    };
    public Stretch CoverStretch => Fills ? Stretch.UniformToFill : Stretch.Uniform;
    public double CoverWidth => Fills ? CardWidth : _presentation switch { CoverPresentation.Icon => 76, CoverPresentation.Logo => 168, _ => 176 };
    public double CoverHeight => Fills ? CardHeight : _presentation switch { CoverPresentation.Icon => 76, CoverPresentation.Logo => 110, _ => 150 };
    public Thickness CoverMargin => Fills ? new Thickness(0) : new Thickness(0, 0, 0, 96);
    public Visibility BackdropVisibility => !Fills && _backdrop is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InitialsVisibility => Cover is null ? Visibility.Visible : Visibility.Collapsed;
    public Brush ArtworkBrush => new SolidColorBrush(Entry.Source switch
    {
        GameSource.Steam => ColorHelper.FromArgb(255, 0x14, 0x21, 0x3D),
        GameSource.Epic => ColorHelper.FromArgb(255, 0x1C, 0x2C, 0x4F),
        _ => ColorHelper.FromArgb(255, 0x0E, 0x18, 0x2E)
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
            thumbnail.Seek(0);
            var backdrop = new BitmapImage { DecodePixelWidth = BackdropDecodeWidth };
            await backdrop.SetSourceAsync(thumbnail);
            SetCover(bitmap, backdrop, CoverPresentation.Icon);
        }
        catch (Exception) { /* Missing or unreadable icons keep the cached image or initials. */ }
    }

    private void SetCover(ImageSource? image, ImageSource? backdrop, CoverPresentation presentation)
    {
        _cover = image;
        _backdrop = backdrop;
        _presentation = presentation;
        _aspect = null;
        Notify();
    }

    private void Notify()
    {
        foreach (var property in new[] { nameof(Cover), nameof(Backdrop), nameof(BackdropVisibility), nameof(InitialsVisibility),
            nameof(CoverStretch), nameof(CoverWidth), nameof(CoverHeight), nameof(CoverMargin) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
