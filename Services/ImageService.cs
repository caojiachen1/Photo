using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Photo.Services
{
    /// <summary>
    /// 图片服务接口
    /// </summary>
    public interface IImageService
    {
        Task<ImageInfo?> LoadImageAsync(StorageFile file);
        Task<bool> RotateImageAsync(StorageFile file);
        Task<bool> DeleteImageAsync(StorageFile file);
        Task<bool> SaveAsAsync(StorageFile sourceFile, StorageFile targetFile);
        Task<List<StorageFile>> GetFolderImagesAsync(StorageFile currentFile);
        Task<BitmapImage?> GetThumbnailAsync(StorageFile file, uint size = 200);
        bool IsImageFile(string extension);
    }

    /// <summary>
    /// 图片信息
    /// </summary>
    public class ImageInfo
    {
        public BitmapImage? Bitmap { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long FileSize { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public DateTimeOffset CreatedDate { get; set; }
        public DateTimeOffset ModifiedDate { get; set; }
    }

    /// <summary>
    /// 图片服务实现
    /// </summary>
    public class ImageService : IImageService
    {
        private readonly string[] _imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".ico", ".tiff", ".tif" };
        private readonly DispatcherQueue _dispatcherQueue;

        public ImageService(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }

        public bool IsImageFile(string extension)
        {
            return _imageExtensions.Contains(extension.ToLowerInvariant());
        }

        public async Task<ImageInfo?> LoadImageAsync(StorageFile file)
        {
            try
            {
                var imageInfo = new ImageInfo
                {
                    FileName = file.Name,
                    FilePath = file.Path,
                    FileType = file.FileType
                };

                // 加载图片
                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(stream);

                    imageInfo.Bitmap = bitmapImage;
                    imageInfo.Width = bitmapImage.PixelWidth;
                    imageInfo.Height = bitmapImage.PixelHeight;
                }

                // 获取文件属性
                var properties = await file.GetBasicPropertiesAsync();
                imageInfo.FileSize = (long)properties.Size;
                imageInfo.ModifiedDate = properties.DateModified;
                imageInfo.CreatedDate = properties.ItemDate;

                return imageInfo;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> RotateImageAsync(StorageFile file)
        {
            try
            {
                // 读取原始图片
                using var inputStream = await file.OpenAsync(FileAccessMode.Read);
                var decoder = await BitmapDecoder.CreateAsync(inputStream);

                // 创建临时文件来保存旋转后的图片
                var tempFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(
                    "temp_rotated" + file.FileType,
                    CreationCollisionOption.ReplaceExisting);

                using (var outputStream = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateForTranscodingAsync(outputStream, decoder);
                    encoder.BitmapTransform.Rotation = BitmapRotation.Clockwise90Degrees;
                    await encoder.FlushAsync();
                }

                // 复制回原文件
                await tempFile.CopyAndReplaceAsync(file);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteImageAsync(StorageFile file)
        {
            try
            {
                await file.DeleteAsync(StorageDeleteOption.Default);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SaveAsAsync(StorageFile sourceFile, StorageFile targetFile)
        {
            try
            {
                await sourceFile.CopyAndReplaceAsync(targetFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<StorageFile>> GetFolderImagesAsync(StorageFile currentFile)
        {
            try
            {
                var folder = await currentFile.GetParentAsync();
                if (folder != null)
                {
                    var files = await folder.GetFilesAsync();
                    return files.Where(f => IsImageFile(f.FileType))
                                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                                .ToList();
                }
                return new List<StorageFile> { currentFile };
            }
            catch
            {
                return new List<StorageFile> { currentFile };
            }
        }

        public async Task<BitmapImage?> GetThumbnailAsync(StorageFile file, uint size = 200)
        {
            try
            {
                var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.ResizeThumbnail);
                if (thumbnail != null)
                {
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(thumbnail);
                    return bitmapImage;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
