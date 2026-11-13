namespace ThumbDock.Core;

/// <summary>A global hotkey: RegisterHotKey modifier flags plus a key.</summary>
internal readonly record struct HotkeyDef(uint Modifiers, Keys Key);

/// <summary>Parses and formats hotkey strings such as "Ctrl+Alt+D" (FR-7).</summary>
internal static class HotkeyParser
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;

    /// <summary>Null when invalid. A modifier is required except for F1–F24.</summary>
    public static HotkeyDef? Parse(string? text)
    {
        var parts = Split(text);
        if (parts.Count == 0) return null;
        uint? mods = ParseModifierTokens(parts[..^1]);
        Keys? key = ParseKey(parts[^1]);
        if (mods is null || key is null) return null;
        if (mods == 0 && key is not (>= Keys.F1 and <= Keys.F24)) return null;
        return new HotkeyDef(mods.Value, key.Value);
    }

    /// <summary>Modifier-only string like "Ctrl+Alt" (used for slot hotkeys). Null when invalid or empty.</summary>
    public static uint? ParseModifiers(string? text)
    {
        uint? mods = ParseModifierTokens(Split(text));
        return mods is > 0 ? mods : null;
    }

    public static string Format(HotkeyDef def) =>
        string.Join("+", FormatModifierTokens(def.Modifiers).Append(FormatKey(def.Key)));

    public static string FormatModifiers(uint modifiers) => string.Join("+", FormatModifierTokens(modifiers));

    /// <summary>Hotkey from a WinForms key event (modifiers + key code); null for a lone modifier press.</summary>
    public static HotkeyDef? FromKeyData(Keys keyData)
    {
        Keys code = keyData & Keys.KeyCode;
        if (code is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None) return null;
        uint mods = 0;
        if (keyData.HasFlag(Keys.Control)) mods |= Ctrl;
        if (keyData.HasFlag(Keys.Alt)) mods |= Alt;
        if (keyData.HasFlag(Keys.Shift)) mods |= Shift;
        return new HotkeyDef(mods, code);
    }

    private static List<string> Split(string? text) =>
        (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    private static uint? ParseModifierTokens(IEnumerable<string> tokens)
    {
        uint mods = 0;
        foreach (var t in tokens)
        {
            uint flag = t.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Ctrl,
                "alt" => Alt,
                "shift" => Shift,
                "win" or "windows" => Win,
                _ => 0,
            };
            if (flag == 0) return null;
            mods |= flag;
        }
        return mods;
    }

    private static Keys? ParseKey(string token)
    {
        if (token.Length == 1 && char.IsAsciiLetter(token[0])) return Keys.A + (char.ToUpperInvariant(token[0]) - 'A');
        if (token.Length == 1 && char.IsAsciiDigit(token[0])) return Keys.D0 + (token[0] - '0');
        if (Enum.TryParse<Keys>(token, ignoreCase: true, out var key) && Enum.IsDefined(key) && (key & Keys.Modifiers) == 0)
            return key;
        return null;
    }

    private static IEnumerable<string> FormatModifierTokens(uint m)
    {
        if ((m & Ctrl) != 0) yield return "Ctrl";
        if ((m & Alt) != 0) yield return "Alt";
        if ((m & Shift) != 0) yield return "Shift";
        if ((m & Win) != 0) yield return "Win";
    }

    private static string FormatKey(Keys key) => key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (key - Keys.D0))).ToString() : key.ToString();
}
