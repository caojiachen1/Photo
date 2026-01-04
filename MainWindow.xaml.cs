using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Photo.Services;
using Photo.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using WinRT.Interop;
using System.Collections.Specialized;

namespace Photo
{
    public sealed partial class MainWindow : Window
    {
        #region P/Invoke

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_MAXIMIZE = 3;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        #endregion

        #region Private Fields

        private bool _isDragging;
        private Point _lastPointerPosition;
        private float _minZoomFactor = 0.1f;
        private bool _isUpdatingSlider;
        private ScrollViewer? _thumbnailScrollViewer;
        private readonly List<(Border faceBox, FrameworkElement? textContainer, TextBlock? textBlock)> _faceBoxElements = new();

        #endregion

        #region ViewModel

        public MainWindowViewModel ViewModel { get; }

        #endregion

        #region Constructor

        public MainWindow()
        {
            InitializeComponent();

            // 创建服务
            var dispatcherQueue = DispatcherQueue;
            var imageService = new ImageService(dispatcherQueue);
            var dialogService = new DialogService();
            var fileWatcherService = new FileWatcherService();
            var settingsService = new SettingsService();
            var clipboardService = new ClipboardService();
            var explorerService = new ExplorerService();

            // 创建 ViewModel
            ViewModel = new MainWindowViewModel(
                imageService,
                dialogService,
                fileWatcherService,
                settingsService,
                clipboardService,
                explorerService,
                dispatcherQueue);

            if (Content is FrameworkElement rootElement)
            {
                rootElement.DataContext = ViewModel;
            }

            // 设置 DialogService 的依赖
            var hwnd = WindowNative.GetWindowHandle(this);
            dialogService.SetWindowHandle(hwnd);
            
            // 当内容加载完成后设置 XamlRoot
            if (Content is FrameworkElement frameworkElement)
            {
                frameworkElement.Loaded += (s, e) => dialogService.SetXamlRoot(Content.XamlRoot);
            }

            // 订阅 ViewModel 事件
            ViewModel.RequestFitToWindow += FitImageToWindow;
            ViewModel.RequestZoom += SetZoom;
            ViewModel.RequestZoomIn += ZoomIn;
            ViewModel.RequestZoomOut += ZoomOut;
            ViewModel.FullScreenChanged += OnFullScreenChanged;
            ViewModel.ThumbnailSelectionChanged += ScrollToCurrentThumbnail;
            ViewModel.SettingsRequested += OnSettingsRequested;
            ViewModel.ZoomSliderValueChanged += OnZoomSliderValueChanged;
            ViewModel.FaceRegions.CollectionChanged += FaceRegions_CollectionChanged;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            // 初始化窗口
            SetDarkTitleBar();
            SetupDragDrop();

            // 扩展标题栏
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);

            // 启动时默认最大化窗口
            try
            {
                ShowWindow(hwnd, SW_MAXIMIZE);
            }
            catch { }

            // 加载完成后获取缩略图列表的ScrollViewer
            ThumbnailListView.Loaded += (s, e) =>
            {
                _thumbnailScrollViewer = GetScrollViewer(ThumbnailListView);
            };
        }

        #endregion

        #region Public Methods

        public async Task LoadImageFromPathAsync(string path)
        {
            await ViewModel.LoadImageFromPathAsync(path);
        }

        #endregion

        #region Window Setup

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

        #endregion

