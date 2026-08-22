using System;
using System.Windows;
using System.Windows.Media;

namespace MoShiOCR;

public static class ThemeManager
{
    public static void Apply(bool dark)
    {
        if (Application.Current is null) return;

        SetColor("Ink", dark ? "#E7EEE9" : "#17211B");
        SetColor("Muted", dark ? "#9AAEA3" : "#68736B");
        SetColor("Canvas", dark ? "#151A17" : "#F4F6F3");
        SetColor("Panel", dark ? "#202723" : "#FFFFFF");
        SetColor("Line", dark ? "#35423B" : "#DDE3DE");
        SetColor("Accent", dark ? "#35B58A" : "#13795B");
        SetColor("AccentSoft", dark ? "#183D31" : "#E6F3EE");
        SetColor("Warm", dark ? "#F09A68" : "#D66B2C");
        SetColor("Sidebar", dark ? "#1B211E" : "#FAFBF9");
        SetColor("Input", dark ? "#18201C" : "#FFFFFF");
        SetColor("Hover", dark ? "#29362F" : "#EEF2EE");
        SetColor("WarningSoft", dark ? "#442C22" : "#FFF1E9");
        SetColor("Section", dark ? "#27312B" : "#F1F3F0");
        SetColor("ImageBadge", dark ? "#E6222925" : "#DDFFFFFF");
    }

    private static void SetColor(string key, string value)
    {
        var color = (Color)ColorConverter.ConvertFromString(value)!;
        Application.Current.Resources[key] = color;
        Application.Current.Resources[$"{key}Brush"] = new SolidColorBrush(color);
    }
}
