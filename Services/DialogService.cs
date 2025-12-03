using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Photo.Services
{
    /// <summary>
    /// 对话框服务接口
    /// </summary>
    public interface IDialogService
    {
        Task ShowErrorAsync(string title, string message);
        Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "确定", string cancelText = "取消");
        Task<StorageFile?> PickSaveFileAsync(string suggestedFileName, string currentExtension);
        void SetXamlRoot(XamlRoot xamlRoot);
        void SetWindowHandle(IntPtr hwnd);
    }

    /// <summary>
    /// 对话框服务实现
    /// </summary>
    public class DialogService : IDialogService
    {
        private XamlRoot? _xamlRoot;
        private IntPtr _hwnd;

        public void SetXamlRoot(XamlRoot xamlRoot)
        {
            _xamlRoot = xamlRoot;
        }

        public void SetWindowHandle(IntPtr hwnd)
        {
            _hwnd = hwnd;
        }

        public async Task ShowErrorAsync(string title, string message)
        {
            if (_xamlRoot == null) return;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = _xamlRoot
            };
            await dialog.ShowAsync();
        }

        public async Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "确定", string cancelText = "取消")
        {
            if (_xamlRoot == null) return false;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = confirmText,
                CloseButtonText = cancelText,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = _xamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        public async Task<StorageFile?> PickSaveFileAsync(string suggestedFileName, string currentExtension)
        {
            if (_hwnd == IntPtr.Zero) return null;

            var savePicker = new FileSavePicker();
            InitializeWithWindow.Initialize(savePicker, _hwnd);

            savePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            savePicker.SuggestedFileName = suggestedFileName;

            // 根据当前文件类型添加文件类型选项
            var extension = currentExtension.ToLowerInvariant();
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

            return await savePicker.PickSaveFileAsync();
        }
    }
}
