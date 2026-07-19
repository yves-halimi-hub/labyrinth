using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using EFYVLabyMake.App.State;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;

namespace EFYVLabyMake.App.UI
{
    // Preview panel (item #3.4): PreviewController-driven playback on a UI timer
    // (honouring per-frame durations and ping-pong via the controller), a
    // debounced re-load when the project changes while stopped, and a disabled
    // Play with the structural-validation reason when the animation cannot play.
    public sealed class PreviewPanel : UserControl
    {
        private readonly EditorShell shell;
        private readonly DispatcherTimer playTimer;
        private readonly DispatcherTimer reloadTimer;
        private readonly Image previewImage;
        private readonly TextBlock statusLabel;
        private readonly TextBlock reasonLabel;
        private readonly Button playButton;
        private readonly Slider seekSlider;

        private PreviewController wiredPreview;
        private WriteableBitmap bitmap;
        private PixelColor[] frameBuffer;
        private int bufferWidth;
        private int bufferHeight;
        private DateTime lastTickAt;
        private bool suppressSeek;
        private bool loaded;

        public PreviewPanel(EditorShell editorShell)
        {
            shell = editorShell ?? throw new ArgumentNullException(nameof(editorShell));

            previewImage = new Image
            {
                Stretch = Stretch.Uniform,
                Width = AppDefaults.PreviewBoxSize,
                Height = AppDefaults.PreviewBoxSize
            };
            RenderOptions.SetBitmapInterpolationMode(previewImage, BitmapInterpolationMode.None);

            statusLabel = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
            reasonLabel = new TextBlock
            {
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                IsVisible = false
            };

            playButton = MakeButton("Play", OnPlay);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            controls.Children.Add(playButton);
            controls.Children.Add(MakeButton("Pause", () => shell.Preview?.Pause()));
            controls.Children.Add(MakeButton("Stop", () => shell.Preview?.Stop()));

            seekSlider = new Slider { Minimum = 0, Maximum = 0, SmallChange = 1, LargeChange = 1 };
            seekSlider.PropertyChanged += (sender, args) =>
            {
                if (args.Property != Slider.ValueProperty || suppressSeek) return;
                PreviewController preview = shell.Preview;
                if (preview == null) return;
                int index = (int)System.Math.Round(seekSlider.Value);
                PreviewStateSnapshot snapshot = preview.Current;
                if (snapshot.State == PreviewPlaybackState.Empty ||
                    index < 0 || index >= snapshot.FrameCount) return;
                try { preview.SeekFrame(index); }
                catch (ArgumentOutOfRangeException) { }
            };

            var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            root.Children.Add(new Border
            {
                Child = previewImage,
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            root.Children.Add(controls);
            root.Children.Add(seekSlider);
            root.Children.Add(statusLabel);
            root.Children.Add(reasonLabel);
            Content = root;

            playTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppDefaults.PreviewTimerMilliseconds)
            };
            playTimer.Tick += OnPlayTick;
            reloadTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppDefaults.PreviewReloadDebounceMilliseconds)
            };
            reloadTimer.Tick += OnReloadTick;

