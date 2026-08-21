using System;
using System.Windows;

namespace MoShiOCR;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.Message, "墨识 OCR", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
