using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using System.Diagnostics;

namespace Photo
{
    public sealed partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_MAXIMIZE = 3;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private StorageFile? _currentFile;
        private string? _currentFilePath;
        private int _rotationAngle = 0;
        private int _imageWidth = 0;
        private int _imageHeight = 0;
        private long _fileSize = 0;
        private bool _isImageLoaded = false;
        private List<StorageFile> _folderFiles = new List<StorageFile>();
        private FileSystemWatcher? _fileWatcher;
        private DispatcherTimer? _folderUpdateTimer;

        // 鼠标拖拽相关
        private bool _isDragging = false;
        private Point _lastPointerPosition;
        private Point _dragStartScrollPosition;

        // 缩放相关
        private float _minZoomFactor = 0.1f;

        // 滑块更新标志（避免循环更新）
        private bool _isUpdatingSlider = false;

        // 全屏相关
        private bool _isFullScreen = false;

        // 缩略图相关
        public ObservableCollection<ThumbnailItem> ThumbnailItems { get; } = new ObservableCollection<ThumbnailItem>();
        private bool _isThumbnailBarVisible = false;
        private bool _isUpdatingThumbnailSelection = false;
        private ScrollViewer? _thumbnailScrollViewer;

        public MainWindow()
        {
            InitializeComponent();
            SetDarkTitleBar();
            SetupDragDrop();
            InitializeFolderUpdateTimer();
            
            // 扩展标题栏
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);
            
