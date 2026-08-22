using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MoShiOCR;

public partial class SettingsWindow : Window
{
    private ModifierKeys _pendingModifiers;
    public AppSettings Settings { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Settings = new AppSettings
        {
            OcrMode = current.OcrMode,
            TargetLanguage = current.TargetLanguage,
            ScreenshotHotkey = current.ScreenshotHotkey,
            RecognitionHotkey = current.RecognitionHotkey,
            TranslationHotkey = current.TranslationHotkey,
            StartWithWindows = current.StartWithWindows,
            DarkMode = current.DarkMode
        };
        OcrApiKeyBox.Password = CredentialStore.Read(CredentialStore.OcrApiKey);
        OcrSecretKeyBox.Password = CredentialStore.Read(CredentialStore.OcrSecretKey);
        TranslateAppIdBox.Text = CredentialStore.Read(CredentialStore.TranslateAppId);
        TranslateSecretBox.Password = CredentialStore.Read(CredentialStore.TranslateSecret);
        OcrModeCombo.SelectedItem = OcrModeCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == Settings.OcrMode) ?? OcrModeCombo.Items[0];
        SetHotkeyBox(ScreenshotHotkeyBox, Settings.ScreenshotHotkey);
        SetHotkeyBox(RecognitionHotkeyBox, Settings.RecognitionHotkey);
        SetHotkeyBox(TranslationHotkeyBox, Settings.TranslationHotkey);
        StartWithWindowsCheckBox.IsChecked = Settings.StartWithWindows;
        DarkModeCheckBox.IsChecked = Settings.DarkMode;
    }

    private bool ApplyForm()
    {
        if (OcrModeCombo.SelectedItem is ComboBoxItem mode) Settings.OcrMode = mode.Tag?.ToString() ?? "general_basic";
        Settings.ScreenshotHotkey = ReadHotkeyBox(ScreenshotHotkeyBox);
        Settings.RecognitionHotkey = ReadHotkeyBox(RecognitionHotkeyBox);
        Settings.TranslationHotkey = ReadHotkeyBox(TranslationHotkeyBox);
        Settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        Settings.DarkMode = DarkModeCheckBox.IsChecked == true;

        var active = new[] { Settings.ScreenshotHotkey, Settings.RecognitionHotkey, Settings.TranslationHotkey }
            .Where(value => value != "Disabled")
            .ToArray();
        if (active.Distinct(StringComparer.OrdinalIgnoreCase).Count() != active.Length)
        {
            System.Windows.MessageBox.Show(this, "截图、识别和翻译不能使用相同的快捷键。", "快捷键设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _pendingModifiers = ModifierKeys.None;
        if (sender is TextBox box) box.Text = "请按下新的组合键...";
    }

    private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _pendingModifiers = ModifierKeys.None;
        if (sender is TextBox box) box.Text = DisplayHotkey(ReadHotkeyBox(box));
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            box.Text = DisplayHotkey(ReadHotkeyBox(box));
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.Back or Key.Delete)
        {
            SetHotkeyBox(box, "Disabled");
            Keyboard.ClearFocus();
            return;
        }
        var pressedModifier = ModifierFromKey(key);
        if (pressedModifier != ModifierKeys.None)
        {
            _pendingModifiers |= pressedModifier;
            box.Text = "继续按一个按键...";
            return;
        }

        var modifiers = (Keyboard.Modifiers | _pendingModifiers) & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
        if (modifiers == ModifierKeys.None && !HotkeyHelper.IsFunctionKey(key))
        {
            box.Text = "单键仅支持 F1 到 F24";
            return;
        }
        if (key == Key.F4 && modifiers.HasFlag(ModifierKeys.Alt))
        {
            box.Text = "Alt + F4 为系统关闭窗口快捷键";
            return;
        }
        if (KeyInterop.VirtualKeyFromKey(key) == 0)
        {
            box.Text = "该按键不可用";
            return;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        SetHotkeyBox(box, string.Join("+", parts));
        _pendingModifiers = ModifierKeys.None;
        Keyboard.ClearFocus();
    }

    private static ModifierKeys ModifierFromKey(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
        Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
        Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
        _ => ModifierKeys.None
    };

    private static void SetHotkeyBox(TextBox box, string value)
    {
        box.Tag = string.IsNullOrWhiteSpace(value) ? "Disabled" : value;
        box.Text = DisplayHotkey(box.Tag.ToString() ?? "Disabled");
    }

    private static string ReadHotkeyBox(TextBox box) => box.Tag?.ToString() ?? "Disabled";

    private static string DisplayHotkey(string value)
    {
        if (value == "Disabled") return "已禁用";
        return string.Join(" + ", value.Split('+').Select(part =>
            part.Length == 2 && part[0] == 'D' && char.IsDigit(part[1]) ? part[1].ToString() : part));
    }

    private async void TestOcr_Click(object sender, RoutedEventArgs e)
    {
        TestOcrButton.IsEnabled = false;
        SetTestStatus(OcrTestStatus, "正在连接...", false);
        try
        {
            await new ApiClient().TestOcrAsync(OcrApiKeyBox.Password, OcrSecretKeyBox.Password, CancellationToken.None);
            SetTestStatus(OcrTestStatus, "连接成功", true);
        }
        catch (Exception ex)
        {
            SetTestStatus(OcrTestStatus, "连接失败", false, true);
            System.Windows.MessageBox.Show(this, ex.Message, "OCR 连接测试", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { TestOcrButton.IsEnabled = true; }
    }

    private async void TestTranslate_Click(object sender, RoutedEventArgs e)
    {
        TestTranslateButton.IsEnabled = false;
        SetTestStatus(TranslateTestStatus, "正在连接...", false);
        try
        {
            await new ApiClient().TestTranslateAsync(TranslateAppIdBox.Text, TranslateSecretBox.Password, CancellationToken.None);
            SetTestStatus(TranslateTestStatus, "连接成功", true);
        }
        catch (Exception ex)
        {
            SetTestStatus(TranslateTestStatus, "连接失败", false, true);
            System.Windows.MessageBox.Show(this, ex.Message, "翻译连接测试", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { TestTranslateButton.IsEnabled = true; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyForm()) return;
        try
        {
            StartupManager.Apply(Settings.StartWithWindows);
            ThemeManager.Apply(Settings.DarkMode);
            SettingsStore.SaveSettings(Settings);
            CredentialStore.Write(CredentialStore.OcrApiKey, OcrApiKeyBox.Password);
            CredentialStore.Write(CredentialStore.OcrSecretKey, OcrSecretKeyBox.Password);
            CredentialStore.Write(CredentialStore.TranslateAppId, TranslateAppIdBox.Text);
            CredentialStore.Write(CredentialStore.TranslateSecret, TranslateSecretBox.Password);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "保存设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SetTestStatus(System.Windows.Controls.TextBlock block, string text, bool success, bool error = false)
    {
        block.Text = text;
        block.Foreground = (Brush)FindResource(error ? "WarmBrush" : success ? "AccentBrush" : "MutedBrush");
    }
}
