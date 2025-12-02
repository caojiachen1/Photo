using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace Photo
{
    public partial class App : Application
    {
        private MainWindow? _window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();

            // 处理命令行参数
            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Length > 1)
            {
                var filePath = cmdArgs[1];
                if (File.Exists(filePath))
                {
                    _ = _window.LoadImageFromPathAsync(filePath);
                }
            }
        }
    }
}
