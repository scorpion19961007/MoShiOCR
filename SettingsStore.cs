using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MoShiOCR;

public static class SettingsStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoShiOCR");
    private static readonly string SettingsPath = Path.Combine(Folder, "settings.json");
    private static readonly string HistoryPath = Path.Combine(Folder, "history.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static List<HistoryItem> LoadHistory()
    {
        try
        {
            if (File.Exists(HistoryPath))
                return JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(HistoryPath)) ?? [];
        }
        catch { }
        return [];
    }

    public static void SaveHistory(IEnumerable<HistoryItem> items)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(items.Take(30), JsonOptions));
    }
}
