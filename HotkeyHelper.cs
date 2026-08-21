using System;
using System.Windows.Input;

namespace MoShiOCR;

public static class HotkeyHelper
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    public static bool TryParse(string shortcut, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" => ModControl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                _ => 0u
            };
            if (modifier == 0) return false;
            modifiers |= modifier;
        }

        if (!Enum.TryParse<Key>(parts[^1], true, out var key)) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0 && (modifiers != 0 || IsFunctionKey(key));
    }

    public static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;
}
