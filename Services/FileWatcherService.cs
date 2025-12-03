using System;
using System.IO;

namespace Photo.Services
{
    /// <summary>
    /// 文件监视服务接口
    /// </summary>
    public interface IFileWatcherService
    {
        event Action? FilesChanged;
        void StartWatching(string folderPath);
        void StopWatching();
    }

    /// <summary>
    /// 文件监视服务实现
    /// </summary>
    public class FileWatcherService : IFileWatcherService, IDisposable
    {
        private FileSystemWatcher? _fileWatcher;
        private readonly string[] _imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".ico", ".tiff", ".tif" };

        public event Action? FilesChanged;

        public void StartWatching(string folderPath)
        {
            if (_fileWatcher != null && string.Equals(_fileWatcher.Path, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            StopWatching();

            try
            {
                if (Directory.Exists(folderPath))
                {
                    _fileWatcher = new FileSystemWatcher(folderPath)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                        Filter = "*.*"
                    };

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

        public void StopWatching()
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (IsImageFile(Path.GetExtension(e.FullPath)))
            {
                FilesChanged?.Invoke();
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (IsImageFile(Path.GetExtension(e.OldFullPath)) || IsImageFile(Path.GetExtension(e.FullPath)))
            {
                FilesChanged?.Invoke();
            }
        }

        private bool IsImageFile(string extension)
        {
            return Array.Exists(_imageExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        public void Dispose()
        {
            StopWatching();
        }
    }
}
