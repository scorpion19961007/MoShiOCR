using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Data;
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
using System.Windows.Threading;

namespace MoShiOCR;

public partial class MainWindow : Window
{
    private const int ScreenshotHotkeyId = 0x4D4F;
    private const int RecognitionHotkeyId = 0x4D50;
    private const int TranslationHotkeyId = 0x4D51;
    private const int TableRecognitionHotkeyId = 0x4D52;
    private const uint ModNoRepeat = 0x4000;

    private readonly ApiClient _api = new();
    private readonly ObservableCollection<HistoryItem> _history;
    private AppSettings _settings;
    private byte[]? _imageBytes;
    private string _mimeType = "image/png";
    private CancellationTokenSource? _operation;
    private HwndSource? _source;
    private bool _hotkeysReady = true;
    private bool _recognitionCompleted;
    private bool _tableResultVisible;

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
            case TableRecognitionHotkeyId:
                CaptureTable_Click();
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
        var tableRecognitionOk = RegisterConfiguredHotkey(TableRecognitionHotkeyId, _settings.TableRecognitionHotkey);
        return screenshotOk && recognitionOk && translationOk && tableRecognitionOk;
    }

    private bool RegisterConfiguredHotkey(int id, string shortcut)
    {
        if (string.Equals(shortcut, "Disabled", StringComparison.OrdinalIgnoreCase)) return true;
        if (_source is null || !HotkeyHelper.TryParse(shortcut, out var modifiers, out var key)) return false;
        return RegisterHotKey(_source.Handle, id, modifiers | ModNoRepeat, key);
    }

    private void UnregisterAllHotkeys()
    {
        if (_source is null) return;
        UnregisterHotKey(_source.Handle, ScreenshotHotkeyId);
        UnregisterHotKey(_source.Handle, RecognitionHotkeyId);
        UnregisterHotKey(_source.Handle, TranslationHotkeyId);
        UnregisterHotKey(_source.Handle, TableRecognitionHotkeyId);
    }

    private void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _recognitionCompleted)
        {
            Hide();
            e.Handled = true;
            return;
        }
        if (e.Key == System.Windows.Input.Key.V && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            // Let the source editor handle Ctrl+V as a normal text paste. Elsewhere it pastes an image.
            if (SourceText.IsKeyboardFocusWithin) return;
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

    private void Capture_Click(object sender, RoutedEventArgs e) => CaptureAndRecognize(false);

    private void CaptureTable_Click() => CaptureAndRecognize(true);

    private async void CaptureAndRecognize(bool table)
    {
        var wasVisible = IsVisible;
        var captured = false;
        if (wasVisible) Hide();
        // Let WPF and DWM finish removing the main window before copying the desktop.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(60);
        try
        {
            var capture = new ScreenCaptureWindow();
            if (capture.ShowDialog() == true && capture.CapturedBytes is not null)
            {
                captured = true;
                LoadImage(capture.CapturedBytes, "image/png", "屏幕截图");
                if (!IsVisible) Show();
                Activate();
                _ = table ? RunTableOcrAsync() : RunOcrAsync();
            }
        }
        finally
        {
            if (wasVisible || captured)
            {
                if (!IsVisible) Show();
                Activate();
            }
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = dialog.Settings;
            ThemeManager.Apply(_settings.DarkMode);
            var hotkeysOk = _hotkeysReady = RegisterAllHotkeys();
            RefreshSettingsSummary();
            SetStatus(hotkeysOk ? "设置已保存，快捷键已生效" : "设置已保存，但部分快捷键已被其他程序占用", !hotkeysOk);
        }
    }

    private async void Ocr_Click(object sender, RoutedEventArgs e) => await RunOcrAsync();

    private async void TableOcr_Click(object sender, RoutedEventArgs e) => await RunTableOcrAsync();

    private async Task RunOcrAsync()
    {
        if (_imageBytes is null) { SetStatus("请先打开、粘贴或截取图片", true); return; }
        _recognitionCompleted = false;
        await RunBusyAsync("正在识别图片...", async token =>
        {
            var result = await _api.RecognizeAsync(
                _imageBytes,
                _settings,
                CredentialStore.Read(CredentialStore.OcrApiKey),
                CredentialStore.Read(CredentialStore.OcrSecretKey),
                CredentialStore.Read(CredentialStore.TencentSecretId),
                CredentialStore.Read(CredentialStore.TencentSecretKey),
                token);
            ShowTextResult(result);
            TranslationText.Clear();
            AddHistory(result, "");
            _recognitionCompleted = true;
            var copied = TrySetClipboardText(result);
            SetStatus(copied ? "识别完成，已复制到剪贴板" : "识别完成，但自动复制失败", !copied);
        });
    }

    private async Task RunTableOcrAsync()
    {
        if (_imageBytes is null) { SetStatus("请先打开、粘贴或截取图片", true); return; }
        _recognitionCompleted = false;
        await RunBusyAsync("正在识别表格...", async token =>
        {
            var result = await _api.RecognizeTableAsync(
                _imageBytes,
                _settings,
                CredentialStore.Read(CredentialStore.OcrApiKey),
                CredentialStore.Read(CredentialStore.OcrSecretKey),
                CredentialStore.Read(CredentialStore.TencentSecretId),
                CredentialStore.Read(CredentialStore.TencentSecretKey),
                token);
            ShowTableResult(result);
            TranslationText.Clear();
            AddHistory(result, "");
            _recognitionCompleted = true;
            var copied = TrySetClipboardTable(result);
            SetStatus(copied ? "表格识别完成，结果已复制到剪贴板" : "表格识别完成，但自动复制失败", !copied);
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
                CredentialStore.Read(CredentialStore.TencentSecretId),
                CredentialStore.Read(CredentialStore.TencentSecretKey),
                token);
            AddHistory(SourceText.Text, TranslationText.Text);
            SetStatus("翻译完成");
        });
    }

    private async Task RunBusyAsync(string message, Func<CancellationToken, Task> action)
    {
        _operation?.Cancel();
        _operation = new CancellationTokenSource();
        OcrButton.IsEnabled = TableOcrButton.IsEnabled = TranslateButton.IsEnabled = false;
        SetStatus(message);
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { SetStatus("操作已取消"); }
        catch (Exception ex) { SetStatus(ex.Message, true); MessageBox.Show(this, ex.Message, "请求失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { OcrButton.IsEnabled = TableOcrButton.IsEnabled = TranslateButton.IsEnabled = true; }
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
            _recognitionCompleted = false;
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
        _recognitionCompleted = false;
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

    private void CopySource_Click(object sender, RoutedEventArgs e)
    {
        if (_tableResultVisible)
        {
            var copied = TrySetClipboardTable(SourceText.Text);
            SetStatus(copied ? "表格已复制，可直接粘贴到 Excel 或 WPS" : "剪贴板暂时不可用，请重试", !copied);
            return;
        }
        CopyText(SourceText.Text, "识别文本已复制");
    }
    private void CopyTranslation_Click(object sender, RoutedEventArgs e) => CopyText(TranslationText.Text, "译文已复制");

    private void CopyText(string text, string status)
    {
        if (string.IsNullOrEmpty(text)) { SetStatus("没有可复制的内容", true); return; }
        SetStatus(TrySetClipboardText(text) ? status : "剪贴板暂时不可用，请重试", true);
    }

    private static bool TrySetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (ExternalException)
            {
                if (attempt == 3) return false;
                Thread.Sleep(40);
            }
        }
        return false;
    }

    private void ShowTextResult(string text)
    {
        _tableResultVisible = false;
        TableResultGrid.Visibility = Visibility.Collapsed;
        SourceText.Visibility = Visibility.Visible;
        SourceText.Text = text;
    }

    private void ShowTableResult(string text)
    {
        _tableResultVisible = true;
        SourceText.Text = text;
        SourceText.Visibility = Visibility.Collapsed;
        TableResultGrid.ItemsSource = ParseTable(text).DefaultView;
        TableResultGrid.Visibility = Visibility.Visible;
    }

    private static DataTable ParseTable(string text)
    {
        var rows = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .ToList();
        var table = new DataTable();
        var columnCount = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(row => row.Length));
        for (var column = 0; column < columnCount; column++) table.Columns.Add($"C{column + 1}");
        foreach (var row in rows)
        {
            var dataRow = table.NewRow();
            for (var column = 0; column < row.Length; column++) dataRow[column] = row[column];
            table.Rows.Add(dataRow);
        }
        return table;
    }

    private static bool TrySetClipboardTable(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData(DataFormats.Text, text);
        data.SetData(DataFormats.Html, ToHtmlTable(text));
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try { Clipboard.SetDataObject(data, true); return true; }
            catch (ExternalException)
            {
                if (attempt == 3) return false;
                Thread.Sleep(40);
            }
        }
        return false;
    }

    private static string ToHtmlTable(string text)
    {
        var rows = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var html = new System.Text.StringBuilder("<table>");
        foreach (var row in rows)
        {
            html.Append("<tr>");
            foreach (var cell in row.Split('\t')) html.Append("<td>").Append(System.Net.WebUtility.HtmlEncode(cell)).Append("</td>");
            html.Append("</tr>");
        }
        return html.Append("</table>").ToString();
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
            ShowTextResult(item.SourceText);
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
        var provider = _settings.OcrProvider == "tencent" ? "腾讯云" : "百度";
        var ocrMode = _settings.OcrProvider == "tencent"
            ? (_settings.OcrMode == "general_accurate" ? "通用高精度" : "通用识别")
            : (_settings.OcrMode == "accurate_basic" ? "高精度版" : "标准版");
        var tableMode = _settings.OcrProvider == "tencent"
            ? (_settings.TableOcrMode == "table_v2" ? "表格 V2" : "表格 V1")
            : (_settings.TableOcrMode == "table_async" ? "表格提交请求" : "表格 V2");
        var translationProvider = _settings.OcrProvider == "tencent" ? "腾讯云翻译" : "百度翻译";
        ApiSummary.Text = $"{provider} OCR {ocrMode} · {tableMode} · {translationProvider}";
        var screenshot = DisplayHotkey(_settings.ScreenshotHotkey);
        var recognition = DisplayHotkey(_settings.RecognitionHotkey);
        var translation = DisplayHotkey(_settings.TranslationHotkey);
        var tableRecognition = DisplayHotkey(_settings.TableRecognitionHotkey);
        ShortcutHint.Text = $"截图  {screenshot}\n识别  {recognition}\n表格  {tableRecognition}\n翻译  {translation}";
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
