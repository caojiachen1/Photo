using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace Photo
{
    /// <summary>
    /// 缩略图项目，用于绑定到ListView
    /// </summary>
    public class ThumbnailItem : INotifyPropertyChanged
    {
        private BitmapImage? _thumbnail;
        private bool _isLoading = true;
        private bool _isSelected = false;
        private SolidColorBrush _borderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        public StorageFile File { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }

        public BitmapImage? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (_thumbnail != value)
                {
                    _thumbnail = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    // 更新边框颜色
                    BorderBrush = value 
                        ? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue) 
                        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            }
        }

        public SolidColorBrush BorderBrush
        {
            get => _borderBrush;
            set
            {
                if (_borderBrush != value)
                {
                    _borderBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        public ThumbnailItem(StorageFile file)
        {
            File = file;
            FilePath = file.Path;
            FileName = file.Name;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
