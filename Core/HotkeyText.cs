namespace IdleDash.Core;

/// <summary>
/// Sneltoetsen als tekst ("ctrl+alt+d") en terug naar wat Windows nodig heeft (toetscodes).
/// Veiligheidsregel: minstens twee van Ctrl, Alt en Shift, of de Windows-toets, zodat je niet
/// per ongeluk een gewone sneltoets als Ctrl+C overal blokkeert.
/// </summary>
public static class HotkeyText
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;

    private static readonly Dictionary<string, uint> Named = new()
    {
        ["space"] = 0x20, ["pageup"] = 0x21, ["pagedown"] = 0x22, ["end"] = 0x23, ["home"] = 0x24,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28, ["insert"] = 0x2D, ["delete"] = 0x2E, ["pause"] = 0x13,
    };

    public static (uint Modifiers, uint Key)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "none") return null;
        uint modifiers = 0;
        uint? key = null;
        foreach (string raw in text.ToLowerInvariant().Split('+'))
        {
            switch (raw.Trim())
            {
                case "ctrl": modifiers |= Ctrl; break;
                case "alt": modifiers |= Alt; break;
                case "shift": modifiers |= Shift; break;
                case "win": modifiers |= Win; break;
                case var name:
                    if (key != null || KeyCode(name) is not uint code) return null;
                    key = code;
                    break;
            }
        }
        return key is uint k && IsAllowed(modifiers, k) ? (modifiers, k) : null;
    }

    public static bool IsAllowed(uint modifiers, uint key)
    {
        if (KeyName(key) == null) return false;
        int count = new[] { Ctrl, Alt, Shift, Win }.Count(m => (modifiers & m) != 0);
        return count >= 2 || modifiers == Win;
    }

    public static string Format(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & Ctrl) != 0) parts.Add("ctrl");
        if ((modifiers & Alt) != 0) parts.Add("alt");
        if ((modifiers & Shift) != 0) parts.Add("shift");
        if ((modifiers & Win) != 0) parts.Add("win");
        parts.Add(KeyName(key) ?? "?");
        return string.Join("+", parts);
    }

    /// <summary>Zoals je hem op het toetsenbord ziet: "Ctrl + Alt + D". Leeg als er geen (geldige) sneltoets is.</summary>
    public static string Display(string? text)
    {
        if (Parse(text) is not { } hotkey) return "";
        var parts = new List<string>();
        if ((hotkey.Modifiers & Ctrl) != 0) parts.Add("Ctrl");
        if ((hotkey.Modifiers & Alt) != 0) parts.Add("Alt");
        if ((hotkey.Modifiers & Shift) != 0) parts.Add("Shift");
        if ((hotkey.Modifiers & Win) != 0) parts.Add("Win");
        string name = KeyName(hotkey.Key)!;
        parts.Add(name.StartsWith("num") ? "Num " + name[3..] : name.Length == 1 || name.StartsWith('f') ? name.ToUpperInvariant() : char.ToUpperInvariant(name[0]) + name[1..]);
        return string.Join(" + ", parts);
    }

    public static uint? KeyCode(string name)
    {
        if (name.Length == 1 && name[0] is >= 'a' and <= 'z') return char.ToUpperInvariant(name[0]);
        if (name.Length == 1 && name[0] is >= '0' and <= '9') return name[0];
        if (name.Length >= 2 && name[0] == 'f' && int.TryParse(name[1..], out int f) && f is >= 1 and <= 24) return (uint)(0x70 + f - 1);
        if (name.StartsWith("num") && int.TryParse(name[3..], out int n) && n is >= 0 and <= 9) return (uint)(0x60 + n);
        return Named.TryGetValue(name, out uint code) ? code : null;
    }

    public static string? KeyName(uint code) => code switch
    {
        >= 0x41 and <= 0x5A => ((char)(code + 32)).ToString(),
        >= 0x30 and <= 0x39 => ((char)code).ToString(),
        >= 0x70 and <= 0x87 => "f" + (code - 0x70 + 1),
        >= 0x60 and <= 0x69 => "num" + (code - 0x60),
        _ => Named.FirstOrDefault(p => p.Value == code).Key,
    };
}