        #region Drag and Drop

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
                    await ViewModel.HandleDropAsync(file);
                }
            }
        }

        #endregion

        #region Zoom Methods

        private float CalculateFitToWindowZoom()
        {
            var scrollViewerWidth = ImageScrollViewer.ActualWidth;
            var scrollViewerHeight = ImageScrollViewer.ActualHeight;
            var imageWidth = ViewModel.ImageWidth;
            var imageHeight = ViewModel.ImageHeight;

            if (scrollViewerWidth <= 0 || scrollViewerHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
                return 0.1f;

            var scaleX = scrollViewerWidth / imageWidth;
            var scaleY = scrollViewerHeight / imageHeight;
            var scale = Math.Min(scaleX, scaleY);
            scale = Math.Min(scale, 1.0);

            return (float)Math.Max(0.01f, scale);
        }

        private void UpdateMinZoomFactor()
        {
            _minZoomFactor = CalculateFitToWindowZoom();
        }

        private void FitImageToWindow()
        {
            if (!ViewModel.IsImageLoaded) return;

            var scale = CalculateFitToWindowZoom();
            _minZoomFactor = scale;
            ImageScrollViewer.ChangeView(null, null, scale);
        }

        private void SetZoom(float zoom)
        {
            ImageScrollViewer.ChangeView(null, null, zoom);
        }

        private void ZoomIn()
        {
            if (!ViewModel.IsImageLoaded) return;

            var currentZoom = ImageScrollViewer.ZoomFactor;
            var newZoom = currentZoom * 1.25f;
            newZoom = Math.Min(10f, newZoom);
            ImageScrollViewer.ChangeView(null, null, newZoom);
        }

        private void ZoomOut()
        {
            if (!ViewModel.IsImageLoaded) return;

            UpdateMinZoomFactor();
            var currentZoom = ImageScrollViewer.ZoomFactor;
            var newZoom = currentZoom * 0.8f;
            newZoom = Math.Max(_minZoomFactor, newZoom);
            ImageScrollViewer.ChangeView(null, null, newZoom);
        }

        private void OnZoomSliderValueChanged(double value)
        {
            if (_isUpdatingSlider || !ViewModel.IsImageLoaded) return;

            var newZoom = (float)(value / 100.0);
            UpdateMinZoomFactor();
            newZoom = Math.Max(_minZoomFactor, Math.Min(10f, newZoom));
            ImageScrollViewer.ChangeView(null, null, newZoom);
        }

        #endregion

        #region Event Handlers

        private void ImageScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!e.IsIntermediate)
            {
                _isUpdatingSlider = true;
                ViewModel.UpdateZoomDisplay(ImageScrollViewer.ZoomFactor);
                _isUpdatingSlider = false;
            }
            UpdateCursor();
            UpdateFaceBoxStyles();
        }

        private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            // ViewModel handles this via ZoomSliderValueChanged event
        }

        private void ImageContainer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (!ViewModel.IsImageLoaded) return;

            var properties = e.GetCurrentPoint(ImageScrollViewer).Properties;
            var delta = properties.MouseWheelDelta;

            UpdateMinZoomFactor();

            var currentZoom = ImageScrollViewer.ZoomFactor;
            var zoomDelta = delta > 0 ? 1.1f : 0.9f;
            var newZoom = currentZoom * zoomDelta;

            newZoom = Math.Max(_minZoomFactor, Math.Min(10f, newZoom));

            if (Math.Abs(newZoom - currentZoom) < 0.001f)
            {
                e.Handled = true;
                return;
            }

            var pointerPosition = e.GetCurrentPoint(ImageScrollViewer).Position;
            var contentX = ImageScrollViewer.HorizontalOffset + pointerPosition.X;
            var contentY = ImageScrollViewer.VerticalOffset + pointerPosition.Y;

            var scale = newZoom / currentZoom;
            var newHorizontalOffset = contentX * scale - pointerPosition.X;
            var newVerticalOffset = contentY * scale - pointerPosition.Y;

            ImageScrollViewer.ChangeView(newHorizontalOffset, newVerticalOffset, newZoom, false);
            UpdateCursor();

            e.Handled = true;
        }

        private void ImageContainer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!ViewModel.IsImageLoaded) return;

            var point = e.GetCurrentPoint(sender as UIElement);

            if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed)
            {
                _isDragging = true;
                _lastPointerPosition = point.Position;

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
            if (!_isDragging || !ViewModel.IsImageLoaded) return;

            var currentPosition = e.GetCurrentPoint(sender as UIElement).Position;

            var deltaX = _lastPointerPosition.X - currentPosition.X;
            var deltaY = _lastPointerPosition.Y - currentPosition.Y;

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

        private async void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            await ViewModel.HandleKeyDownAsync(e.Key);
            e.Handled = true;
        }

        private async void ThumbnailListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ThumbnailItem item)
            {
                await ViewModel.HandleThumbnailSelectionAsync(item);
            }
        }

        private void ThumbnailListView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_thumbnailScrollViewer == null) return;

            var properties = e.GetCurrentPoint(ThumbnailListView).Properties;
            var delta = properties.MouseWheelDelta;

            var newOffset = _thumbnailScrollViewer.HorizontalOffset - delta;
            newOffset = Math.Max(0, Math.Min(_thumbnailScrollViewer.ScrollableWidth, newOffset));
            _thumbnailScrollViewer.ChangeView(newOffset, null, null);

            e.Handled = true;
        }

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowSettingsDialogAsync();
        }

        #endregion

        #region Full Screen

        private void OnFullScreenChanged(bool isFullScreen)
        {
            var appWindow = GetAppWindow();
            if (appWindow == null) return;

            if (isFullScreen)
            {
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                RootGrid.RowDefinitions[0].Height = new GridLength(0);
                RootGrid.RowDefinitions[3].Height = new GridLength(0);
            }
            else
            {
                appWindow.SetPresenter(AppWindowPresenterKind.Default);
                RootGrid.RowDefinitions[0].Height = new GridLength(48);
                RootGrid.RowDefinitions[3].Height = new GridLength(48);
            }

            if (ViewModel.IsImageLoaded)
            {
                DispatcherQueue.TryEnqueue(FitImageToWindow);
            }
        }

        private AppWindow? GetAppWindow()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }

        #endregion

        #region Helper Methods

        private void UpdateCursor()
        {
            if (!ViewModel.IsImageLoaded)
            {
                SetElementCursor(ImageContainerGrid, InputSystemCursorShape.Arrow);
                return;
            }

            if (_isDragging)
            {
                SetElementCursor(ImageContainerGrid, InputSystemCursorShape.SizeAll);
            }
            else
            {
                bool isZoomed = ImageScrollViewer.ZoomFactor > _minZoomFactor + 0.001f;

                if (isZoomed)
                {
                    SetElementCursor(ImageContainerGrid, InputSystemCursorShape.Hand);
                }
                else
                {
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

        private void ScrollToCurrentThumbnail()
        {
            var selectedItem = ThumbnailListView.SelectedItem;
            if (selectedItem != null)
            {
                ThumbnailListView.ScrollIntoView(selectedItem, ScrollIntoViewAlignment.Default);
            }
        }

        private async void OnSettingsRequested()
        {
            await ShowSettingsDialogAsync();
        }

        private async Task ShowSettingsDialogAsync()
        {
            var dialog = new SettingsDialog
            {
                XamlRoot = Content.XamlRoot,
                ConfirmBeforeDelete = AppSettings.ConfirmBeforeDelete,
                ShowFaces = AppSettings.ShowFaces,
                UseHardwareAcceleration = AppSettings.UseHardwareAcceleration
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                AppSettings.ConfirmBeforeDelete = dialog.ConfirmBeforeDelete;
                
                if (AppSettings.ShowFaces != dialog.ShowFaces)
                {
                    AppSettings.ShowFaces = dialog.ShowFaces;
                    UpdateFaceOverlay();
                }

                AppSettings.UseHardwareAcceleration = dialog.UseHardwareAcceleration;
            }
        }

        private void FaceRegions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateFaceOverlay();
        }

        private void MainImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateFaceOverlay();
        }

        private void UpdateFaceOverlay()
        {
            if (FaceOverlayCanvas == null) return;

            FaceOverlayCanvas.Children.Clear();
            _faceBoxElements.Clear();

            if (!AppSettings.ShowFaces) return;

            var imageWidth = MainImage.ActualWidth;
            var imageHeight = MainImage.ActualHeight;

            if (imageWidth <= 0 || imageHeight <= 0) return;

            // 设置 Canvas 大小与图片一致
            FaceOverlayCanvas.Width = imageWidth;
            FaceOverlayCanvas.Height = imageHeight;

            System.Diagnostics.Debug.WriteLine($"UpdateFaceOverlay: Image size = {imageWidth} x {imageHeight}, Regions count = {ViewModel.FaceRegions.Count}");

            foreach (var region in ViewModel.FaceRegions)
            {
                var x = region.X * imageWidth;
                var y = region.Y * imageHeight;
                var w = region.Width * imageWidth;
                var h = region.Height * imageHeight;

                System.Diagnostics.Debug.WriteLine($"Drawing face: {region.Name} at ({x}, {y}, {w}, {h})");

                // 创建可见的人脸框（默认不可见）
                var faceBox = new Border
                {
                    BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White),
                    Width = w,
                    Height = h,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    Opacity = 0,  // 默认不可见
                    IsHitTestVisible = false  // 不参与命中测试
                };

                FrameworkElement? textContainer = null;
                TextBlock? textBlock = null;

                // 添加人名标签
                if (!string.IsNullOrEmpty(region.Name))
                {
                    textBlock = new TextBlock
                    {
                        Text = region.Name,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                        FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    textContainer = new Border
                    {
                        Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 64, 64, 64)),
                        BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(2, 0, 2, 0),
                        Child = textBlock,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Bottom
                    };

                    faceBox.Child = textContainer;
                }

                // 保存元素引用以便后续更新样式
                _faceBoxElements.Add((faceBox, textContainer, textBlock));

                ToolTipService.SetToolTip(faceBox, region.Name);

                Canvas.SetLeft(faceBox, x);
                Canvas.SetTop(faceBox, y);

                // 创建透明的悬停检测区域（比人脸框稍大一些）
                var padding = 20.0;  // 扩展检测区域
                var hitArea = new Border
                {
                    Width = w + padding * 2,
                    Height = h + padding * 2,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                };

                Canvas.SetLeft(hitArea, x - padding);
                Canvas.SetTop(hitArea, y - padding);

                // 鼠标进入时显示人脸框
                hitArea.PointerEntered += (s, e) =>
                {
                    faceBox.Opacity = 1;
                };

                // 鼠标离开时隐藏人脸框
                hitArea.PointerExited += (s, e) =>
                {
                    faceBox.Opacity = 0;
                };

                // 先添加检测区域，再添加人脸框（确保人脸框在上层显示）
                FaceOverlayCanvas.Children.Add(hitArea);
                FaceOverlayCanvas.Children.Add(faceBox);
            }

            // 初始化样式
            UpdateFaceBoxStyles();
        }

        private void UpdateFaceBoxStyles()
        {
            if (_faceBoxElements.Count == 0) return;

            // 获取当前缩放比例
            var zoomFactor = ImageScrollViewer?.ZoomFactor ?? 1.0f;

            // 获取图片原始分辨率
            var originalWidth = ViewModel.ImageWidth;
            var originalHeight = ViewModel.ImageHeight;

            // 根据图片分辨率动态确定基础值
            double baseFontSize, baseBorderThickness, baseTextOffset, minBorderThickness;

            if (originalWidth >= 3000 || originalHeight >= 3000)
            {
                // 高分辨率图片（4K及以上）
                baseFontSize = 12.0;
                baseBorderThickness = 1.5;
                baseTextOffset = 25.0;
                minBorderThickness = 0.8;
            }
            else if (originalWidth >= 2000 || originalHeight >= 2000)
            {
                // 高分辨率图片（2K-4K）
                baseFontSize = 11.0;
                baseBorderThickness = 1.2;
                baseTextOffset = 24.0;
                minBorderThickness = 0.7;
            }
            else if (originalWidth >= 1000 || originalHeight >= 1000)
            {
                // 中等分辨率图片（1K-2K）
                baseFontSize = 10.0;
                baseBorderThickness = 1.0;
                baseTextOffset = 22.0;
                minBorderThickness = 0.6;
            }
            else
            {
                // 低分辨率图片（<1K）
                baseFontSize = 9.0;
                baseBorderThickness = 0.8;
                baseTextOffset = 20.0;
                minBorderThickness = 0.5;
            }

            // 根据缩放比例反向调整，使得视觉大小保持不变
            var fontSize = baseFontSize / zoomFactor;
            var borderThickness = baseBorderThickness / zoomFactor;
            var textOffset = baseTextOffset / zoomFactor;

            // 设置最小值，防止过小不可见（根据分辨率动态调整）
            fontSize = Math.Max(fontSize, 7.0);
            borderThickness = Math.Max(borderThickness, minBorderThickness);
            textOffset = Math.Max(textOffset, 15.0);

            foreach (var (faceBox, textContainer, textBlock) in _faceBoxElements)
            {
                faceBox.BorderThickness = new Thickness(borderThickness);

                if (textBlock != null && textContainer is Border border)
                {
                    textBlock.FontSize = fontSize;
                    border.Margin = new Thickness(0, 0, 0, -textOffset - (fontSize / 2));
                    border.BorderThickness = new Thickness(Math.Max(borderThickness * 0.6, 0.5));
                    border.CornerRadius = new CornerRadius(fontSize * 0.5);
                    border.Padding = new Thickness(fontSize * 0.2, 0, fontSize * 0.2, 0);
                }
            }
        }

        private void OnToolbarElementDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.VideoFile))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ViewModel.VideoFile != null)
                    {
                        VideoPlayer.ViewModel.FilePath = ViewModel.VideoFile.Path;
                    }
                    else
                    {
                        VideoPlayer.ViewModel.StopCommand.Execute(null);
                    }
                });
            }
        }

        #endregion
    }
}
