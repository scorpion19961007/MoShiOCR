using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MoShiOCR;

public partial class MainWindow : Window
{
    private const int ScreenshotHotkeyId = 0x4D4F;
    private const int RecognitionHotkeyId = 0x4D50;
    private const int TranslationHotkeyId = 0x4D51;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;

    private readonly ApiClient _api = new();
    private readonly ObservableCollection<HistoryItem> _history;
    private AppSettings _settings;
    private byte[]? _imageBytes;
    private string _mimeType = "image/png";
    private CancellationTokenSource? _operation;
    private HwndSource? _source;
    private bool _hotkeysReady = true;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.LoadSettings();
        _history = new ObservableCollection<HistoryItem>(SettingsStore.LoadHistory());
        HistoryList.ItemsSource = _history;
        Loaded += (_, _) =>
        {
            RefreshSettingsSummary();
            if (!_hotkeysReady) SetStatus("部分快捷键已被其他程序占用，请在设置中更换", true);
        };
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source.AddHook(WndProc);
        _hotkeysReady = RegisterAllHotkeys();
    }

    protected override void OnClosed(EventArgs e)
    {
        _operation?.Cancel();
        if (_source is not null)
        {
            UnregisterAllHotkeys();
            _source.RemoveHook(WndProc);
        }
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x0312) return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case ScreenshotHotkeyId:
                Capture_Click(this, new RoutedEventArgs());
                handled = true;
                break;
            case RecognitionHotkeyId:
                BringToFront();
                _ = RunOcrAsync();
                handled = true;
                break;
            case TranslationHotkeyId:
                BringToFront();
                _ = RunTranslateAsync();
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    private bool RegisterAllHotkeys()
    {
        if (_source is null) return false;
        UnregisterAllHotkeys();
        var screenshotOk = RegisterConfiguredHotkey(ScreenshotHotkeyId, _settings.ScreenshotHotkey);
        var recognitionOk = RegisterConfiguredHotkey(RecognitionHotkeyId, _settings.RecognitionHotkey);
        var translationOk = RegisterConfiguredHotkey(TranslationHotkeyId, _settings.TranslationHotkey);
        return screenshotOk && recognitionOk && translationOk;
    }

    private bool RegisterConfiguredHotkey(int id, string shortcut)
    {
        if (string.Equals(shortcut, "Disabled", StringComparison.OrdinalIgnoreCase)) return true;
        if (_source is null || !TryParseHotkey(shortcut, out var modifiers, out var key)) return false;
        return RegisterHotKey(_source.Handle, id, modifiers | ModNoRepeat, key);
    }

    private void UnregisterAllHotkeys()
    {
        if (_source is null) return;
        UnregisterHotKey(_source.Handle, ScreenshotHotkeyId);
        UnregisterHotKey(_source.Handle, RecognitionHotkeyId);
        UnregisterHotKey(_source.Handle, TranslationHotkeyId);
    }

    private static bool TryParseHotkey(string shortcut, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToUpperInvariant() switch
            {
                "CTRL" => ModControl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                _ => 0
            };
        }
        var keyName = parts[^1];
        if (Enum.TryParse<Key>(keyName, true, out var parsedKey))
            key = (uint)KeyInterop.VirtualKeyFromKey(parsedKey);
        return modifiers != 0 && key != 0;
    }

    private void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.V && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            Paste_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要识别的图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) == true) LoadImage(File.ReadAllBytes(dialog.FileName), MimeFromPath(dialog.FileName), Path.GetFileName(dialog.FileName));
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsImage())
        {
            var bitmap = Clipboard.GetImage();
            if (bitmap is not null) LoadImage(EncodePng(bitmap), "image/png", "剪贴板图片");
            return;
        }

        if (Clipboard.ContainsFileDropList())
        {
            var path = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(IsSupportedImage);
            if (path is not null) { LoadImage(File.ReadAllBytes(path), MimeFromPath(path), Path.GetFileName(path)); return; }
        }
        SetStatus("剪贴板中没有可用图片", true);
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (!IsVisible) return;
        Hide();
        try
        {
            var capture = new ScreenCaptureWindow();
            if (capture.ShowDialog() == true && capture.CapturedBytes is not null)
            {
                LoadImage(capture.CapturedBytes, "image/png", "屏幕截图");
                Activate();
                _ = RunOcrAsync();
            }
        }
        finally
        {
            Show();
            Activate();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = dialog.Settings;
            var hotkeysOk = _hotkeysReady = RegisterAllHotkeys();
            RefreshSettingsSummary();
            SetStatus(hotkeysOk ? "设置已保存，快捷键已生效" : "设置已保存，但部分快捷键已被其他程序占用", !hotkeysOk);
        }
    }

    private async void Ocr_Click(object sender, RoutedEventArgs e) => await RunOcrAsync();

    private async Task RunOcrAsync()
    {
        if (_imageBytes is null) { SetStatus("请先打开、粘贴或截取图片", true); return; }
        await RunBusyAsync("正在识别图片...", async token =>
        {
            SourceText.Text = await _api.RecognizeAsync(
                _imageBytes,
                _settings,
                CredentialStore.Read(CredentialStore.OcrApiKey),
                CredentialStore.Read(CredentialStore.OcrSecretKey),
                token);
            TranslationText.Clear();
            AddHistory(SourceText.Text, "");
            SetStatus("识别完成");
        });
    }

    private async void Translate_Click(object sender, RoutedEventArgs e) => await RunTranslateAsync();

    private async Task RunTranslateAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceText.Text)) { SetStatus("没有可翻译的文本", true); return; }
        await RunBusyAsync($"正在翻译为{_settings.TargetLanguage}...", async token =>
        {
            TranslationText.Text = await _api.TranslateAsync(
                SourceText.Text,
                _settings,
                CredentialStore.Read(CredentialStore.TranslateAppId),
                CredentialStore.Read(CredentialStore.TranslateSecret),
                token);
            AddHistory(SourceText.Text, TranslationText.Text);
            SetStatus("翻译完成");
        });
    }

    private async Task RunBusyAsync(string message, Func<CancellationToken, Task> action)
    {
        _operation?.Cancel();
        _operation = new CancellationTokenSource();
        OcrButton.IsEnabled = TranslateButton.IsEnabled = false;
        SetStatus(message);
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { SetStatus("操作已取消"); }
        catch (Exception ex) { SetStatus(ex.Message, true); MessageBox.Show(this, ex.Message, "请求失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { OcrButton.IsEnabled = TranslateButton.IsEnabled = true; }
    }

    private void LoadImage(byte[] bytes, string mime, string label)
    {
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            var normalized = NormalizeForBaidu(image, bytes, mime);
            _imageBytes = normalized.Bytes;
            _mimeType = normalized.MimeType;
            PreviewImage.Source = image;
            PreviewImage.Visibility = Visibility.Visible;
            DropHint.Visibility = Visibility.Collapsed;
            ImageBadge.Visibility = RemoveImageButton.Visibility = Visibility.Visible;
            ImageInfo.Text = $"{label}  ·  {image.PixelWidth} × {image.PixelHeight}";
            SetStatus("图片已载入，可以开始识别");
        }
        catch { SetStatus("无法读取该图片，请尝试 PNG 或 JPG 格式", true); }
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        _imageBytes = null;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        DropHint.Visibility = Visibility.Visible;
        ImageBadge.Visibility = RemoveImageButton.Visibility = Visibility.Collapsed;
        SetStatus("图片已移除");
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var path = files.FirstOrDefault(IsSupportedImage);
            if (path is not null) LoadImage(File.ReadAllBytes(path), MimeFromPath(path), Path.GetFileName(path));
            else SetStatus("拖入的文件不是受支持的图片", true);
        }
    }

    private void SourceText_TextChanged(object sender, TextChangedEventArgs e) => SourceCount.Text = $"{SourceText.Text.Length} 字";

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedItem is ComboBoxItem item && _settings is not null)
        {
            _settings.TargetLanguage = item.Content.ToString() ?? "简体中文";
            SettingsStore.SaveSettings(_settings);
        }
    }

    private void CopySource_Click(object sender, RoutedEventArgs e) => CopyText(SourceText.Text, "识别文本已复制");
    private void CopyTranslation_Click(object sender, RoutedEventArgs e) => CopyText(TranslationText.Text, "译文已复制");

    private void CopyText(string text, string status)
    {
        if (string.IsNullOrEmpty(text)) { SetStatus("没有可复制的内容", true); return; }
        Clipboard.SetText(text);
        SetStatus(status);
    }

    private void AddHistory(string source, string translation)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        if (_history.FirstOrDefault() is { } first && first.SourceText == source)
        {
            first.Translation = translation;
            _history.Remove(first);
            _history.Insert(0, first);
        }
        else _history.Insert(0, new HistoryItem { SourceText = source, Translation = translation });
        while (_history.Count > 30) _history.RemoveAt(_history.Count - 1);
        SettingsStore.SaveHistory(_history);
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryItem item)
        {
            SourceText.Text = item.SourceText;
            TranslationText.Text = item.Translation;
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0) return;
        if (MessageBox.Show(this, "确定清空全部历史记录？", "墨识 OCR", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _history.Clear();
            SettingsStore.SaveHistory(_history);
        }
    }

    private void RefreshSettingsSummary()
    {
        ApiSummary.Text = $"百度 OCR {(_settings.OcrMode == "accurate_basic" ? "高精度版" : "标准版")}  ·  百度翻译";
        var screenshot = DisplayHotkey(_settings.ScreenshotHotkey);
        var recognition = DisplayHotkey(_settings.RecognitionHotkey);
        var translation = DisplayHotkey(_settings.TranslationHotkey);
        ShortcutHint.Text = $"截图  {screenshot}\n识别  {recognition}\n翻译  {translation}";
        CaptureButton.ToolTip = $"框选屏幕区域 ({screenshot})";
        var language = LanguageCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Content?.ToString() == _settings.TargetLanguage);
        if (language is not null) LanguageCombo.SelectedItem = language;
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusDot.Fill = error ? (Brush)FindResource("WarmBrush") : (Brush)FindResource("AccentBrush");
    }

    private static bool IsSupportedImage(string path) => new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    private static string DisplayHotkey(string value) => value == "Disabled" ? "关闭" : value.Replace("+", "+");
    private static string MimeFromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".bmp" => "image/bmp", _ => "image/png" };
    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static (byte[] Bytes, string MimeType) NormalizeForBaidu(BitmapSource source, byte[] original, string mime)
    {
        const int maxSide = 4096;
        const int softByteLimit = 3_700_000;
        var needsConversion = mime == "image/webp" || original.Length > softByteLimit || source.PixelWidth > maxSide || source.PixelHeight > maxSide;
        if (!needsConversion) return (original, mime);

        BitmapSource output = source;
        var scale = Math.Min(1d, (double)maxSide / Math.Max(source.PixelWidth, source.PixelHeight));
        if (scale < 1)
        {
            output = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            output.Freeze();
        }
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
        encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return (stream.ToArray(), "image/jpeg");
    }
}
