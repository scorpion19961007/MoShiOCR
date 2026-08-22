using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace MoShiOCR;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _trayIconImage;
    private bool _isExiting;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.Message, "墨识 OCR", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var settings = SettingsStore.LoadSettings();
        ThemeManager.Apply(settings.DarkMode);
        if (settings.StartWithWindows)
        {
            try { StartupManager.Apply(true); }
            catch { }
        }

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.Closing += MainWindow_Closing;
        CreateTrayIcon();
        var silent = e.Args.Any(arg => string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase));
        if (silent)
        {
            new WindowInteropHelper(_mainWindow).EnsureHandle();
        }
        else
        {
            _mainWindow.Show();
        }
    }

    private void CreateTrayIcon()
    {
        if (_trayIcon is not null) return;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开墨识 OCR", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            try { _trayIconImage = Icon.ExtractAssociatedIcon(executablePath); }
            catch { }
        }

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "墨识 OCR",
            Icon = _trayIconImage ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting) return;

        e.Cancel = true;
        _mainWindow?.Hide();
        if (!_trayHintShown && _trayIcon is not null)
        {
            _trayHintShown = true;
            _trayIcon.ShowBalloonTip(1800, "墨识 OCR", "已缩小到系统托盘", Forms.ToolTipIcon.Info);
        }
    }

    private void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainWindow is null) return;
            if (!_mainWindow.IsVisible) _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private void ExitApplication()
    {
        Dispatcher.Invoke(() =>
        {
            _isExiting = true;
            _mainWindow?.Close();
            Shutdown();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
        }
        _trayIconImage?.Dispose();
        base.OnExit(e);
    }
}
