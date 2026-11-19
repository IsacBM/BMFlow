using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DockBar
{
    public sealed partial class DockWindow
    {
        private bool showMediaPlayer = true;
        private bool hideMediaWhenInactive = true;
        private MediaSessionService mediaSessionService;
        private Grid mediaPlayerHolder;
        private Border mediaPlayerCard;
        private Image mediaArtworkImage;
        private Image mediaSourceIcon;
        private TextBlock mediaFallbackGlyph;
        private TextBlock mediaTitleText;
        private TextBlock mediaPlayPauseGlyph;
        private Border mediaPreviousButton;
        private Border mediaPlayPauseButton;
        private Border mediaNextButton;

        internal bool CurrentShowMediaPlayer { get { return showMediaPlayer; } }
        internal bool CurrentHideMediaWhenInactive { get { return hideMediaWhenInactive; } }

        internal void SetShowMediaPlayer(bool show)
        {
            showMediaPlayer = show;
            if (show)
            {
                EnsureMediaPlayerService();
                ApplyMediaSnapshot(mediaSessionService == null ? MediaSessionSnapshot.Empty : mediaSessionService.Current);
            }
            else if (mediaPlayerHolder != null)
            {
                mediaPlayerHolder.Visibility = Visibility.Collapsed;
                waveLayoutDirty = true;
                SchedulePositionDock();
            }
            SaveSettings();
        }

        internal void SetHideMediaWhenInactive(bool hide)
        {
            hideMediaWhenInactive = hide;
            ApplyMediaSnapshot(mediaSessionService == null ? MediaSessionSnapshot.Empty : mediaSessionService.Current);
            SaveSettings();
        }

        private FrameworkElement BuildMediaPlayerCard()
        {
            mediaPlayerHolder = new Grid
            {
                Width = 164,
                Height = 63,
                ClipToBounds = false,
                Visibility = hideMediaWhenInactive ? Visibility.Collapsed : Visibility.Visible
            };
            mediaPlayerCard = new Border
            {
                Width = 158,
                Height = 48,
                Margin = new Thickness(3, 0, 3, 8),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(11),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Background = DefaultMediaBackground(),
                RenderTransformOrigin = new Point(.5, 1),
                RenderTransform = new ScaleTransform(1, 1),
                Opacity = 1,
                ClipToBounds = false
            };
            BuildMediaPlayerContents(false);
            mediaPlayerHolder.Children.Add(mediaPlayerCard);
            AddMagnify(mediaPlayerCard, 1.12);
            return mediaPlayerHolder;
        }

        private void InitializeMediaPlayer()
        {
            if (!showMediaPlayer)
            {
                if (mediaPlayerHolder != null) mediaPlayerHolder.Visibility = Visibility.Collapsed;
                return;
            }
            EnsureMediaPlayerService();
        }

        private void EnsureMediaPlayerService()
        {
            if (mediaSessionService != null) return;
            mediaSessionService = new MediaSessionService();
            mediaSessionService.SnapshotChanged += OnMediaSnapshotChanged;
            mediaSessionService.Start();
        }

        private void DisposeMediaPlayer()
        {
            if (mediaSessionService == null) return;
            mediaSessionService.SnapshotChanged -= OnMediaSnapshotChanged;
            mediaSessionService.Dispose();
            mediaSessionService = null;
        }

        private void OnMediaSnapshotChanged(object sender, EventArgs args)
        {
            MediaSessionSnapshot snapshot = mediaSessionService == null
                ? MediaSessionSnapshot.Empty
                : mediaSessionService.Current;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (!isClosing) ApplyMediaSnapshot(snapshot);
                }));
            }
            catch { }
        }

        private void ApplyMediaSnapshot(MediaSessionSnapshot snapshot)
        {
            if (mediaPlayerCard == null || mediaPlayerHolder == null) return;
            if (snapshot == null) snapshot = MediaSessionSnapshot.Empty;
            bool visible = showMediaPlayer && (snapshot.HasMedia || !hideMediaWhenInactive);
            Visibility nextVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
            bool visibilityChanged = mediaPlayerHolder.Visibility != nextVisibility;
            mediaPlayerHolder.Visibility = nextVisibility;
            if (!visible)
            {
                if (visibilityChanged)
                {
                    waveLayoutDirty = true;
                    SchedulePositionDock();
                }
                return;
            }

            mediaTitleText.Text = snapshot.HasMedia ? snapshot.Title : "Nenhuma mídia";
            mediaTitleText.ToolTip = snapshot.HasMedia
                ? snapshot.Title + (String.IsNullOrWhiteSpace(snapshot.Artist) ? String.Empty : " — " + snapshot.Artist)
                : "Abra uma música ou vídeo para começar";
            mediaPlayPauseGlyph.Text = snapshot.IsPlaying ? "\uE769" : "\uE768";
            SetMediaButtonEnabled(mediaPreviousButton, snapshot.HasMedia && snapshot.CanPrevious);
            SetMediaButtonEnabled(mediaPlayPauseButton, snapshot.HasMedia && snapshot.CanPlayPause);
            SetMediaButtonEnabled(mediaNextButton, snapshot.HasMedia && snapshot.CanNext);

            ImageSource artwork = DecodeMediaArtwork(snapshot.Artwork);
            mediaArtworkImage.Source = artwork;
            mediaArtworkImage.Visibility = artwork == null ? Visibility.Collapsed : Visibility.Visible;
            mediaFallbackGlyph.Visibility = artwork == null ? Visibility.Visible : Visibility.Collapsed;

            ImageSource sourceIcon = snapshot.HasMedia
                ? ResolveAppIcon(null, snapshot.SourceAppUserModelId)
                : null;
            mediaSourceIcon.Source = sourceIcon;
            mediaSourceIcon.Visibility = sourceIcon == null ? Visibility.Collapsed : Visibility.Visible;

            Color? accent = artwork == null ? null : ExtractAccentColor(artwork);
            mediaPlayerCard.Background = accent.HasValue
                ? MediaBackgroundFromAccent(accent.Value)
                : DefaultMediaBackground();
            mediaPlayerCard.BeginAnimation(OpacityProperty, new DoubleAnimation(.55, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            if (visibilityChanged)
            {
                waveLayoutDirty = true;
                SchedulePositionDock();
            }
        }

        private void UpdateMediaPlayerOrientation(bool vertical)
        {
            if (mediaPlayerCard == null || mediaPlayerHolder == null) return;
            mediaPlayerHolder.Width = vertical ? 58 : 164;
            mediaPlayerHolder.Height = vertical ? 136 : 63;
            mediaPlayerCard.Width = vertical ? 50 : 158;
            mediaPlayerCard.Height = vertical ? 130 : 48;
            mediaPlayerCard.Margin = vertical ? new Thickness(0, 3, 8, 3) : new Thickness(3, 0, 3, 8);
            BuildMediaPlayerContents(vertical);
            ApplyMediaSnapshot(mediaSessionService == null ? MediaSessionSnapshot.Empty : mediaSessionService.Current);
        }

        private void BuildMediaPlayerContents(bool vertical)
        {
            var root = new Grid();
            FrameworkElement artwork = BuildMediaArtwork(vertical);
            mediaTitleText = new TextBlock
            {
                Text = "Nenhuma mídia",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            mediaPreviousButton = MediaControlButton("\uE892", "Anterior", delegate { return RunMediaCommand(service => service.SkipPreviousAsync()); });
            mediaPlayPauseButton = MediaControlButton("\uE768", "Reproduzir ou pausar", delegate { return RunMediaCommand(service => service.TogglePlayPauseAsync()); });
            mediaPlayPauseGlyph = mediaPlayPauseButton.Child as TextBlock;
            mediaNextButton = MediaControlButton("\uE893", "Próxima", delegate { return RunMediaCommand(service => service.SkipNextAsync()); });

            if (vertical)
            {
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                artwork.Width = artwork.Height = 50;
                artwork.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetRow(artwork, 0);
                root.Children.Add(artwork);

                var controls = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                controls.Children.Add(mediaPreviousButton);
                controls.Children.Add(mediaPlayPauseButton);
                controls.Children.Add(mediaNextButton);
                Grid.SetRow(controls, 1);
                root.Children.Add(controls);
            }
            else
            {
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                artwork.Width = artwork.Height = 54;
                artwork.VerticalAlignment = VerticalAlignment.Center;
                Panel.SetZIndex(artwork, 2);
                Grid.SetColumn(artwork, 0);
                root.Children.Add(artwork);

                var body = new Grid { Margin = new Thickness(7, 2, 4, 2) };
                body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
                body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(mediaTitleText, 0);
                body.Children.Add(mediaTitleText);
                var controls = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                controls.Children.Add(mediaPreviousButton);
                controls.Children.Add(mediaPlayPauseButton);
                controls.Children.Add(mediaNextButton);
                Grid.SetRow(controls, 1);
                body.Children.Add(controls);
                Grid.SetColumn(body, 1);
                root.Children.Add(body);
            }
            mediaPlayerCard.Child = root;
        }

        private FrameworkElement BuildMediaArtwork(bool vertical)
        {
            var grid = new Grid();
            mediaFallbackGlyph = new TextBlock
            {
                Text = "\uE8D6",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 19,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            mediaArtworkImage = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
            mediaSourceIcon = new Image
            {
                Width = 13,
                Height = 13,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 2, 2),
                Visibility = Visibility.Collapsed
            };
            grid.Children.Add(mediaFallbackGlyph);
            grid.Children.Add(mediaArtworkImage);
            grid.Children.Add(mediaSourceIcon);
            return new Border
            {
                CornerRadius = vertical
                    ? new CornerRadius(12, 12, 0, 0)
                    : new CornerRadius(10),
                ClipToBounds = true,
                Background = new LinearGradientBrush(Color.FromRgb(91, 35, 122), Color.FromRgb(21, 34, 87), 45),
                Effect = vertical ? null : new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 7,
                    ShadowDepth = 1,
                    Opacity = .38
                },
                Child = grid
            };
        }

        private Border MediaControlButton(string glyph, string tooltip, Func<Task> action)
        {
            var button = new Border
            {
                Width = 24,
                Height = 18,
                CornerRadius = new CornerRadius(7),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 11,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            button.MouseEnter += delegate { if (button.IsHitTestVisible) button.Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)); };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            button.MouseLeftButtonUp += async delegate
            {
                if (!button.IsHitTestVisible) return;
                await action();
            };
            return button;
        }

        private Task RunMediaCommand(Func<MediaSessionService, Task> command)
        {
            MediaSessionService service = mediaSessionService;
            return service == null ? Task.FromResult(0) : command(service);
        }

        private static void SetMediaButtonEnabled(Border button, bool enabled)
        {
            if (button == null) return;
            button.IsHitTestVisible = enabled;
            button.Opacity = enabled ? 1 : .32;
            button.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
        }

        private static ImageSource DecodeMediaArtwork(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    image.StreamSource = stream;
                    image.DecodePixelWidth = 128;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch { return null; }
        }

        private static Brush DefaultMediaBackground()
        {
            return new LinearGradientBrush(Color.FromRgb(34, 20, 78), Color.FromRgb(13, 18, 48), 15);
        }

        private static Brush MediaBackgroundFromAccent(Color accent)
        {
            int maximum = Math.Max(accent.R, Math.Max(accent.G, accent.B));
            int minimum = Math.Min(accent.R, Math.Min(accent.G, accent.B));
            bool neutral = maximum - minimum < 24;
            Color first;
            if (neutral)
            {
                int luminance = (accent.R * 299 + accent.G * 587 + accent.B * 114) / 1000;
                byte gray = (byte)Math.Max(32, Math.Min(82, 24 + luminance * .26));
                first = Color.FromRgb(gray, gray, gray);
            }
            else
            {
                double factor = maximum > 190 ? .52 : maximum > 120 ? .64 : maximum > 70 ? .82 : 1.08;
                first = Color.FromRgb(
                    (byte)Math.Max(10, Math.Min(132, accent.R * factor)),
                    (byte)Math.Max(10, Math.Min(132, accent.G * factor)),
                    (byte)Math.Max(10, Math.Min(132, accent.B * factor)));
            }
            Color second = Color.FromRgb(
                (byte)Math.Max(8, first.R * .38),
                (byte)Math.Max(8, first.G * .38),
                (byte)Math.Max(8, first.B * .38));
            return new LinearGradientBrush(first, second, 15);
        }
    }
}