            shell.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(EditorShell.HasSession)) OnSessionChanged();
                else if (args.PropertyName == nameof(EditorShell.Problems)) ScheduleReload();
            };
            OnSessionChanged();
        }

        private void OnSessionChanged()
        {
            if (wiredPreview != null) wiredPreview.StateChanged -= OnPreviewState;
            wiredPreview = shell.Preview;
            if (wiredPreview != null) wiredPreview.StateChanged += OnPreviewState;
            loaded = false;
            Reload();
        }

        // Debounce a reload while stopped/paused; leave a running preview alone
        // so idle editing does not interrupt playback (Stop+Play refreshes it).
        private void ScheduleReload()
        {
            if (shell.Preview == null) return;
            if (shell.Preview.Current.State == PreviewPlaybackState.Playing) return;
            reloadTimer.Stop();
            reloadTimer.Start();
        }

        private void OnReloadTick(object sender, EventArgs e)
        {
            reloadTimer.Stop();
            if (shell.Preview != null &&
                shell.Preview.Current.State == PreviewPlaybackState.Playing) return;
            Reload();
        }

        private void Reload()
        {
            if (!shell.HasSession)
            {
                loaded = false;
                playButton.IsEnabled = false;
                reasonLabel.IsVisible = false;
                statusLabel.Text = "No project open.";
                previewImage.Source = null;
                return;
            }

            string reason;
            loaded = shell.TryLoadPreview(out reason);
            playButton.IsEnabled = loaded;
            reasonLabel.Text = reason;
            reasonLabel.IsVisible = !loaded;
            Render();
        }

        private void OnPlay()
        {
            if (!loaded) Reload();
            PreviewController preview = shell.Preview;
            if (preview == null || !loaded) return;
            lastTickAt = DateTime.UtcNow;
            preview.Play();
            playTimer.Start();
        }

        private void OnPlayTick(object sender, EventArgs e)
        {
            PreviewController preview = shell.Preview;
            if (preview == null)
            {
                playTimer.Stop();
                return;
            }
            DateTime now = DateTime.UtcNow;
            TimeSpan elapsed = now - lastTickAt;
            lastTickAt = now;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            preview.Tick(elapsed);
        }

        private void OnPreviewState(PreviewStateSnapshot snapshot)
        {
            if (snapshot.State != PreviewPlaybackState.Playing) playTimer.Stop();
            Render();
        }

        private void Render()
        {
            if (!shell.HasSession)
            {
                previewImage.Source = null;
                return;
            }
            PreviewController preview = shell.Preview;
            if (preview == null) return;

            PreviewStateSnapshot snapshot = preview.Current;
            statusLabel.Text = PreviewStatusFormatter.FormatStatus(snapshot);

            suppressSeek = true;
            try
            {
                seekSlider.Maximum = System.Math.Max(0, snapshot.FrameCount - 1);
                seekSlider.Value = snapshot.FrameIndex < 0 ? 0 : snapshot.FrameIndex;
                seekSlider.IsEnabled = snapshot.FrameCount > 1;
            }
            finally
            {
                suppressSeek = false;
            }

            if (snapshot.State == PreviewPlaybackState.Empty)
            {
                previewImage.Source = null;
                return;
            }

            int width = shell.Session.Project.CanvasWidth;
            int height = shell.Session.Project.CanvasHeight;
            if (width <= 0 || height <= 0) return;
            EnsureBuffers(width, height);
            try
            {
                preview.CopyCurrentPixelsTo(frameBuffer);
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException)
            {
                // A canvas resize since the last load desynced the buffer; a
                // reload re-captures at the new size.
                Reload();
                return;
            }
            WriteFrame();
            previewImage.Source = bitmap;
            previewImage.InvalidateVisual();
        }

        private void EnsureBuffers(int width, int height)
        {
            if (bitmap != null && bufferWidth == width && bufferHeight == height) return;
            bufferWidth = width;
            bufferHeight = height;
            frameBuffer = new PixelColor[width * height];
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Bgra8888,
                AlphaFormat.Unpremul);
        }

        private unsafe void WriteFrame()
        {
            using (ILockedFramebuffer buffer = bitmap.Lock())
            {
                uint* destination = (uint*)buffer.Address;
                int rowPixels = buffer.RowBytes / sizeof(uint);
                for (int y = 0; y < bufferHeight; y++)
                {
                    for (int x = 0; x < bufferWidth; x++)
                    {
                        uint rgba = frameBuffer[y * bufferWidth + x].Rgba;
                        byte red = (byte)rgba;
                        byte green = (byte)(rgba >> 8);
                        byte blue = (byte)(rgba >> 16);
                        byte alpha = (byte)(rgba >> 24);
                        destination[y * rowPixels + x] =
                            blue | ((uint)green << 8) | ((uint)red << 16) | ((uint)alpha << 24);
                    }
                }
            }
        }

        private static Button MakeButton(string caption, Action onClick)
        {
            var button = new Button { Content = caption };
            button.Click += (sender, args) => onClick();
            return button;
        }
    }
}
