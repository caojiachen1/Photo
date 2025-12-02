using System;
using System.Collections.Generic;
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
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Photo
{
    public sealed partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private StorageFile? _currentFile;
        private string? _currentFilePath;
        private int _rotationAngle = 0;
        private int _imageWidth = 0;
        private int _imageHeight = 0;
        private long _fileSize = 0;
        private bool _isImageLoaded = false;

        public MainWindow()
        {
            InitializeComponent();
            SetDarkTitleBar();
            SetupDragDrop();
            
            // 扩展标题栏
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);
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
                        await LoadImageAsync(file);
                    }
                }
            }
        }

        private bool IsImageFile(string extension)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".ico", ".tiff", ".tif" };
            return imageExtensions.Contains(extension.ToLowerInvariant());
        }

        public async Task LoadImageFromPathAsync(string path)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                await LoadImageAsync(file);
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("无法打开文件", ex.Message);
            }
        }

        private async Task LoadImageAsync(StorageFile file)
        {
            try
            {
                _currentFile = file;
                _currentFilePath = file.Path;
                _rotationAngle = 0;
                ImageRotateTransform.Angle = 0;

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

        private void FitImageToWindow()
        {
            if (!_isImageLoaded) return;
            
            // 计算合适的缩放比例使图片适应窗口
            var scrollViewerWidth = ImageScrollViewer.ActualWidth;
            var scrollViewerHeight = ImageScrollViewer.ActualHeight;

            if (scrollViewerWidth <= 0 || scrollViewerHeight <= 0 || _imageWidth <= 0 || _imageHeight <= 0)
                return;

            var scaleX = scrollViewerWidth / _imageWidth;
            var scaleY = scrollViewerHeight / _imageHeight;
            var scale = Math.Min(scaleX, scaleY);
            scale = Math.Min(scale, 1.0); // 不要放大超过100%

            // 确保在有效范围内
            scale = Math.Max(0.1f, Math.Min(10f, (float)scale));

            ImageScrollViewer.ChangeView(null, null, (float)scale);
        }

        private void ImageScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!e.IsIntermediate)
            {
                UpdateZoomPercentage();
            }
        }

        private void UpdateZoomPercentage()
        {
            var zoomFactor = ImageScrollViewer.ZoomFactor;
            ZoomPercentText.Text = $"{(int)(zoomFactor * 100)}%";
        }

        #region 缩放功能

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
                await LoadImageAsync(_currentFile);
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
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    await _currentFile.DeleteAsync(StorageDeleteOption.Default);
                    
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
                }
                catch (Exception ex)
                {
                    await ShowErrorDialog("无法删除文件", ex.Message);
                }
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