            // 启动时默认最大化窗口
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                ShowWindow(hwnd, SW_MAXIMIZE);
            }
            catch { }

            // 加载完成后获取缩略图列表的ScrollViewer
            ThumbnailListView.Loaded += (s, e) =>
            {
                _thumbnailScrollViewer = GetScrollViewer(ThumbnailListView);
            };
        }

        private ScrollViewer? GetScrollViewer(DependencyObject element)
        {
            if (element is ScrollViewer scrollViewer)
                return scrollViewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                var result = GetScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void SetDarkTitleBar()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            int useImmersiveDarkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
        }

        private void SetupDragDrop()
        {
            var grid = Content as Grid;
            if (grid != null)
            {
                grid.AllowDrop = true;
                grid.DragOver += Grid_DragOver;
                grid.Drop += Grid_Drop;
            }
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "打开图片";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
        }

        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0 && items[0] is StorageFile file)
                {
                    if (IsImageFile(file.FileType))
                    {
                        await LoadImageAsync(file, true);
                    }
                }
            }
        }

        private bool IsImageFile(string extension)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".ico", ".tiff", ".tif" };
            return imageExtensions.Contains(extension.ToLowerInvariant());
        }

        private void InitializeFolderUpdateTimer()
        {
            _folderUpdateTimer = new DispatcherTimer();
            _folderUpdateTimer.Interval = TimeSpan.FromMilliseconds(200);
            _folderUpdateTimer.Tick += async (s, e) =>
            {
                _folderUpdateTimer.Stop();
                await UpdateFileListAsync();
                UpdateNavigationButtons();
            };
        }

        private void SetupFileWatcher(string folderPath)
        {
            if (_fileWatcher != null && string.Equals(_fileWatcher.Path, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }

            try
            {
                if (Directory.Exists(folderPath))
                {
                    _fileWatcher = new FileSystemWatcher(folderPath);
                    _fileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime;
                    _fileWatcher.Filter = "*.*";
                    _fileWatcher.Created += OnFileChanged;
                    _fileWatcher.Deleted += OnFileChanged;
                    _fileWatcher.Renamed += OnFileRenamed;
                    _fileWatcher.EnableRaisingEvents = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watcher error: {ex.Message}");
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (IsImageFile(Path.GetExtension(e.FullPath)))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _folderUpdateTimer?.Stop();
                    _folderUpdateTimer?.Start();
                });
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (IsImageFile(Path.GetExtension(e.OldFullPath)) || IsImageFile(Path.GetExtension(e.FullPath)))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _folderUpdateTimer?.Stop();
                    _folderUpdateTimer?.Start();
                });
            }
        }

        public async Task LoadImageFromPathAsync(string path)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                await LoadImageAsync(file, true);
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("无法打开文件", ex.Message);
            }
        }

        private async Task LoadImageAsync(StorageFile file, bool reloadFolder = true)
        {
            try
            {
                _currentFile = file;
                _currentFilePath = file.Path;
                _rotationAngle = 0;
                ImageRotateTransform.Angle = 0;

                // Setup watcher
                if (!string.IsNullOrEmpty(_currentFilePath))
                {
                    var folderPath = Path.GetDirectoryName(_currentFilePath);
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        SetupFileWatcher(folderPath);
                    }
                }

                // 加载图片
                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(stream);
                    
                    MainImage.Source = bitmapImage;
                    _imageWidth = bitmapImage.PixelWidth;
                    _imageHeight = bitmapImage.PixelHeight;
                }

                // 获取文件信息
                var properties = await file.GetBasicPropertiesAsync();
                _fileSize = (long)properties.Size;

                // 更新UI
                FileNameText.Text = file.Name;
                Title = $"{file.Name} - Photo";
                ImageDimensionsText.Text = $"{_imageWidth} x {_imageHeight}";
                FileSizeText.Text = FormatFileSize(_fileSize);

                PlaceholderPanel.Visibility = Visibility.Collapsed;
                _isImageLoaded = true;

                // 适应窗口
                await Task.Delay(100); // 等待布局完成
                FitImageToWindow();

                // 更新文件信息面板
                UpdateFileInfoPanel();

                if (reloadFolder)
                {
                    await UpdateFileListAsync();
                }
                else
                {
                    // 仅更新缩略图选择状态
                    UpdateThumbnailSelection();
                }
                UpdateNavigationButtons();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("无法加载图片", ex.Message);
            }
        }

        private void UpdateFileInfoPanel()
        {
            if (_currentFile == null) return;

            InfoFileName.Text = _currentFile.Name;
            InfoFilePath.Text = Path.GetDirectoryName(_currentFilePath) ?? "";
            InfoFileType.Text = _currentFile.FileType.ToUpperInvariant().TrimStart('.') + " 图片";
            InfoDimensions.Text = $"{_imageWidth} x {_imageHeight} 像素";
            InfoFileSize.Text = FormatFileSize(_fileSize);

            Task.Run(async () =>
            {
                try
                {
                    var properties = await _currentFile.GetBasicPropertiesAsync();
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        InfoCreatedDate.Text = properties.ItemDate.LocalDateTime.ToString("yyyy年M月d日 HH:mm");
                        InfoModifiedDate.Text = properties.DateModified.LocalDateTime.ToString("yyyy年M月d日 HH:mm");
                    });
                }
                catch { }
            });
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private float CalculateFitToWindowZoom()
        {
            var scrollViewerWidth = ImageScrollViewer.ActualWidth;
            var scrollViewerHeight = ImageScrollViewer.ActualHeight;

            if (scrollViewerWidth <= 0 || scrollViewerHeight <= 0 || _imageWidth <= 0 || _imageHeight <= 0)
                return 0.1f;

            var scaleX = scrollViewerWidth / _imageWidth;
            var scaleY = scrollViewerHeight / _imageHeight;
            var scale = Math.Min(scaleX, scaleY);
            scale = Math.Min(scale, 1.0); // 不要放大超过100%

            return (float)Math.Max(0.01f, scale);
        }

        private void UpdateMinZoomFactor()
        {
            _minZoomFactor = CalculateFitToWindowZoom();
        }

        private void FitImageToWindow()
        {
            if (!_isImageLoaded) return;
            
            var scale = CalculateFitToWindowZoom();
            _minZoomFactor = scale;

            ImageScrollViewer.ChangeView(null, null, scale);
        }

        private void ImageScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!e.IsIntermediate)
            {
                UpdateZoomPercentage();
                UpdateZoomSlider();
            }
            UpdateCursor();
        }

        private void UpdateZoomPercentage()
        {
            var zoomFactor = ImageScrollViewer.ZoomFactor;
            ZoomPercentText.Text = $"{(int)(zoomFactor * 100)}%";
        }

        private void UpdateZoomSlider()
        {
            if (ZoomSlider == null) return;

            _isUpdatingSlider = true;
            var zoomPercent = ImageScrollViewer.ZoomFactor * 100;
            // 限制滑块值在有效范围内
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, zoomPercent));
            _isUpdatingSlider = false;
        }

        #region 缩放功能

        private void ImageContainer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (!_isImageLoaded) return;

            var properties = e.GetCurrentPoint(ImageScrollViewer).Properties;
            var delta = properties.MouseWheelDelta;

            // 更新最小缩放比例
            UpdateMinZoomFactor();

            // 计算新的缩放比例
            var currentZoom = ImageScrollViewer.ZoomFactor;
            var zoomDelta = delta > 0 ? 1.1f : 0.9f;
            var newZoom = currentZoom * zoomDelta;

            // 限制缩放范围：最小为适应窗口，最大为1000%
            newZoom = Math.Max(_minZoomFactor, Math.Min(10f, newZoom));

            // 如果缩放比例没有变化，不执行操作
            if (Math.Abs(newZoom - currentZoom) < 0.001f)
            {
                e.Handled = true;
                return;
            }

            // 获取鼠标相对于 ScrollViewer 的位置
            var pointerPosition = e.GetCurrentPoint(ImageScrollViewer).Position;

            // 计算当前鼠标位置对应的内容坐标
            var contentX = ImageScrollViewer.HorizontalOffset + pointerPosition.X;
            var contentY = ImageScrollViewer.VerticalOffset + pointerPosition.Y;

            // 计算缩放后的新偏移量，保持鼠标位置不变
            var scale = newZoom / currentZoom;
            var newHorizontalOffset = contentX * scale - pointerPosition.X;
            var newVerticalOffset = contentY * scale - pointerPosition.Y;

            // 应用缩放
            ImageScrollViewer.ChangeView(newHorizontalOffset, newVerticalOffset, newZoom, true);
            
            // 更新光标状态
            UpdateCursor();

            e.Handled = true;
        }

        private void FitToWindow_Click(object sender, RoutedEventArgs e)
        {
            FitImageToWindow();
        }

        private void ActualSize_Click(object sender, RoutedEventArgs e)
        {
            ImageScrollViewer.ChangeView(null, null, 1.0f);
        }

        private void Zoom25_Click(object sender, RoutedEventArgs e)
        {
            ImageScrollViewer.ChangeView(null, null, 0.25f);
        }

        private void Zoom50_Click(object sender, RoutedEventArgs e)
        {
            ImageScrollViewer.ChangeView(null, null, 0.5f);
        }

        private void Zoom100_Click(object sender, RoutedEventArgs e)
        {
            ImageScrollViewer.ChangeView(null, null, 1.0f);
        }

        private void Zoom200_Click(object sender, RoutedEventArgs e)
        {
            ImageScrollViewer.ChangeView(null, null, 2.0f);
        }

        private void Zoom400_Click(object sender, RoutedEventArgs e)
        {
            ImageScrollViewer.ChangeView(null, null, 4.0f);
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded) return;

            UpdateMinZoomFactor();
            var currentZoom = ImageScrollViewer.ZoomFactor;
            var newZoom = currentZoom * 0.8f;
            newZoom = Math.Max(_minZoomFactor, newZoom);
            ImageScrollViewer.ChangeView(null, null, newZoom);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded) return;

            var currentZoom = ImageScrollViewer.ZoomFactor;
            var newZoom = currentZoom * 1.25f;
            newZoom = Math.Min(10f, newZoom);
            ImageScrollViewer.ChangeView(null, null, newZoom);
        }

        private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingSlider || !_isImageLoaded) return;

            var newZoom = (float)(e.NewValue / 100.0);
            UpdateMinZoomFactor();
            newZoom = Math.Max(_minZoomFactor, Math.Min(10f, newZoom));
            ImageScrollViewer.ChangeView(null, null, newZoom);
        }

        private void ZoomSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 开始拖动滑块时的处理
        }

        private void ZoomSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            // 结束拖动滑块时的处理
        }

        #endregion

        #region 鼠标拖拽功能

        private void ImageContainer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isImageLoaded) return;

            var point = e.GetCurrentPoint(sender as UIElement);
            
            // 左键或中键都可以拖拽
            if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed)
            {
                _isDragging = true;
                _lastPointerPosition = point.Position;
                _dragStartScrollPosition = new Point(ImageScrollViewer.HorizontalOffset, ImageScrollViewer.VerticalOffset);
                
                // 捕获指针
                if (sender is UIElement element)
                {
                    element.CapturePointer(e.Pointer);
                }
                
                UpdateCursor();
                e.Handled = true;
            }
        }

        private void ImageContainer_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging || !_isImageLoaded) return;

            var currentPosition = e.GetCurrentPoint(sender as UIElement).Position;
            
            // 计算移动距离
            var deltaX = _lastPointerPosition.X - currentPosition.X;
            var deltaY = _lastPointerPosition.Y - currentPosition.Y;

            // 更新滚动位置
            var newHorizontalOffset = ImageScrollViewer.HorizontalOffset + deltaX;
            var newVerticalOffset = ImageScrollViewer.VerticalOffset + deltaY;

            ImageScrollViewer.ChangeView(newHorizontalOffset, newVerticalOffset, null, true);

            _lastPointerPosition = currentPosition;
            e.Handled = true;
        }

        private void ImageContainer_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                if (sender is UIElement element)
                {
                    element.ReleasePointerCapture(e.Pointer);
                }
                
                UpdateCursor();
                e.Handled = true;
            }
        }

        private void ImageContainer_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
            UpdateCursor();
        }

        private void ImageContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            UpdateCursor();
        }

        private void ImageContainer_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            SetElementCursor(ImageContainerGrid, InputSystemCursorShape.Arrow);
        }

        #endregion

        #region 光标辅助方法

        private void UpdateCursor()
        {
            if (!_isImageLoaded)
            {
                SetElementCursor(ImageContainerGrid, InputSystemCursorShape.Arrow);
                return;
            }

            if (_isDragging)
            {
                // 拖拽时显示抓取手 (使用 SizeAll 模拟抓取状态，因为它最接近)
                SetElementCursor(ImageContainerGrid, InputSystemCursorShape.SizeAll);
            }
            else
            {
                // 检查是否放大（可滚动）
                // 注意：使用 ZoomFactor 判断可能更准确，或者 ScrollableWidth
                bool isZoomed = ImageScrollViewer.ZoomFactor > _minZoomFactor + 0.001f;
                
                if (isZoomed)
                {
                    // 放大时显示手型 (模拟 Open Hand)
                    SetElementCursor(ImageContainerGrid, InputSystemCursorShape.Hand);
                }
                else
                {
                    // 适应窗口时显示默认箭头
                    SetElementCursor(ImageContainerGrid, InputSystemCursorShape.Arrow);
                }
            }
        }

        private void SetElementCursor(UIElement element, InputSystemCursorShape cursorShape)
        {
            try
            {
                var cursor = InputSystemCursor.Create(cursorShape);
                var property = typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (property != null)
                {
                    property.SetValue(element, cursor);
                }
            }
            catch { }
        }

        private void ResetElementCursor(UIElement element)
        {
            try
            {
                var property = typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (property != null)
                {
                    property.SetValue(element, null);
                }
            }
            catch { }
        }

        #endregion

        #region 旋转功能

        private async void RotateButton_Click(object sender, RoutedEventArgs e)
        {
            await RotateImageAsync();
        }

        private async Task RotateImageAsync()
        {
            if (_currentFile == null || !_isImageLoaded) return;

            try
            {
                _rotationAngle = (_rotationAngle + 90) % 360;

                // 读取原始图片
                using var inputStream = await _currentFile.OpenAsync(FileAccessMode.Read);
                var decoder = await BitmapDecoder.CreateAsync(inputStream);

                // 创建临时文件来保存旋转后的图片
                var tempFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(
                    "temp_rotated" + _currentFile.FileType,
                    CreationCollisionOption.ReplaceExisting);

                using (var outputStream = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateForTranscodingAsync(outputStream, decoder);
                    encoder.BitmapTransform.Rotation = BitmapRotation.Clockwise90Degrees;
                    await encoder.FlushAsync();
                }

                // 复制回原文件
                await tempFile.CopyAndReplaceAsync(_currentFile);

                // 重新加载图片
                await LoadImageAsync(_currentFile, false);
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("无法旋转图片", ex.Message);
            }
        }

        #endregion

        #region 删除功能

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFile == null) return;

            // 检查是否需要确认
            if (AppSettings.ConfirmBeforeDelete)
            {
                var dialog = new ContentDialog
                {
                    Title = "删除文件",
                    Content = $"确定要将 \"{_currentFile.Name}\" 移至回收站吗？",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    return;
                }
            }

            try
            {
                // 记录要切换到的文件（优先切换到下一张，如果没有则切换到上一张）
                StorageFile? targetFile = null;
                if (_folderFiles.Count > 1)
                {
                    int currentIndex = _folderFiles.FindIndex(f => f.Path == _currentFile.Path);
                    if (currentIndex >= 0)
                    {
                        // 优先切换到下一张
                        if (currentIndex < _folderFiles.Count - 1)
                        {
                            targetFile = _folderFiles[currentIndex + 1];
                        }
                        else if (currentIndex > 0)
                        {
                            // 如果是最后一张，则切换到上一张
                            targetFile = _folderFiles[currentIndex - 1];
                        }
                    }
                }

                await _currentFile.DeleteAsync(StorageDeleteOption.Default);
                
                if (targetFile != null)
                {
                    // 加载目标图片
                    await LoadImageAsync(targetFile, true);
                }
                else
                {
                    // 重置UI
                    MainImage.Source = null;
                    _currentFile = null;
                    _currentFilePath = null;
                    _isImageLoaded = false;
                    
                    FileNameText.Text = "";
                    Title = "Photo";
                    ImageDimensionsText.Text = "";
                    FileSizeText.Text = "";
                    PlaceholderPanel.Visibility = Visibility.Visible;
                    FileInfoPanel.Visibility = Visibility.Collapsed;
                    ZoomPercentText.Text = "100%";
                    
                    _folderFiles.Clear();
                    UpdateNavigationButtons();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("无法删除文件", ex.Message);
            }
        }

        #endregion

        #region 文件信息面板

        private void FileInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded) return;
            
            FileInfoPanel.Visibility = FileInfoPanel.Visibility == Visibility.Visible 
                ? Visibility.Collapsed 
                : Visibility.Visible;
        }

        private void CloseFileInfoPanel_Click(object sender, RoutedEventArgs e)
        {
            FileInfoPanel.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region 另存为、复制、在资源管理器中打开

        private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFile == null || !_isImageLoaded) return;

            try
            {
                var savePicker = new FileSavePicker();
                var hwnd = WindowNative.GetWindowHandle(this);
                InitializeWithWindow.Initialize(savePicker, hwnd);

                savePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                savePicker.SuggestedFileName = Path.GetFileNameWithoutExtension(_currentFile.Name);

                // 根据当前文件类型添加文件类型选项
                var extension = _currentFile.FileType.ToLowerInvariant();
                switch (extension)
                {
                    case ".jpg":
                    case ".jpeg":
                        savePicker.FileTypeChoices.Add("JPEG 图片", new List<string> { ".jpg", ".jpeg" });
                        break;
                    case ".png":
                        savePicker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
                        break;
                    case ".bmp":
                        savePicker.FileTypeChoices.Add("BMP 图片", new List<string> { ".bmp" });
                        break;
                    case ".gif":
                        savePicker.FileTypeChoices.Add("GIF 图片", new List<string> { ".gif" });
                        break;
                    case ".webp":
                        savePicker.FileTypeChoices.Add("WebP 图片", new List<string> { ".webp" });
                        break;
                    case ".tiff":
                    case ".tif":
                        savePicker.FileTypeChoices.Add("TIFF 图片", new List<string> { ".tiff", ".tif" });
                        break;
                    default:
                        savePicker.FileTypeChoices.Add("图片", new List<string> { extension });
                        break;
                }

                // 添加其他常见格式
                if (extension != ".jpg" && extension != ".jpeg")
                    savePicker.FileTypeChoices.Add("JPEG 图片", new List<string> { ".jpg", ".jpeg" });
                if (extension != ".png")
                    savePicker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
                if (extension != ".bmp")
                    savePicker.FileTypeChoices.Add("BMP 图片", new List<string> { ".bmp" });

                var file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    await _currentFile.CopyAndReplaceAsync(file);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("无法保存文件", ex.Message);
            }
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFile == null || !_isImageLoaded) return;

            try
            {
                var dataPackage = new DataPackage();
                dataPackage.RequestedOperation = DataPackageOperation.Copy;

                // 复制文件引用
                dataPackage.SetStorageItems(new List<IStorageItem> { _currentFile });

                // 同时复制图片位图数据
                var stream = RandomAccessStreamReference.CreateFromFile(_currentFile);
                dataPackage.SetBitmap(stream);

                Clipboard.SetContent(dataPackage);
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("无法复制图片", ex.Message);
            }
        }

        private async void OpenInExplorerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFile == null || string.IsNullOrEmpty(_currentFilePath)) return;

            try
            {
                // 使用 explorer.exe 打开并选中文件
                Process.Start("explorer.exe", $"/select,\"{_currentFilePath}\"");
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("无法打开文件资源管理器", ex.Message);
            }
        }

        #endregion

        #region 导航功能

        private async Task UpdateFileListAsync()
        {
            if (_currentFile == null) return;

            try
            {
                var folder = await _currentFile.GetParentAsync();
                if (folder != null)
                {
                    var files = await folder.GetFilesAsync();
                    _folderFiles = files.Where(f => IsImageFile(f.FileType))
                                        .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                                        .ToList();
                }
                else
                {
                    _folderFiles.Clear();
                    _folderFiles.Add(_currentFile);
                }

                // 更新缩略图列表
                await UpdateThumbnailsAsync();
            }
            catch
            {
                _folderFiles.Clear();
                if (_currentFile != null) _folderFiles.Add(_currentFile);
            }
        }

        private void UpdateNavigationButtons()
        {
            if (_folderFiles.Count <= 1 || _currentFile == null)
            {
                PreviousButton.Visibility = Visibility.Collapsed;
                NextButton.Visibility = Visibility.Collapsed;
                return;
            }

            int currentIndex = _folderFiles.FindIndex(f => f.Path == _currentFile.Path);
            
            PreviousButton.Visibility = currentIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Visibility = currentIndex < _folderFiles.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateImageAsync(-1);
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateImageAsync(1);
        }

        private async void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // ESC 键退出全屏
            if (e.Key == Windows.System.VirtualKey.Escape && _isFullScreen)
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }

            if (!_isImageLoaded) return;

            if (e.Key == Windows.System.VirtualKey.Left)
            {
                await NavigateImageAsync(-1);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Right)
            {
                await NavigateImageAsync(1);
                e.Handled = true;
            }
        }

        private async Task NavigateImageAsync(int direction)
        {
            if (_folderFiles.Count <= 1 || _currentFile == null) return;

            int currentIndex = _folderFiles.FindIndex(f => f.Path == _currentFile.Path);
            
            if (currentIndex == -1) 
            {
                await UpdateFileListAsync();
                currentIndex = _folderFiles.FindIndex(f => f.Path == _currentFile.Path);
                if (currentIndex == -1) return;
            }

            int newIndex = currentIndex + direction;
            
            // 不循环切换
            if (newIndex < 0 || newIndex >= _folderFiles.Count) return;

            var nextFile = _folderFiles[newIndex];
            await LoadImageAsync(nextFile, false);
        }

        #endregion

        #region 全屏功能

        private void FullScreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        private void ToggleFullScreen()
        {
            var appWindow = GetAppWindow();
            if (appWindow == null) return;

            if (_isFullScreen)
            {
                // 退出全屏
                appWindow.SetPresenter(AppWindowPresenterKind.Default);
                FullScreenIcon.Glyph = "\uE740"; // 全屏图标
                ToolTipService.SetToolTip(FullScreenButton, "全屏");
                _isFullScreen = false;

                // 显示工具栏
                TopToolbar.Visibility = Visibility.Visible;
                BottomStatusBar.Visibility = Visibility.Visible;
                
                // 恢复缩略图条状态
                if (_isThumbnailBarVisible)
                {
                    ThumbnailBar.Visibility = Visibility.Visible;
                }

                // 恢复行定义
                RootGrid.RowDefinitions[0].Height = new GridLength(48);
                RootGrid.RowDefinitions[3].Height = new GridLength(48);
            }
            else
            {
                // 进入全屏
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                FullScreenIcon.Glyph = "\uE73F"; // 退出全屏图标
                ToolTipService.SetToolTip(FullScreenButton, "退出全屏");
                _isFullScreen = true;

                // 隐藏工具栏和缩略图条
                TopToolbar.Visibility = Visibility.Collapsed;
                BottomStatusBar.Visibility = Visibility.Collapsed;
                ThumbnailBar.Visibility = Visibility.Collapsed;

                // 设置行高度为0
                RootGrid.RowDefinitions[0].Height = new GridLength(0);
                RootGrid.RowDefinitions[3].Height = new GridLength(0);
            }

            // 重新适应窗口
            if (_isImageLoaded)
            {
                DispatcherQueue.TryEnqueue(() => FitImageToWindow());
            }
        }

        private AppWindow? GetAppWindow()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }

        #endregion

        #region 设置功能

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsDialog
            {
                XamlRoot = Content.XamlRoot,
                ConfirmBeforeDelete = AppSettings.ConfirmBeforeDelete
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // 保存设置
                AppSettings.ConfirmBeforeDelete = dialog.ConfirmBeforeDelete;
            }
        }

        #endregion

        #region 缩略图功能

        private void ThumbnailToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isThumbnailBarVisible = !_isThumbnailBarVisible;
            ThumbnailBar.Visibility = _isThumbnailBarVisible ? Visibility.Visible : Visibility.Collapsed;
            
            // 更新图标
            ThumbnailToggleIcon.Glyph = _isThumbnailBarVisible ? "\uE70D" : "\uE8FD";
            ToolTipService.SetToolTip(ThumbnailToggleButton, _isThumbnailBarVisible ? "隐藏缩略图" : "显示缩略图");

            // 如果显示缩略图，滚动到当前选中项
            if (_isThumbnailBarVisible && _currentFile != null)
            {
                ScrollToCurrentThumbnail();
            }

            // 重新适应窗口，避免缩略图列表遮挡图片
            if (_isImageLoaded)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    // 延迟一帧让布局更新完成
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        FitImageToWindow();
                    });
                });
            }
        }

        private async Task UpdateThumbnailsAsync()
        {
            // 清空现有缩略图
            ThumbnailItems.Clear();

            if (_folderFiles.Count == 0) return;

            // 添加所有文件的缩略图项（先显示占位符）
            foreach (var file in _folderFiles)
            {
                var item = new ThumbnailItem(file);
                item.IsSelected = file.Path == _currentFile?.Path;
                ThumbnailItems.Add(item);
            }

            // 异步加载缩略图
            await LoadThumbnailsAsync();
        }

        private async Task LoadThumbnailsAsync()
        {
            // 并行加载缩略图，但限制并发数以避免过度消耗资源
            var tasks = new List<Task>();
            var semaphore = new System.Threading.SemaphoreSlim(4); // 最多同时加载4个

            foreach (var item in ThumbnailItems.ToList())
            {
                tasks.Add(LoadSingleThumbnailAsync(item, semaphore));
            }

            await Task.WhenAll(tasks);
        }

        private async Task LoadSingleThumbnailAsync(ThumbnailItem item, System.Threading.SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                var thumbnail = await item.File.GetThumbnailAsync(ThumbnailMode.SingleItem, 200, ThumbnailOptions.ResizeThumbnail);
                if (thumbnail != null)
                {
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(thumbnail);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        item.Thumbnail = bitmapImage;
                        item.IsLoading = false;
                    });
                }
                else
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        item.IsLoading = false;
                    });
                }
            }
            catch
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    item.IsLoading = false;
                });
            }
            finally
            {
                semaphore.Release();
            }
        }

        private void UpdateThumbnailSelection()
        {
            if (_currentFile == null) return;

            _isUpdatingThumbnailSelection = true;
            
            foreach (var item in ThumbnailItems)
            {
                item.IsSelected = item.FilePath == _currentFile.Path;
            }

            // 选中对应项
            var selectedItem = ThumbnailItems.FirstOrDefault(t => t.FilePath == _currentFile.Path);
            if (selectedItem != null)
            {
                ThumbnailListView.SelectedItem = selectedItem;
            }

            _isUpdatingThumbnailSelection = false;

            // 滚动到当前缩略图
            ScrollToCurrentThumbnail();
        }

        private void ScrollToCurrentThumbnail()
        {
            var selectedItem = ThumbnailItems.FirstOrDefault(t => t.FilePath == _currentFile?.Path);
            if (selectedItem != null)
            {
                ThumbnailListView.ScrollIntoView(selectedItem, ScrollIntoViewAlignment.Default);
            }
        }

        private async void ThumbnailListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingThumbnailSelection) return;
            if (e.AddedItems.Count == 0) return;

            if (e.AddedItems[0] is ThumbnailItem item)
            {
                if (item.FilePath != _currentFile?.Path)
                {
                    await LoadImageAsync(item.File, false);
                    UpdateThumbnailSelection();
                    UpdateNavigationButtons();
                }
            }
        }

        private void ThumbnailListView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_thumbnailScrollViewer == null) return;

            var properties = e.GetCurrentPoint(ThumbnailListView).Properties;
            var delta = properties.MouseWheelDelta;

            // 将垂直滚轮转换为水平滚动
            var newOffset = _thumbnailScrollViewer.HorizontalOffset - delta;
            newOffset = Math.Max(0, Math.Min(_thumbnailScrollViewer.ScrollableWidth, newOffset));
            _thumbnailScrollViewer.ChangeView(newOffset, null, null);

            e.Handled = true;
        }

        #endregion

        private async Task ShowErrorDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
