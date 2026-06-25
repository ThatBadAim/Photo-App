using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;

namespace SocialPrepTool;

/// <summary>
/// The main content page displayed inside the application window.
/// Add your UI logic, event handlers, and data binding here.
/// </summary>
public sealed partial class MainPage : Page
{
    public ViewModels.MainViewModel ViewModel { get; } = new ViewModels.MainViewModel();

    public MainPage()
    {
        this.InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        this.Loaded += MainPage_Loaded;
        this.Unloaded += MainPage_Unloaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadPresetsCommand.ExecuteAsync(null);
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        this.Loaded -= MainPage_Loaded;
        this.Unloaded -= MainPage_Unloaded;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        if (_currentSubscribedRecord != null)
        {
            _currentSubscribedRecord.PropertyChanged -= Record_PropertyChanged;
            _currentSubscribedRecord = null;
        }
        _currentBitmap?.Dispose();
        _currentBitmap = null;
        _currentBitmapPath = null;
    }

    private void QueuePanel_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void QueuePanel_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items.OfType<StorageFile>())
            {
                string path = item.Path;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".heic")
                {
                    string baselinePath = await NormalizeImageAsync(path);
                    ViewModel.AddFileToQueueCommand.Execute(baselinePath);
                }
            }
        }
    }

    private async void ImportPhotos_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".heic");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
        {
            string baselinePath = await NormalizeImageAsync(file.Path);
            ViewModel.AddFileToQueueCommand.Execute(baselinePath);
        }
    }

    private void ApplyToAll_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyToAllCommand.Execute(null);
    }

    private async void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        var itemsToExport = ViewModel.QueueItems.Where(x => x.IsChecked).ToList();
        if (itemsToExport.Count == 0 && ViewModel.SelectedQueueItem != null)
        {
            itemsToExport.Add(ViewModel.SelectedQueueItem);
        }

        if (itemsToExport.Count == 0) return;

        var folderPicker = new Windows.Storage.Pickers.FolderPicker();
        folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        folderPicker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            var exportService = new Services.ExportService();
            foreach(var item in itemsToExport)
            {
                item.Status = ViewModels.QueueItemStatus.Processing;
                try
                {
                    await exportService.ExportJobAsync(item.Record, folder.Path, item.FileName);
                    item.Status = ViewModels.QueueItemStatus.Done;
                }
                catch (Exception ex)
                {
                    item.Status = ViewModels.QueueItemStatus.Error;
                    item.ErrorMessage = ex.Message;
                }
            }
        }
    }

    private async void NewPreset_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedQueueItem == null) return;

        int nextIndex = 1;
        if (ViewModel.PresetContainer != null && ViewModel.PresetContainer.Presets != null)
        {
            nextIndex = ViewModel.PresetContainer.Presets.Count + 1;
        }

        try
        {
            var preset = new Models.Preset
            {
                Name = "Preset " + nextIndex,
                TargetRatio = ViewModel.SelectedQueueItem.Record.TargetRatio,
                BackgroundMode = ViewModel.SelectedQueueItem.Record.BackgroundMode,
                BackgroundColor = ViewModel.SelectedQueueItem.Record.BackgroundColor,
                BlurStrength = ViewModel.SelectedQueueItem.Record.BlurStrength,
                DropShadow = ViewModel.SelectedQueueItem.Record.DropShadow
            };
            await ViewModel.SavePresetCommand.ExecuteAsync(preset);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding new preset: {ex}");
        }
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button btn && btn.Tag is Models.Preset preset)
        {
            ViewModel.ApplyPresetCommand.Execute(preset);
        }
    }

    private async void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button btn && btn.Tag is Models.Preset preset)
        {
            await ViewModel.DeletePresetCommand.ExecuteAsync(preset);
        }
    }

    private async System.Threading.Tasks.Task<string> NormalizeImageAsync(string sourcePath)
    {
        // Normalize EXIF orientation to baseline
        string tempDir = Path.Combine(Path.GetTempPath(), "SocialPrepTool", "BaselineCache");
        Directory.CreateDirectory(tempDir);

        string ext = Path.GetExtension(sourcePath);
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string targetExt = ext.ToLowerInvariant() == ".heic" ? ".jpg" : ext;

        string newPath = Path.Combine(tempDir, baseName + targetExt);
        int counter = 1;
        while (File.Exists(newPath))
        {
            newPath = Path.Combine(tempDir, $"{baseName}_{counter}{targetExt}");
            counter++;
        }

        await System.Threading.Tasks.Task.Run(() =>
        {
            using var image = SixLabors.ImageSharp.Image.Load(sourcePath);
            image.Mutate(x => x.AutoOrient());
            // For HEIC we'd need a codec or we convert to JPEG for the working file
            // If it's HEIC, ImageSharp might fail if no codec. Let's save as JPG for internal baseline
            if (ext.ToLowerInvariant() == ".heic")
            {
                image.SaveAsJpeg(newPath);
            }
            else
            {
                image.Save(newPath);
            }
        });

            return newPath;
        }

        private SKBitmap? _currentBitmap;
        private string? _currentBitmapPath;
        private Models.EditRecord? _currentSubscribedRecord;
        private bool _isLoadingBitmap = false;

        // Pointer tracking for Carousel dragging
        private bool _isDragging = false;
        private Windows.Foundation.Point _lastPointerPosition;

        private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.MainViewModel.SelectedQueueItem))
            {
                if (_currentSubscribedRecord != null)
                {
                    _currentSubscribedRecord.PropertyChanged -= Record_PropertyChanged;
                }

                var item = ViewModel.SelectedQueueItem;
                if (item?.Record != null)
                {
                    _currentSubscribedRecord = item.Record;
                    _currentSubscribedRecord.PropertyChanged += Record_PropertyChanged;
                    await LoadPreviewBitmapAsync(item.Record.SourceFilePath);
                }
                else
                {
                    _currentSubscribedRecord = null;
                    _currentBitmap?.Dispose();
                    _currentBitmap = null;
                    _currentBitmapPath = null;
                }

                PreviewCanvas.Invalidate();
            }
        }

        private async Task LoadPreviewBitmapAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                _currentBitmap?.Dispose();
                _currentBitmap = null;
                _currentBitmapPath = null;
                PreviewCanvas.Invalidate();
                return;
            }

            if (_currentBitmapPath == filePath)
                return;

            _isLoadingBitmap = true;

            try
            {
                var newBitmap = await Task.Run(() =>
                {
                    using var codec = SkiaSharp.SKCodec.Create(filePath);
                    if (codec == null) return null;

                    var info = codec.Info;
                    int maxDimension = 2048;

                    if (info.Width <= maxDimension && info.Height <= maxDimension)
                    {
                        return SkiaSharp.SKBitmap.Decode(filePath);
                    }

                    float scale = Math.Min((float)maxDimension / info.Width, (float)maxDimension / info.Height);
                    var targetInfo = new SkiaSharp.SKImageInfo((int)(info.Width * scale), (int)(info.Height * scale));

                    var bitmap = SkiaSharp.SKBitmap.Decode(filePath);
                    if (bitmap == null) return null;

                    var resized = bitmap.Resize(targetInfo, SkiaSharp.SKFilterQuality.Medium);
                    bitmap.Dispose();
                    return resized;
                });

                // Check if the user navigated away while we were loading
                if (ViewModel.SelectedQueueItem?.Record?.SourceFilePath == filePath)
                {
                    _currentBitmap?.Dispose();
                    _currentBitmap = newBitmap;
                    _currentBitmapPath = filePath;
                }
                else
                {
                    newBitmap?.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error decoding preview bitmap: {ex}");
                _currentBitmap?.Dispose();
                _currentBitmap = null;
                _currentBitmapPath = null;
            }
            finally
            {
                _isLoadingBitmap = false;
                DispatcherQueue.TryEnqueue(() => PreviewCanvas.Invalidate());
            }
        }

        private void Record_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            PreviewCanvas.Invalidate();
        }

        private void PreviewCanvas_PaintSurface(object sender, SkiaSharp.Views.Windows.SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SkiaSharp.SKColors.Transparent);

            var item = ViewModel.SelectedQueueItem;
            if (item == null || string.IsNullOrEmpty(item?.Record?.SourceFilePath))
            {
                return;
            }

            var record = item.Record;

            if (_isLoadingBitmap)
            {
                // Optionally draw a loading indicator here
                return;
            }

            if (_currentBitmap == null || _currentBitmapPath != record.SourceFilePath)
            {
                // Ensure the bitmap is loaded if we somehow missed the property change
                if (!_isLoadingBitmap)
                {
                    _ = LoadPreviewBitmapAsync(record.SourceFilePath);
                }
                return;
            }

            // Handle manual rotation dimensions
            int bmpWidth = _currentBitmap.Width;
            int bmpHeight = _currentBitmap.Height;
            if (record.ManualRotationDegrees == 90 || record.ManualRotationDegrees == 270)
            {
                bmpWidth = _currentBitmap.Height;
                bmpHeight = _currentBitmap.Width;
            }

            int targetWidth, targetHeight;
            if (record.TargetRatio == "1:1") { targetWidth = 1080; targetHeight = 1080; }
            else if (record.TargetRatio == "4:5") { targetWidth = 1080; targetHeight = 1350; }
            else if (record.TargetRatio == "9:16") { targetWidth = 1080; targetHeight = 1920; }
            else { targetWidth = 1080; targetHeight = 608; } // 16:9

            int fullCanvasWidth = targetWidth;
            if (record.IsCarousel)
            {
                fullCanvasWidth = targetWidth * record.CarouselSlideCount;
            }

            // Calculate scale to fit canvas
            float scale = Math.Min((float)e.Info.Width / fullCanvasWidth, (float)e.Info.Height / targetHeight);

            DispatcherQueue.TryEnqueue(() =>
            {
                ImageInfoText.Text = $"{fullCanvasWidth} × {targetHeight}";
                ZoomInfoText.Text = $"{(int)(scale * 100)}%";
            });

            float drawWidth = fullCanvasWidth * scale;
            float drawHeight = targetHeight * scale;
            float dx = (e.Info.Width - drawWidth) / 2;
            float dy = (e.Info.Height - drawHeight) / 2;

            canvas.Save();
            canvas.Translate(dx, dy);
            canvas.Scale(scale);

            // Clip to target ratio bounds
            var targetRect = new SKRect(0, 0, fullCanvasWidth, targetHeight);
            canvas.ClipRect(targetRect);

            // Checkerboard
            float checkerSize = 20;
            using (var checkerPaint = new SKPaint { Color = new SKColor(230, 230, 230) })
            {
                canvas.Clear(SKColors.White);
                for (float y = 0; y < targetHeight; y += checkerSize)
                {
                    for (float x = 0; x < fullCanvasWidth; x += checkerSize)
                    {
                        if (((int)(x / checkerSize) + (int)(y / checkerSize)) % 2 == 0)
                        {
                            canvas.DrawRect(x, y, checkerSize, checkerSize, checkerPaint);
                        }
                    }
                }
            }

            // Background
            if (record.IsCarousel)
            {
                canvas.Clear(SKColors.Transparent);
            }
            else if (record.BackgroundMode == "solid")
            {
                if (SKColor.TryParse(record.BackgroundColor, out var color))
                {
                    using var bgPaint = new SKPaint { Color = color };
                    canvas.DrawRect(targetRect, bgPaint);
                }
                else
                {
                    using var bgPaint = new SKPaint { Color = SKColors.Black };
                    canvas.DrawRect(targetRect, bgPaint);
                }
            }
            else
            {
                // Blur background
                // Scale image to cover using logically rotated dimensions
                float bgScale = Math.Max((float)targetWidth / bmpWidth, (float)targetHeight / bmpHeight);
                float bgW = bmpWidth * bgScale;
                float bgH = bmpHeight * bgScale;

                float centerX = targetWidth / 2f;
                float centerY = targetHeight / 2f;

                float unrotatedW = _currentBitmap.Width * bgScale;
                float unrotatedH = _currentBitmap.Height * bgScale;
                var bgRect = new SKRect(centerX - unrotatedW / 2, centerY - unrotatedH / 2, centerX + unrotatedW / 2, centerY + unrotatedH / 2);

                using var blurPaint = new SKPaint();
                using var blurFilter = record.BlurStrength > 0
                    ? SKImageFilter.CreateBlur((float)record.BlurStrength, (float)record.BlurStrength, SKShaderTileMode.Clamp)
                    : null;
                blurPaint.ImageFilter = blurFilter;

                canvas.Save();
                if (record.ManualRotationDegrees != 0)
                {
                    canvas.RotateDegrees(record.ManualRotationDegrees, centerX, centerY);
                }
                canvas.DrawBitmap(_currentBitmap, bgRect, blurPaint);
                canvas.Restore();
            }

            // Foreground image
            float fgScale;
            if (record.IsCarousel)
            {
                fgScale = Math.Max((float)fullCanvasWidth / bmpWidth, (float)targetHeight / bmpHeight);
            }
            else
            {
                fgScale = Math.Min((float)targetWidth / bmpWidth, (float)targetHeight / bmpHeight);
            }
            float fgW = bmpWidth * fgScale;
            float fgH = bmpHeight * fgScale;

            // Apply anchor
            float offsetX = (fullCanvasWidth - fgW) * (float)record.CropAnchorX;
            float offsetY = (targetHeight - fgH) * (float)record.CropAnchorY;

            float fgCenterX = offsetX + fgW / 2f;
            float fgCenterY = offsetY + fgH / 2f;

            float unrotatedFgW = _currentBitmap.Width * fgScale;
            float unrotatedFgH = _currentBitmap.Height * fgScale;
            var fgRect = new SKRect(fgCenterX - unrotatedFgW / 2, fgCenterY - unrotatedFgH / 2, fgCenterX + unrotatedFgW / 2, fgCenterY + unrotatedFgH / 2);

            canvas.Save();
            if (record.ManualRotationDegrees != 0)
            {
                canvas.RotateDegrees(record.ManualRotationDegrees, fgCenterX, fgCenterY);
            }

            if (record.DropShadow)
            {
                using var shadowPaint = new SKPaint();
                using var shadowFilter = SKImageFilter.CreateDropShadow(4, 4, 12, 12, new SKColor(0, 0, 0, 153));
                shadowPaint.ImageFilter = shadowFilter;
                canvas.DrawBitmap(_currentBitmap, fgRect, shadowPaint);
            }
            else
            {
                canvas.DrawBitmap(_currentBitmap, fgRect);
            }
            canvas.Restore();

            if (record.IsCarousel)
            {
                // Draw cut lines
                using var cutLinePaint = new SKPaint
                {
                    Color = SKColors.White,
                    StrokeWidth = 2 / scale,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 10 / scale, 10 / scale }, 0)
                };

                for (int i = 1; i < record.CarouselSlideCount; i++)
                {
                    float cutX = i * targetWidth;
                    canvas.DrawLine(cutX, 0, cutX, targetHeight, cutLinePaint);
                }

                if (record.SafeZoneOverlay)
                {
                    using var safeZonePaint = new SKPaint
                    {
                        Color = new SKColor(255, 0, 0, 100),
                        StrokeWidth = 4 / scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    };

                    for (int i = 0; i < record.CarouselSlideCount; i++)
                    {
                        float startX = i * targetWidth;
                        // Safe zone margins (e.g., 5% width, 10% height)
                        float marginX = targetWidth * 0.05f;
                        float marginY = targetHeight * 0.1f;
                        var safeRect = new SKRect(startX + marginX, marginY, startX + targetWidth - marginX, targetHeight - marginY);
                        canvas.DrawRect(safeRect, safeZonePaint);
                    }
                }
            }
            else if (record.SafeZoneOverlay)
            {
                using var safeZonePaint = new SKPaint
                {
                    Color = new SKColor(255, 0, 0, 100),
                    StrokeWidth = 4 / scale,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke
                };
                float marginX = fullCanvasWidth * 0.05f;
                float marginY = targetHeight * 0.1f;
                var safeRect = new SKRect(marginX, marginY, fullCanvasWidth - marginX, targetHeight - marginY);
                canvas.DrawRect(safeRect, safeZonePaint);
            }

            // Hover grid and anchor dot drawing block removed

            canvas.Restore();
        }

        private void PreviewCanvas_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var item = ViewModel.SelectedQueueItem;
            if (item?.Record == null || !item.Record.IsCarousel) return;

            PreviewCanvas.CapturePointer(e.Pointer);
            _isDragging = true;
            _lastPointerPosition = e.GetCurrentPoint(PreviewCanvas).Position;
        }

        private void PreviewCanvas_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isDragging) return;

            var item = ViewModel.SelectedQueueItem;
            if (item?.Record == null || !item.Record.IsCarousel || _currentBitmap == null) return;

            var currentPosition = e.GetCurrentPoint(PreviewCanvas).Position;
            double dx = currentPosition.X - _lastPointerPosition.X;
            double dy = currentPosition.Y - _lastPointerPosition.Y;

            _lastPointerPosition = currentPosition;

            var record = item.Record;

            int targetWidth;
            int targetHeight;
            if (record.TargetRatio == "1:1") { targetWidth = 1080; targetHeight = 1080; }
            else if (record.TargetRatio == "4:5") { targetWidth = 1080; targetHeight = 1350; }
            else if (record.TargetRatio == "9:16") { targetWidth = 1080; targetHeight = 1920; }
            else { targetWidth = 1080; targetHeight = 608; } // 16:9

            int fullCanvasWidth = targetWidth * record.CarouselSlideCount;

            float scale = Math.Min((float)PreviewCanvas.ActualWidth / fullCanvasWidth, (float)PreviewCanvas.ActualHeight / targetHeight);

            float fgScale = Math.Max((float)fullCanvasWidth / _currentBitmap.Width, (float)targetHeight / _currentBitmap.Height);
            float fgW = _currentBitmap.Width * fgScale;
            float fgH = _currentBitmap.Height * fgScale;

            // Calculate pan bounds
            double maxPanX = fgW - fullCanvasWidth;
            double maxPanY = fgH - targetHeight;

            if (maxPanX > 0)
            {
                // Translate pixel delta to anchor delta (0 to 1)
                // Note: dragging right (positive dx) means we are shifting the image right, which means crop anchor moves left (smaller)
                double anchorDeltaX = - (dx / scale) / maxPanX;
                record.CropAnchorX = Math.Clamp(record.CropAnchorX + anchorDeltaX, 0, 1);
            }

            if (maxPanY > 0)
            {
                double anchorDeltaY = - (dy / scale) / maxPanY;
                record.CropAnchorY = Math.Clamp(record.CropAnchorY + anchorDeltaY, 0, 1);
            }
        }

        private void PreviewCanvas_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                PreviewCanvas.ReleasePointerCapture(e.Pointer);
            }
        }

        private async void SimulateSwipe_Click(object sender, RoutedEventArgs e)
        {
            var item = ViewModel.SelectedQueueItem;
            if (item?.Record == null || !item.Record.IsCarousel || _currentBitmap == null) return;

            var record = item.Record;

            int targetWidth;
            int targetHeight;
            if (record.TargetRatio == "1:1") { targetWidth = 1080; targetHeight = 1080; }
            else if (record.TargetRatio == "4:5") { targetWidth = 1080; targetHeight = 1350; }
            else if (record.TargetRatio == "9:16") { targetWidth = 1080; targetHeight = 1920; }
            else { targetWidth = 1080; targetHeight = 608; } // 16:9

            int fullCanvasWidth = targetWidth * record.CarouselSlideCount;

            // Render full strip
            using var stripSurface = SKSurface.Create(new SKImageInfo(fullCanvasWidth, targetHeight));
            var canvas = stripSurface.Canvas;
            canvas.Clear(SKColors.Transparent);

            float fgScale = Math.Max((float)fullCanvasWidth / _currentBitmap.Width, (float)targetHeight / _currentBitmap.Height);
            float fgW = _currentBitmap.Width * fgScale;
            float fgH = _currentBitmap.Height * fgScale;

            float offsetX = (fullCanvasWidth - fgW) * (float)record.CropAnchorX;
            float offsetY = (targetHeight - fgH) * (float)record.CropAnchorY;

            float fgCenterX = offsetX + fgW / 2f;
            float fgCenterY = offsetY + fgH / 2f;

            float unrotatedFgW = _currentBitmap.Width * fgScale;
            float unrotatedFgH = _currentBitmap.Height * fgScale;
            var fgRect = new SKRect(fgCenterX - unrotatedFgW / 2, fgCenterY - unrotatedFgH / 2, fgCenterX + unrotatedFgW / 2, fgCenterY + unrotatedFgH / 2);

            canvas.Save();
            if (record.ManualRotationDegrees != 0)
            {
                canvas.RotateDegrees(record.ManualRotationDegrees, fgCenterX, fgCenterY);
            }
            canvas.DrawBitmap(_currentBitmap, fgRect);
            canvas.Restore();

            using var fullStripImage = stripSurface.Snapshot();

            SwipeImagesPanel.Children.Clear();

            // Slice and add to panel
            for (int i = 0; i < record.CarouselSlideCount; i++)
            {
                using var sliceImage = fullStripImage.Subset(new SKRectI(i * targetWidth, 0, (i + 1) * targetWidth, targetHeight));
                using var sliceData = sliceImage.Encode(SKEncodedImageFormat.Jpeg, 90);

                using var stream = new System.IO.MemoryStream();
                sliceData.SaveTo(stream);
                stream.Position = 0;

                var bitMapImage = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitMapImage.SetSourceAsync(stream.AsRandomAccessStream());

                var imageControl = new Microsoft.UI.Xaml.Controls.Image
                {
                    Source = bitMapImage,
                    Width = targetWidth * 0.4, // Scale down for preview
                    Height = targetHeight * 0.4,
                    Margin = new Thickness(0, 0, 4, 0)
                };

                SwipeImagesPanel.Children.Add(imageControl);
            }

            SwipeSimulatorOverlay.Visibility = Visibility.Visible;
        }

        private void CloseSwipeSimulator_Click(object sender, RoutedEventArgs e)
        {
            SwipeSimulatorOverlay.Visibility = Visibility.Collapsed;
            SwipeImagesPanel.Children.Clear();
        }

        private void PredefinedColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedQueueItem == null) return;
            if (sender is Button btn && btn.Tag is string hex)
            {
                ViewModel.SelectedQueueItem.Record.BackgroundColor = hex;
            }
        }

        public Visibility IsBlurMode(string mode) => mode == "blur" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSolidMode(string mode) => mode == "solid" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsEmpty(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility IsCarouselMode(bool isCarousel) => isCarousel ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsNotCarouselMode(bool isCarousel) => !isCarousel ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsStandardBlurMode(bool isCarousel, string mode) => (!isCarousel && mode == "blur") ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsStandardSolidMode(bool isCarousel, string mode) => (!isCarousel && mode == "solid") ? Visibility.Visible : Visibility.Collapsed;

        public Visibility IsProcessing(ViewModels.QueueItemStatus status) => status == ViewModels.QueueItemStatus.Processing ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsDone(ViewModels.QueueItemStatus status) => status == ViewModels.QueueItemStatus.Done ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsError(ViewModels.QueueItemStatus status) => status == ViewModels.QueueItemStatus.Error ? Visibility.Visible : Visibility.Collapsed;

        public Brush GetColorSwatchStroke(string swatchColor, string currentColor)
        {
            if (string.Equals(swatchColor, currentColor, StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212));
            }
            return new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        public double GetColorSwatchThickness(string swatchColor, string currentColor)
        {
            if (string.Equals(swatchColor, currentColor, StringComparison.OrdinalIgnoreCase))
            {
                return 3.0;
            }
            return 1.0;
        }

        // Rotation event handlers removed

        private void DeleteAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            if (ViewModel.SelectedQueueItem != null)
            {
                ViewModel.RemoveFileFromQueueCommand.Execute(ViewModel.SelectedQueueItem);
                args.Handled = true;
            }
        }

        private void ExportAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            ExportSelected_Click(null, null);
            args.Handled = true;
        }

        // Keyboard accelerators and pointer handlers removed
    }

    public class StringHexToColorConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string hex)
            {
                hex = hex.Replace("#", "");
                if (hex.Length == 6)
                {
                    if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                        byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                        byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        return Windows.UI.Color.FromArgb(255, r, g, b);
                    }
                }
                else if (hex.Length == 8)
                {
                    if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte a) &&
                        byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                        byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                        byte.TryParse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        return Windows.UI.Color.FromArgb(a, r, g, b);
                    }
                }
            }
            return Windows.UI.Color.FromArgb(255, 0, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Windows.UI.Color c)
            {
                return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return "#FF000000";
        }
    }

    public class ProcessingVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is ViewModels.QueueItemStatus status && status == ViewModels.QueueItemStatus.Processing ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class DoneVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is ViewModels.QueueItemStatus status && status == ViewModels.QueueItemStatus.Done ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class ErrorVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is ViewModels.QueueItemStatus status && status == ViewModels.QueueItemStatus.Error ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class PresetModeVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string mode && parameter is string targetMode)
            {
                return string.Equals(mode, targetMode, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class StringHexToBrushConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string hex)
            {
                hex = hex.Replace("#", "");
                if (hex.Length == 6)
                {
                    if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                        byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                        byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
                    }
                }
                else if (hex.Length == 8)
                {
                    if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte a) &&
                        byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                        byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                        byte.TryParse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
                    }
                }
            }
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is SolidColorBrush brush)
            {
                var c = brush.Color;
                return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return "#FF000000";
        }
    }
