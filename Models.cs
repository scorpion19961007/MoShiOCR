using System;

namespace MoShiOCR;

public sealed class AppSettings
{
    public string OcrMode { get; set; } = "general_basic";
    public string TargetLanguage { get; set; } = "简体中文";
    public string ScreenshotHotkey { get; set; } = "Ctrl+Shift+A";
    public string RecognitionHotkey { get; set; } = "Ctrl+F8";
    public string TranslationHotkey { get; set; } = "Ctrl+F9";
}

public sealed class HistoryItem
{
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string SourceText { get; set; } = "";
    public string Translation { get; set; } = "";
    public string DisplayTitle => string.IsNullOrWhiteSpace(SourceText)
        ? "空白记录"
        : SourceText.Replace("\r", " ").Replace("\n", " ").Trim() is var text && text.Length > 24 ? text[..24] + "..." : text;
    public string DisplayTime => CreatedAt.ToString("MM-dd  HH:mm");
}
