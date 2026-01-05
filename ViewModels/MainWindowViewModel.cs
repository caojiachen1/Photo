using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Photo.Services;
using Windows.Storage;

namespace Photo.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        #region Services

        private readonly IImageService _imageService;
        private readonly IDialogService _dialogService;
        private readonly IFileWatcherService _fileWatcherService;
        private readonly ISettingsService _settingsService;
        private readonly IClipboardService _clipboardService;
        private readonly IExplorerService _explorerService;
        private readonly DispatcherQueue _dispatcherQueue;
        private DispatcherTimer? _folderUpdateTimer;
        private System.Threading.CancellationTokenSource? _thumbnailCts;

        #endregion

        #region Private Fields

        private StorageFile? _currentFile;
        private List<StorageFile> _folderFiles = new();
        private bool _isUpdatingThumbnailSelection;

        #endregion

        #region Observable Properties

        private BitmapImage? _imageSource;
        public BitmapImage? ImageSource
        {
            get => _imageSource;
            set => SetProperty(ref _imageSource, value);
        }

        private string _fileName = string.Empty;
        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value);
        }

        private string _windowTitle = "Photo";
        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        private string _imageDimensions = string.Empty;
        public string ImageDimensions
        {
            get => _imageDimensions;
            set => SetProperty(ref _imageDimensions, value);
        }

        private string _fileSize = string.Empty;
        public string FileSize
        {
            get => _fileSize;
            set => SetProperty(ref _fileSize, value);
        }

        private string _zoomPercent = "100%";
        public string ZoomPercent
        {
            get => _zoomPercent;
            set => SetProperty(ref _zoomPercent, value);
        }

        private double _zoomSliderValue = 100;
        public double ZoomSliderValue
        {
            get => _zoomSliderValue;
            set
            {
                if (SetProperty(ref _zoomSliderValue, value))
                {
                    ZoomSliderValueChanged?.Invoke(value);
                }
            }
        }

        private bool _isImageLoaded;
        public bool IsImageLoaded
        {
            get => _isImageLoaded;
            set
            {
                if (SetProperty(ref _isImageLoaded, value))
                {
                    OnPropertyChanged(nameof(PlaceholderVisibility));
                    OnPropertyChanged(nameof(IsImageInteractionEnabled));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        private bool _isVideo;
        public bool IsVideo
        {
            get => _isVideo;
            set
            {
                if (SetProperty(ref _isVideo, value))
                {
                    OnPropertyChanged(nameof(ImageVisibility));
                    OnPropertyChanged(nameof(VideoVisibility));
                    OnPropertyChanged(nameof(IsImageInteractionEnabled));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public Visibility ImageVisibility => IsVideo ? Visibility.Collapsed : Visibility.Visible;
        public Visibility VideoVisibility => IsVideo ? Visibility.Visible : Visibility.Collapsed;

        public bool IsImageInteractionEnabled => IsImageLoaded && !IsVideo;

        private StorageFile? _videoFile;
        public StorageFile? VideoFile
        {
            get => _videoFile;
            set => SetProperty(ref _videoFile, value);
        }

        public Visibility PlaceholderVisibility => IsImageLoaded ? Visibility.Collapsed : Visibility.Visible;

        private bool _isPreviousButtonVisible;
        public bool IsPreviousButtonVisible
        {
            get => _isPreviousButtonVisible;
            set
            {
                if (SetProperty(ref _isPreviousButtonVisible, value))
                {
                    OnPropertyChanged(nameof(PreviousButtonVisibility));
                    (NavigatePreviousCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public Visibility PreviousButtonVisibility => IsPreviousButtonVisible ? Visibility.Visible : Visibility.Collapsed;

        private bool _isNextButtonVisible;
        public bool IsNextButtonVisible
        {
            get => _isNextButtonVisible;
            set
            {
                if (SetProperty(ref _isNextButtonVisible, value))
                {
                    OnPropertyChanged(nameof(NextButtonVisibility));
                    (NavigateNextCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public Visibility NextButtonVisibility => IsNextButtonVisible ? Visibility.Visible : Visibility.Collapsed;

        private bool _isThumbnailBarVisible;
        public bool IsThumbnailBarVisible
        {
            get => _isThumbnailBarVisible;
            set
            {
                if (SetProperty(ref _isThumbnailBarVisible, value))
                {
                    OnPropertyChanged(nameof(ThumbnailBarVisibility));
                    OnPropertyChanged(nameof(ThumbnailToggleIcon));
                    OnPropertyChanged(nameof(ThumbnailToggleTooltip));
                }
            }
        }

        public Visibility ThumbnailBarVisibility => IsThumbnailBarVisible ? Visibility.Visible : Visibility.Collapsed;
        public string ThumbnailToggleIcon => IsThumbnailBarVisible ? "\uE70D" : "\uE8FD";
        public string ThumbnailToggleTooltip => IsThumbnailBarVisible ? "隐藏缩略图" : "显示缩略图";

        private bool _isFileInfoPanelVisible;
        public bool IsFileInfoPanelVisible
        {
            get => _isFileInfoPanelVisible;
            set
            {
                if (SetProperty(ref _isFileInfoPanelVisible, value))
                {
                    OnPropertyChanged(nameof(FileInfoPanelVisibility));
                }
            }
        }

        public Visibility FileInfoPanelVisibility => IsFileInfoPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        private bool _isFullScreen;
        public bool IsFullScreen
        {
            get => _isFullScreen;
            set
            {
                if (SetProperty(ref _isFullScreen, value))
                {
                    OnPropertyChanged(nameof(FullScreenIcon));
                    OnPropertyChanged(nameof(FullScreenTooltip));
                    OnPropertyChanged(nameof(ToolbarVisibility));
                    FullScreenChanged?.Invoke(value);
                }
            }
        }

        public string FullScreenIcon => IsFullScreen ? "\uE73F" : "\uE740";
        public string FullScreenTooltip => IsFullScreen ? "退出全屏" : "全屏";
        public Visibility ToolbarVisibility => IsFullScreen ? Visibility.Collapsed : Visibility.Visible;

        // 文件信息面板属性
        private string _infoFileName = string.Empty;
        public string InfoFileName
        {
            get => _infoFileName;
            set => SetProperty(ref _infoFileName, value);
        }

        private string _infoFilePath = string.Empty;
        public string InfoFilePath
        {
            get => _infoFilePath;
            set => SetProperty(ref _infoFilePath, value);
        }

        private string _infoFileType = string.Empty;
        public string InfoFileType
        {
            get => _infoFileType;
            set => SetProperty(ref _infoFileType, value);
        }

        private string _infoDimensions = string.Empty;
        public string InfoDimensions
        {
            get => _infoDimensions;
            set => SetProperty(ref _infoDimensions, value);
        }

        private string _infoFileSize = string.Empty;
        public string InfoFileSize
        {
            get => _infoFileSize;
            set => SetProperty(ref _infoFileSize, value);
        }

        private string _infoCreatedDate = string.Empty;
        public string InfoCreatedDate
        {
            get => _infoCreatedDate;
            set => SetProperty(ref _infoCreatedDate, value);
        }

        private string _infoModifiedDate = string.Empty;
        public string InfoModifiedDate
        {
            get => _infoModifiedDate;
            set => SetProperty(ref _infoModifiedDate, value);
        }

        private string _infoCameraModel = string.Empty;
        public string InfoCameraModel { get => _infoCameraModel; set => SetProperty(ref _infoCameraModel, value); }

        private string _infoFNumber = string.Empty;
        public string InfoFNumber { get => _infoFNumber; set => SetProperty(ref _infoFNumber, value); }

        private string _infoExposureTime = string.Empty;
        public string InfoExposureTime { get => _infoExposureTime; set => SetProperty(ref _infoExposureTime, value); }

        private string _infoISO = string.Empty;
        public string InfoISO { get => _infoISO; set => SetProperty(ref _infoISO, value); }

        private string _infoFocalLength = string.Empty;
        public string InfoFocalLength { get => _infoFocalLength; set => SetProperty(ref _infoFocalLength, value); }

        private string _infoDateTimeOriginal = string.Empty;
        public string InfoDateTimeOriginal { get => _infoDateTimeOriginal; set => SetProperty(ref _infoDateTimeOriginal, value); }

        private string _infoKeywords = string.Empty;
        public string InfoKeywords { get => _infoKeywords; set => SetProperty(ref _infoKeywords, value); }

        private string _infoPeople = string.Empty;
        public string InfoPeople { get => _infoPeople; set => SetProperty(ref _infoPeople, value); }

        public ObservableCollection<FaceRegionItem> FaceRegions { get; } = new();

        private int _imageWidth;
        public int ImageWidth
        {
            get => _imageWidth;
            set => SetProperty(ref _imageWidth, value);
        }

        private int _imageHeight;
        public int ImageHeight
        {
            get => _imageHeight;
            set => SetProperty(ref _imageHeight, value);
        }

        #endregion

        #region Collections

        public ObservableCollection<ThumbnailItem> ThumbnailItems { get; } = new();

        #endregion

        #region Events for View

        public event Action? RequestFitToWindow;
        public event Action<double>? ZoomSliderValueChanged;
        public event Action<bool>? FullScreenChanged;
        public event Action? ThumbnailSelectionChanged;

        #endregion

        #region Commands

        public ICommand RotateCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand OpenInExplorerCommand { get; }
        public ICommand ToggleFileInfoCommand { get; }
        public ICommand CloseFileInfoCommand { get; }
        public ICommand ToggleThumbnailBarCommand { get; }
        public ICommand ToggleFullScreenCommand { get; }
        public ICommand NavigatePreviousCommand { get; }
        public ICommand NavigateNextCommand { get; }
        public ICommand FitToWindowCommand { get; }
        public ICommand ActualSizeCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand Zoom25Command { get; }
        public ICommand Zoom50Command { get; }
        public ICommand Zoom100Command { get; }
        public ICommand Zoom200Command { get; }
        public ICommand Zoom400Command { get; }
        public ICommand OpenSettingsCommand { get; }

        #endregion

        #region Constructor

        public MainWindowViewModel(
            IImageService imageService,
            IDialogService dialogService,
            IFileWatcherService fileWatcherService,
            ISettingsService settingsService,
            IClipboardService clipboardService,
            IExplorerService explorerService,
            DispatcherQueue dispatcherQueue)
        {
            _imageService = imageService;
            _dialogService = dialogService;
            _fileWatcherService = fileWatcherService;
            _settingsService = settingsService;
            _clipboardService = clipboardService;
            _explorerService = explorerService;
            _dispatcherQueue = dispatcherQueue;

            // 初始化命令
            RotateCommand = new AsyncRelayCommand(RotateAsync, () => IsImageInteractionEnabled);
            DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => IsImageLoaded);
            SaveAsCommand = new AsyncRelayCommand(SaveAsAsync, () => IsImageLoaded);
            CopyCommand = new AsyncRelayCommand(CopyAsync, () => IsImageLoaded);
            OpenInExplorerCommand = new RelayCommand(OpenInExplorer, () => IsImageLoaded);
            ToggleFileInfoCommand = new RelayCommand(ToggleFileInfo, () => IsImageLoaded);
            CloseFileInfoCommand = new RelayCommand(() => IsFileInfoPanelVisible = false);
            ToggleThumbnailBarCommand = new RelayCommand(ToggleThumbnailBar);
            ToggleFullScreenCommand = new RelayCommand(() => IsFullScreen = !IsFullScreen);
            NavigatePreviousCommand = new AsyncRelayCommand(() => NavigateAsync(-1), () => IsPreviousButtonVisible);
            NavigateNextCommand = new AsyncRelayCommand(() => NavigateAsync(1), () => IsNextButtonVisible);
            FitToWindowCommand = new RelayCommand(() => RequestFitToWindow?.Invoke(), () => IsImageInteractionEnabled);
            ActualSizeCommand = new RelayCommand(() => SetZoom(1.0f), () => IsImageInteractionEnabled);
            ZoomInCommand = new RelayCommand(ZoomIn, () => IsImageInteractionEnabled);
            ZoomOutCommand = new RelayCommand(ZoomOut, () => IsImageInteractionEnabled);
            Zoom25Command = new RelayCommand(() => SetZoom(0.25f), () => IsImageInteractionEnabled);
            Zoom50Command = new RelayCommand(() => SetZoom(0.5f), () => IsImageInteractionEnabled);
            Zoom100Command = new RelayCommand(() => SetZoom(1.0f), () => IsImageInteractionEnabled);
            Zoom200Command = new RelayCommand(() => SetZoom(2.0f), () => IsImageInteractionEnabled);
            Zoom400Command = new RelayCommand(() => SetZoom(4.0f), () => IsImageInteractionEnabled);
            OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);

            // 文件监视
            _fileWatcherService.FilesChanged += OnFilesChanged;

            // 初始化定时器
            InitializeFolderUpdateTimer();
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            (RotateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (DeleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SaveAsCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CopyCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (OpenInExplorerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleFileInfoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FitToWindowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ActualSizeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ZoomInCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ZoomOutCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (Zoom25Command as RelayCommand)?.RaiseCanExecuteChanged();
            (Zoom50Command as RelayCommand)?.RaiseCanExecuteChanged();
            (Zoom100Command as RelayCommand)?.RaiseCanExecuteChanged();
            (Zoom200Command as RelayCommand)?.RaiseCanExecuteChanged();
            (Zoom400Command as RelayCommand)?.RaiseCanExecuteChanged();
            (NavigatePreviousCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (NavigateNextCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        #region Zoom Events (to communicate with View)

        public event Action<float>? RequestZoom;
        public event Action? RequestZoomIn;
        public event Action? RequestZoomOut;

        private void SetZoom(float zoom)
        {
            RequestZoom?.Invoke(zoom);
        }

        private void ZoomIn()
        {
            RequestZoomIn?.Invoke();
        }

        private void ZoomOut()
        {
            RequestZoomOut?.Invoke();
        }

        #endregion

        #region Public Methods

        public async Task LoadImageFromPathAsync(string path)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                await LoadImageAsync(file, true);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("无法打开文件", ex.Message);
            }
        }

        public async Task LoadImageAsync(StorageFile file, bool reloadFolder = true)
        {
            try
            {
                _currentFile = file;

                var imageInfo = await _imageService.LoadImageAsync(file);
                if (imageInfo == null)
                {
                    await _dialogService.ShowErrorAsync("无法加载文件", "无法读取文件");
                    return;
                }

                // 设置文件监视
                var folderPath = Path.GetDirectoryName(file.Path);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    _fileWatcherService.StartWatching(folderPath);
                }

                // 更新属性
                if (_imageService.IsVideoFile(file.FileType))
                {
                    IsVideo = true;
                    VideoFile = file;
                    ImageSource = null;
                }
                else
                {
                    IsVideo = false;
                    VideoFile = null;
                    ImageSource = imageInfo.Bitmap;
                }

                ImageWidth = imageInfo.Width;
                ImageHeight = imageInfo.Height;
                FileName = imageInfo.FileName;
                WindowTitle = $"{imageInfo.FileName} - Photo";
                ImageDimensions = $"{imageInfo.Width} x {imageInfo.Height}";
                FileSize = FormatFileSize(imageInfo.FileSize);
                IsImageLoaded = true;

                // 更新文件信息面板
                UpdateFileInfo(imageInfo);

                // 重新适应窗口
                await Task.Delay(100);
                RequestFitToWindow?.Invoke();

                if (reloadFolder)
                {
                    await UpdateFileListAsync();
                }
                else
                {
                    UpdateThumbnailSelection();
                }
                UpdateNavigationButtons();
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("无法加载文件", ex.Message);
            }
        }

        public async Task HandleDropAsync(StorageFile file)
        {
            if (_imageService.IsImageFile(file.FileType) || _imageService.IsVideoFile(file.FileType))
            {
                // 通过路径重新获取文件，以获得完整的读写权限
                // 拖拽进来的 StorageFile 是只读的，无法进行旋转等修改操作
                try
                {
                    var fileWithAccess = await StorageFile.GetFileFromPathAsync(file.Path);
                    await LoadImageAsync(fileWithAccess, true);
                }
                catch
                {
                    // 如果无法获取完整权限（例如系统保护目录），则使用原始只读文件
                    await LoadImageAsync(file, true);
                }
            }
        }

        public void UpdateZoomDisplay(float zoomFactor)
        {
            ZoomPercent = $"{(int)(zoomFactor * 100)}%";
            _zoomSliderValue = Math.Max(10, Math.Min(400, zoomFactor * 100));
            OnPropertyChanged(nameof(ZoomSliderValue));
        }

        public async Task HandleThumbnailSelectionAsync(ThumbnailItem item)
        {
            if (_isUpdatingThumbnailSelection) return;
            if (item.FilePath != _currentFile?.Path)
            {
                await LoadImageAsync(item.File, false);
                UpdateThumbnailSelection();
                UpdateNavigationButtons();
            }
        }

        public async Task HandleKeyDownAsync(Windows.System.VirtualKey key)
        {
            if (key == Windows.System.VirtualKey.Escape && IsFullScreen)
            {
                IsFullScreen = false;
                return;
            }

            if (!IsImageLoaded) return;

            if (key == Windows.System.VirtualKey.Left)
            {
                await NavigateAsync(-1);
            }
            else if (key == Windows.System.VirtualKey.Right)
            {
                await NavigateAsync(1);
            }
            else if (key == Windows.System.VirtualKey.Delete)
            {
                await DeleteAsync();
            }
        }

        #endregion

        #region Private Methods

        private void InitializeFolderUpdateTimer()
        {
            _folderUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _folderUpdateTimer.Tick += async (s, e) =>
            {
                _folderUpdateTimer.Stop();
                await UpdateFileListAsync();
                UpdateNavigationButtons();
            };
        }

        private void OnFilesChanged()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _folderUpdateTimer?.Stop();
                _folderUpdateTimer?.Start();
            });
        }

        private async Task UpdateFileListAsync()
        {
            if (_currentFile == null) return;

            _folderFiles = await _imageService.GetFolderImagesAsync(_currentFile);
            // 异步更新缩略图列表，不阻塞主流程（如导航按钮的更新）
            _ = UpdateThumbnailsAsync();
        }

        private void UpdateNavigationButtons()
        {
            if (_folderFiles.Count <= 1 || _currentFile == null)
            {
                IsPreviousButtonVisible = false;
                IsNextButtonVisible = false;
                return;
            }

            int currentIndex = _folderFiles.FindIndex(f => f.Path == _currentFile.Path);
            IsPreviousButtonVisible = currentIndex > 0;
            IsNextButtonVisible = currentIndex < _folderFiles.Count - 1;
        }

        private async Task NavigateAsync(int direction)
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
            if (newIndex < 0 || newIndex >= _folderFiles.Count) return;

            var nextFile = _folderFiles[newIndex];
            await LoadImageAsync(nextFile, false);
        }

        private void UpdateFileInfo(ImageInfo imageInfo)
        {
            InfoFileName = imageInfo.FileName;
            InfoFilePath = Path.GetDirectoryName(imageInfo.FilePath) ?? "";
            
            if (_imageService.IsVideoFile(imageInfo.FileType))
            {
                InfoFileType = imageInfo.FileType.ToUpperInvariant().TrimStart('.') + " 视频";
                if (imageInfo.Duration != TimeSpan.Zero)
                {
                    InfoDimensions = $"{imageInfo.Width} x {imageInfo.Height} ({imageInfo.Duration:hh\\:mm\\:ss})";
                }
                else
                {
                    InfoDimensions = $"{imageInfo.Width} x {imageInfo.Height}";
                }
            }
            else
            {
                InfoFileType = imageInfo.FileType.ToUpperInvariant().TrimStart('.') + " 图片";
                InfoDimensions = $"{imageInfo.Width} x {imageInfo.Height} 像素";
            }

            InfoFileSize = FormatFileSize(imageInfo.FileSize);
            InfoCreatedDate = imageInfo.CreatedDate.LocalDateTime.ToString("yyyy年M月d日 HH:mm");
            InfoModifiedDate = imageInfo.ModifiedDate.LocalDateTime.ToString("yyyy年M月d日 HH:mm");

            InfoCameraModel = imageInfo.CameraModel;
            InfoFNumber = imageInfo.FNumber;
            InfoExposureTime = imageInfo.ExposureTime;
            InfoISO = imageInfo.ISO;
            InfoFocalLength = imageInfo.FocalLength;
            InfoDateTimeOriginal = imageInfo.DateTimeOriginal?.ToString("yyyy年M月d日 HH:mm:ss") ?? "";
            InfoKeywords = string.Join(", ", imageInfo.Keywords);
            InfoPeople = string.Join(", ", imageInfo.People);

            FaceRegions.Clear();
            
            System.Diagnostics.Debug.WriteLine($"UpdateFileInfo: Found {imageInfo.FaceRegions.Count} face regions");
            
            foreach (var region in imageInfo.FaceRegions)
            {
                System.Diagnostics.Debug.WriteLine($"Adding face region: {region.Name} at ({region.X}, {region.Y}, {region.Width}, {region.Height})");
                FaceRegions.Add(new FaceRegionItem
                {
                    Name = region.Name,
                    X = region.X,
                    Y = region.Y,
                    Width = region.Width,
                    Height = region.Height
                });
            }
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

        private async Task RotateAsync()
        {
            if (_currentFile == null) return;

            var success = await _imageService.RotateImageAsync(_currentFile);
            if (success)
            {
                await LoadImageAsync(_currentFile, false);
            }
            else
            {
                await _dialogService.ShowErrorAsync("无法旋转图片", "图片旋转失败");
            }
        }

        private async Task DeleteAsync()
        {
            if (_currentFile == null) return;

            if (_settingsService.ConfirmBeforeDelete)
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    "删除文件",
                    $"确定要将 \"{_currentFile.Name}\" 移至回收站吗？",
                    "删除",
                    "取消");

                if (!confirmed) return;
            }

            try
            {
                StorageFile? targetFile = null;
                if (_folderFiles.Count > 1)
                {
                    int currentIndex = _folderFiles.FindIndex(f => f.Path == _currentFile.Path);
                    if (currentIndex >= 0)
                    {
                        if (currentIndex < _folderFiles.Count - 1)
                            targetFile = _folderFiles[currentIndex + 1];
                        else if (currentIndex > 0)
                            targetFile = _folderFiles[currentIndex - 1];
                    }
                }

                var success = await _imageService.DeleteImageAsync(_currentFile);
                if (!success)
                {
                    await _dialogService.ShowErrorAsync("无法删除文件", "删除操作失败");
                    return;
                }

                if (targetFile != null)
                {
                    await LoadImageAsync(targetFile, true);
                }
                else
                {
                    ResetUI();
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("无法删除文件", ex.Message);
            }
        }

        private void ResetUI()
        {
            ImageSource = null;
            _currentFile = null;
            IsImageLoaded = false;
            FileName = "";
            WindowTitle = "Photo";
            ImageDimensions = "";
            FileSize = "";
            IsFileInfoPanelVisible = false;
            ZoomPercent = "100%";
            _folderFiles.Clear();
            ThumbnailItems.Clear();
            UpdateNavigationButtons();
        }

        private async Task SaveAsAsync()
        {
            if (_currentFile == null) return;

            try
            {
                var targetFile = await _dialogService.PickSaveFileAsync(
                    Path.GetFileNameWithoutExtension(_currentFile.Name),
                    _currentFile.FileType);

                if (targetFile != null)
                {
                    var success = await _imageService.SaveAsAsync(_currentFile, targetFile);
                    if (!success)
                    {
                        await _dialogService.ShowErrorAsync("无法保存文件", "保存操作失败");
                    }
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("无法保存文件", ex.Message);
            }
        }

        private async Task CopyAsync()
        {
            if (_currentFile == null) return;

            var success = await _clipboardService.CopyImageAsync(_currentFile);
            if (!success)
            {
                await _dialogService.ShowErrorAsync("无法复制图片", "复制操作失败");
            }
        }

        private void OpenInExplorer()
        {
            if (_currentFile == null) return;
            _explorerService.OpenInExplorer(_currentFile.Path);
        }

        private void ToggleFileInfo()
        {
            if (!IsImageLoaded) return;
            IsFileInfoPanelVisible = !IsFileInfoPanelVisible;
        }

        private void ToggleThumbnailBar()
        {
            IsThumbnailBarVisible = !IsThumbnailBarVisible;

            if (IsThumbnailBarVisible && _currentFile != null)
            {
                ThumbnailSelectionChanged?.Invoke();
            }

            if (IsImageLoaded)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    _dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        RequestFitToWindow?.Invoke();
                    });
                });
            }
        }

        private async Task UpdateThumbnailsAsync()
        {
            _thumbnailCts?.Cancel();
            _thumbnailCts?.Dispose();
            _thumbnailCts = new System.Threading.CancellationTokenSource();
            var token = _thumbnailCts.Token;

            ThumbnailItems.Clear();

            if (_folderFiles.Count == 0) return;

            // 如果文件非常多，分批添加到集合中，防止 UI 长时间卡顿
            const int batchSize = 100;
            for (int i = 0; i < _folderFiles.Count; i += batchSize)
            {
                if (token.IsCancellationRequested) return;

                var batch = _folderFiles.Skip(i).Take(batchSize);
                foreach (var file in batch)
                {
                    var item = new ThumbnailItem(file)
                    {
                        IsSelected = file.Path == _currentFile?.Path
                    };
                    ThumbnailItems.Add(item);
                }

                // 给 UI 线程喘息的机会
                if (_folderFiles.Count > batchSize)
                {
                    await Task.Yield();
                }
            }

            // 不要在主流程中等待所有缩略图加载完成
            _ = LoadThumbnailsAsync(token);
        }

        private async Task LoadThumbnailsAsync(System.Threading.CancellationToken token)
        {
            if (ThumbnailItems.Count == 0) return;

            // 优先加载当前图片附近的缩略图
            int currentIndex = _folderFiles.FindIndex(f => f.Path == _currentFile?.Path);
            if (currentIndex == -1) currentIndex = 0;

            var itemsToLoad = ThumbnailItems.ToList();
            // 按距离当前索引的距离排序，优先加载近的
            var sortedItems = itemsToLoad.OrderBy(item => 
            {
                int index = itemsToLoad.IndexOf(item);
                return Math.Abs(index - currentIndex);
            }).ToList();

            var semaphore = new System.Threading.SemaphoreSlim(4);
            var tasks = sortedItems.Select(item => LoadSingleThumbnailAsync(item, semaphore, token));
            await Task.WhenAll(tasks);
        }

        private async Task LoadSingleThumbnailAsync(ThumbnailItem item, System.Threading.SemaphoreSlim semaphore, System.Threading.CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            await semaphore.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;

                var thumbnail = await _imageService.GetThumbnailAsync(item.File);
                
                if (token.IsCancellationRequested) return;

                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;
                    if (thumbnail != null)
                    {
                        item.Thumbnail = thumbnail;
                    }
                    item.IsLoading = false;
                });
            }
            catch
            {
                _dispatcherQueue.TryEnqueue(() => item.IsLoading = false);
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

            _isUpdatingThumbnailSelection = false;
            ThumbnailSelectionChanged?.Invoke();
        }

        private async Task OpenSettingsAsync()
        {
            // 这个需要在 View 层处理，因为需要 XamlRoot
            // 通过事件通知 View
            SettingsRequested?.Invoke();
        }

        public event Action? SettingsRequested;

        #endregion
    }

    public class FaceRegionItem
    {
        public string Name { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Tooltip => Name;
    }
}
