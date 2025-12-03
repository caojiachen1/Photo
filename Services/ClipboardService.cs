using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Photo.Services
{
    /// <summary>
    /// 剪贴板服务接口
    /// </summary>
    public interface IClipboardService
    {
        Task<bool> CopyImageAsync(StorageFile file);
    }

    /// <summary>
    /// 剪贴板服务实现
    /// </summary>
    public class ClipboardService : IClipboardService
    {
        public Task<bool> CopyImageAsync(StorageFile file)
        {
            try
            {
                var dataPackage = new DataPackage
                {
                    RequestedOperation = DataPackageOperation.Copy
                };

                // 复制文件引用
                dataPackage.SetStorageItems(new[] { file });

                // 同时复制图片位图数据
                var stream = RandomAccessStreamReference.CreateFromFile(file);
                dataPackage.SetBitmap(stream);

                Clipboard.SetContent(dataPackage);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }

    /// <summary>
    /// 文件资源管理器服务接口
    /// </summary>
    public interface IExplorerService
    {
        void OpenInExplorer(string filePath);
    }

    /// <summary>
    /// 文件资源管理器服务实现
    /// </summary>
    public class ExplorerService : IExplorerService
    {
        public void OpenInExplorer(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
        }
    }
}
