using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using MetadataExtractor;
using MetadataExtractor.Formats.Xmp;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using XmpCore;

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
    /// 人脸区域信息
    /// </summary>
    public class FaceRegion
    {
        public string Name { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
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

        // Metadata
        public List<string> Keywords { get; set; } = new();
        public List<string> People { get; set; } = new();
        public List<FaceRegion> FaceRegions { get; set; } = new();
        public string CameraModel { get; set; } = string.Empty;
        public string FNumber { get; set; } = string.Empty;
        public string ExposureTime { get; set; } = string.Empty;
        public string ISO { get; set; } = string.Empty;
        public string FocalLength { get; set; } = string.Empty;
        public DateTime? DateTimeOriginal { get; set; }
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

                // 提取元数据
                await ExtractMetadataAsync(file, imageInfo);

                return imageInfo;
            }
            catch
            {
                return null;
            }
        }

        private async Task ExtractMetadataAsync(StorageFile file, ImageInfo imageInfo)
        {
            try
            {
                using var stream = await file.OpenStreamForReadAsync();
                var directories = ImageMetadataReader.ReadMetadata(stream);

                // Exif
                var exifSubIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                if (exifSubIfdDirectory != null)
                {
                    imageInfo.DateTimeOriginal = exifSubIfdDirectory.GetDescription(ExifDirectoryBase.TagDateTimeOriginal) is string dateStr 
                        ? (DateTime.TryParseExact(dateStr, "yyyy:MM:dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var dt) ? dt : null) 
                        : null;
                    imageInfo.FNumber = exifSubIfdDirectory.GetDescription(ExifDirectoryBase.TagFNumber) ?? "";
                    imageInfo.ExposureTime = exifSubIfdDirectory.GetDescription(ExifDirectoryBase.TagExposureTime) ?? "";
                    imageInfo.ISO = exifSubIfdDirectory.GetDescription(ExifDirectoryBase.TagIsoEquivalent) ?? "";
                    imageInfo.FocalLength = exifSubIfdDirectory.GetDescription(ExifDirectoryBase.TagFocalLength) ?? "";
                }

                var exifIfd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                if (exifIfd0Directory != null)
                {
                    imageInfo.CameraModel = exifIfd0Directory.GetDescription(ExifDirectoryBase.TagModel) ?? "";
                }

                // IPTC Keywords
                var iptcDirectory = directories.OfType<IptcDirectory>().FirstOrDefault();
                if (iptcDirectory != null)
                {
                    var keywords = iptcDirectory.GetDescription(IptcDirectory.TagKeywords);
                    if (keywords != null)
                    {
                        imageInfo.Keywords.Add(keywords);
                    }
                }

                // XMP
                var xmpDirectory = directories.OfType<XmpDirectory>().FirstOrDefault();
                if (xmpDirectory != null && xmpDirectory.XmpMeta != null)
                {
                    var xmp = xmpDirectory.XmpMeta;

                    // Keywords (dc:subject)
                    try 
                    {
                        var subjects = xmp.GetProperty(XmpConstants.NsDC, "subject");
                        if (subjects != null)
                        {
                            // XMP subject is usually a Bag
                            var count = xmp.CountArrayItems(XmpConstants.NsDC, "subject");
                            for (int i = 1; i <= count; i++)
                            {
                                var item = xmp.GetArrayItem(XmpConstants.NsDC, "subject", i);
                                if (item != null && !string.IsNullOrEmpty(item.Value) && !imageInfo.Keywords.Contains(item.Value))
                                {
                                    imageInfo.Keywords.Add(item.Value);
                                }
                            }
                        }
                    }
                    catch {}

                    // Microsoft Photo Regions - try multiple approaches
                    try
                    {
                        // Check for Microsoft Photo 1.2 regions
                        string nsMP = "http://ns.microsoft.com/photo/1.2/";
                        string nsMPRI = "http://ns.microsoft.com/photo/1.2/t/RegionInfo#";
                        string nsMPReg = "http://ns.microsoft.com/photo/1.2/t/Region#";

                        // Try to read RegionInfo structure
                        // First, check if RegionInfo exists
                        if (xmp.DoesPropertyExist(nsMP, "RegionInfo"))
                        {
                            System.Diagnostics.Debug.WriteLine("Found RegionInfo in XMP");
                            
                            // Try different path formats
                            string[] pathFormats = new[] {
                                "RegionInfo/MPRI:Regions",
                                "RegionInfo/Regions"
                            };
                            
                            int count = 0;
                            string workingPath = "";
                            
                            foreach (var pathFormat in pathFormats)
                            {
                                try {
                                    count = xmp.CountArrayItems(nsMP, pathFormat);
                                    System.Diagnostics.Debug.WriteLine($"Trying path {pathFormat}: count = {count}");
                                    if (count > 0) {
                                        workingPath = pathFormat;
                                        break;
                                    }
                                } catch (Exception ex) {
                                    System.Diagnostics.Debug.WriteLine($"Path {pathFormat} failed: {ex.Message}");
                                }
                            }
                            
                            // Also try with nsMPRI namespace
                            if (count == 0)
                            {
                                try {
                                    count = xmp.CountArrayItems(nsMPRI, "Regions");
                                    if (count > 0) {
                                        workingPath = "Regions";
                                        System.Diagnostics.Debug.WriteLine($"Found {count} regions using nsMPRI:Regions");
                                    }
                                } catch {}
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"Final: Found {count} face regions using path: {workingPath}");
                            
                            if (count > 0)
                            {
                                for (int i = 1; i <= count; i++)
                                {
                                    string itemPath = workingPath + "[" + i + "]";
                                    
                                    string name = "";
                                    string rect = "";
                                    
                                    // Try different namespace combinations for PersonDisplayName
                                    string[] fieldNamespaces = new[] { nsMPReg, nsMP, nsMPRI };
                                    foreach (var ns in fieldNamespaces)
                                    {
                                        try {
                                            var nameProp = xmp.GetStructField(nsMP, itemPath, ns, "PersonDisplayName");
                                            if (nameProp != null && !string.IsNullOrEmpty(nameProp.Value)) {
                                                name = nameProp.Value;
                                                System.Diagnostics.Debug.WriteLine($"Found PersonDisplayName using ns {ns}: {name}");
                                                break;
                                            }
                                        } catch {}
                                    }
                                    
                                    // Try different namespace combinations for Rectangle
                                    foreach (var ns in fieldNamespaces)
                                    {
                                        try {
                                            var rectProp = xmp.GetStructField(nsMP, itemPath, ns, "Rectangle");
                                            if (rectProp != null && !string.IsNullOrEmpty(rectProp.Value)) {
                                                rect = rectProp.Value;
                                                System.Diagnostics.Debug.WriteLine($"Found Rectangle using ns {ns}: {rect}");
                                                break;
                                            }
                                        } catch {}
                                    }
                                    
                                    System.Diagnostics.Debug.WriteLine($"Region {i}: Name='{name}', Rect='{rect}'");
                                        
                                    if (!string.IsNullOrEmpty(rect))
                                    {
                                        // Rectangle format: "x, y, w, h" (with spaces after commas)
                                        var parts = rect.Split(',');
                                        if (parts.Length == 4)
                                        {
                                            if (double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x) &&
                                                double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y) &&
                                                double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double w) &&
                                                double.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double h))
                                            {
                                                imageInfo.FaceRegions.Add(new FaceRegion
                                                {
                                                    Name = name,
                                                    X = x,
                                                    Y = y,
                                                    Width = w,
                                                    Height = h
                                                });
                                                
                                                System.Diagnostics.Debug.WriteLine($"Added face region: {name} at ({x}, {y}, {w}, {h})");
                                                
                                                if (!string.IsNullOrEmpty(name) && !imageInfo.People.Contains(name))
                                                {
                                                    imageInfo.People.Add(name);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("No RegionInfo found in XMP");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Error parsing XMP regions: " + ex.Message);
                    }
                    
                    // Also try to read from raw XMP string as fallback
                    if (imageInfo.FaceRegions.Count == 0)
                    {
                        try
                        {
                            var xmpRaw = xmpDirectory.GetDescription(XmpDirectory.TagXmpValueCount);
                            System.Diagnostics.Debug.WriteLine($"XMP raw description: {xmpRaw}");
                            
                            // Try to iterate all properties
                            var props = xmpDirectory.Tags;
                            foreach (var tag in props)
                            {
                                System.Diagnostics.Debug.WriteLine($"XMP Tag: {tag.Name} = {tag.Description}");
                            }
                        }
                        catch {}
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error extracting metadata: " + ex.Message);
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
                if (folder == null) return new List<StorageFile> { currentFile };

                // 1. 快速获取文件列表（保持之前的性能优化）
                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, _imageExtensions);
                queryOptions.IndexerOption = IndexerOption.DoNotUseIndexer;
                var queryResult = folder.CreateFileQueryWithOptions(queryOptions);
                var files = await queryResult.GetFilesAsync();
                var fileList = files.ToList();

                if (fileList.Count <= 1) return fileList;

                // 2. 尝试获取 Shell 排序顺序（匹配资源管理器的当前视图）
                try
                {
                    string folderPath = folder.Path;
                    var shellOrder = await Task.Run(() =>
                    {
                        var pathOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                        if (shellType != null)
                        {
                            object? shellObj = Activator.CreateInstance(shellType);
                            if (shellObj != null)
                            {
                                dynamic shell = shellObj;
                                // 优先尝试从当前打开的资源管理器窗口获取顺序
                                dynamic windows = shell.Windows();
                                bool foundWindow = false;
                                for (int i = 0; i < windows.Count; i++)
                                {
                                    try
                                    {
                                        dynamic window = windows.Item(i);
                                        if (window != null)
                                        {
                                            string? locationUrl = window.LocationURL;
                                            if (!string.IsNullOrEmpty(locationUrl))
                                            {
                                                string windowPath = new Uri(locationUrl).LocalPath;
                                                if (string.Equals(windowPath, folderPath, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    // 获取该窗口视图中的项顺序
                                                    dynamic document = window.Document;
                                                    if (document != null)
                                                    {
                                                        dynamic folderObj = document.Folder;
                                                        if (folderObj != null)
                                                        {
                                                            dynamic items = folderObj.Items();
                                                            int index = 0;
                                                            foreach (dynamic item in items)
                                                            {
                                                                pathOrder[item.Path] = index++;
                                                            }
                                                            foundWindow = true;
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch { /* 忽略无法访问的窗口 */ }
                                }

                                // 如果没找到打开的窗口，则获取该文件夹的默认 Shell 顺序
                                if (!foundWindow)
                                {
                                    dynamic shellFolder = shell.NameSpace(folderPath);
                                    if (shellFolder != null)
                                    {
                                        dynamic items = shellFolder.Items();
                                        int index = 0;
                                        foreach (dynamic item in items)
                                        {
                                            pathOrder[item.Path] = index++;
                                        }
                                    }
                                }
                            }
                        }
                        return pathOrder;
                    });

                    if (shellOrder.Count > 0)
                    {
                        // 根据 Shell 顺序对文件列表进行排序
                        return fileList.OrderBy(f => shellOrder.TryGetValue(f.Path, out int index) ? index : int.MaxValue).ToList();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Shell sorting failed: " + ex.Message);
                }

                // 3. 回退：如果无法获取 Shell 顺序，则使用默认顺序（通常是按名称）
                return fileList;
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
