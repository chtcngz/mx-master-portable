using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;

namespace MX4Portable;

internal sealed class AppProfile
{
    public Dictionary<string, string> Actions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AllowGlobalMouseHook { get; set; }
    public string Get(string id) => Actions.TryGetValue(id, out var action) ? action : "native";
}

internal sealed class RecoveryJournal
{
    public string UnitId { get; set; } = "";
    public Dictionary<string, bool> OriginalTemporaryDiversions { get; set; } = new();
}

internal sealed class ProfileStore
{
    public string Folder { get; }
    private readonly JsonSerializerOptions _settings = new() { WriteIndented = true };
    private string LegacyProfilePath => Path.Combine(Folder, "profile.json");
    private string LegacyLocalAppDataProfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MX4Portable", "profile.json");
    private string JournalPath => Path.Combine(Folder, "recovery.json");
    private string ProfilesFolder => Path.Combine(Folder, "profiles");
    private string ProfilePath(string deviceIdentity)
    {
        // Never put a raw HID path or arbitrary mouse name into the filename.
        string safeId = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(deviceIdentity)));
        return Path.Combine(ProfilesFolder, safeId + ".json");
    }

    public ProfileStore()
    {
        string preferred = AppContext.BaseDirectory;
        try
        {
            string temp = Path.Combine(preferred, ".write-test");
            File.WriteAllText(temp, "OK");
            File.Delete(temp);
            Folder = preferred;
        }
        catch
        {
            Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MXMasterPortable");
            Directory.CreateDirectory(Folder);
        }
    }

    public AppProfile LoadProfile(string deviceIdentity, SupportedMouseModel model)
    {
        string path = ProfilePath(deviceIdentity);
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppProfile>(File.ReadAllText(path)) ?? new();
            // v0.1 stored one global MX4 profile. Preserve it for MX4 only;
            // never assign MX4 shortcuts to a newly discovered MX Master 3S.
            if (model.Key == SupportedModels.Master4.Key)
            {
                if (File.Exists(LegacyProfilePath))
                    return JsonSerializer.Deserialize<AppProfile>(File.ReadAllText(LegacyProfilePath)) ?? new();
                if (File.Exists(LegacyLocalAppDataProfilePath))
                    return JsonSerializer.Deserialize<AppProfile>(File.ReadAllText(LegacyLocalAppDataProfilePath)) ?? new();
            }
        }
        catch { /* malformed profiles must not break device discovery */ }
        return new();
    }

    public void SaveProfile(string deviceIdentity, AppProfile settings)
    {
        Directory.CreateDirectory(ProfilesFolder);
        string path = ProfilePath(deviceIdentity);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, _settings));
        File.Move(temp, path, true);
    }

    public RecoveryJournal? LoadJournal()
    {
        try { return JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllText(JournalPath)); }
        catch { return null; }
    }

    public void SaveJournal(RecoveryJournal journal)
    {
        string temp = JournalPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(journal, _settings));
        File.Move(temp, JournalPath, true);
    }

    public void ClearJournal() { if (File.Exists(JournalPath)) File.Delete(JournalPath); }
}

internal sealed record ActionOption(string Id, string Title);

internal static class ShortcutActions
{
    public const string CustomPrefix = "keys:";
    public const string RecordCustom = "custom";

    public static readonly ActionOption[] All =
    [
        new("native", "Varsayılan (fare işlevi)"),
        new("copy", "Kopyala · Ctrl+C"),
        new("paste", "Yapıştır · Ctrl+V"),
        new("cut", "Kes · Ctrl+X"),
        new("undo", "Geri al · Ctrl+Z"),
        new("redo", "Yinele · Ctrl+Y"),
        new("tab", "Pencere değiştir · Alt+Tab"),
        new("taskview", "Görev görünümü · Win+Tab"),
        new("desktop", "Masaüstü · Win+D"),
        new("explorer", "Dosya Gezgini · Win+E"),
        new("desktop-left", "Önceki sanal masaüstü · Win+Ctrl+←"),
        new("desktop-right", "Sonraki sanal masaüstü · Win+Ctrl+→"),
        new("reopen", "Sekmeyi yeniden aç · Ctrl+Shift+T"),
        new("close", "Sekmeyi kapat · Ctrl+W"),
        new("back", "Tarayıcı geri"),
        new("forward", "Tarayıcı ileri"),
        new("mute", "Sesi kapat/aç"),
        new("play", "Oynat/duraklat"),
        new("escape", "Esc"),
        new("enter", "Enter"),
        new("f13", "F13"),
        new("f14", "F14")
    ];

    /// <summary>Options for one assignment cell: the fixed list, the row's
    /// current custom shortcut (if any) and the "record" entry.</summary>
    public static List<ActionOption> OptionsFor(string? current)
    {
        var list = new List<ActionOption>(All);
        if (current is not null && TryParseCustom(current, out _))
            list.Add(new(current, "Özel · " + Describe(current)));
        list.Add(new(RecordCustom, "Özel kısayol kaydet…"));
        return list;
    }

    public static string Describe(string action)
    {
        if (TryParseCustom(action, out var keys))
            return string.Join(" + ", keys.Select(KeyName));
        return All.FirstOrDefault(a => a.Id == action)?.Title ?? action;
    }

    public static string KeyName(ushort vk) => vk switch
    {
        Ctrl => "Ctrl", Shift => "Shift", Alt => "Alt", Win => "Win",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓",
        0x0D => "Enter", 0x1B => "Esc", 0x20 => "Space", 0x08 => "Backspace",
        0x2C => "PrintScreen", 0x21 => "PageUp", 0x22 => "PageDown",
        0xAD => "Ses kapat", 0xAE => "Ses -", 0xAF => "Ses +",
        0xB0 => "Sonraki parça", 0xB1 => "Önceki parça", 0xB3 => "Oynat/duraklat",
        _ => ((Keys)vk).ToString()
    };

    public static bool IsModifier(ushort vk) => vk is Ctrl or Shift or Alt or Win;

    /// <summary>Collapses left/right modifier variants to the generic key.</summary>
    public static ushort Normalize(ushort vk) => vk switch
    {
        0xA0 or 0xA1 => Shift,
        0xA2 or 0xA3 => Ctrl,
        0xA4 or 0xA5 => Alt,
        0x5C => Win,
        _ => vk
    };

    /// <summary>Builds "keys:11+10+7B" with modifiers first in a fixed order.</summary>
    public static string BuildCustom(IEnumerable<ushort> keys)
    {
        var all = keys.Select(Normalize).Distinct().ToList();
        var ordered = new[] { Ctrl, Shift, Alt, Win }.Where(all.Contains)
            .Concat(all.Where(k => !IsModifier(k)));
        return CustomPrefix + string.Join("+", ordered.Select(k => k.ToString("X2")));
    }

    public static bool TryParseCustom(string action, out ushort[] keys)
    {
        keys = [];
        if (!action.StartsWith(CustomPrefix, StringComparison.Ordinal)) return false;
        var parts = action[CustomPrefix.Length..].Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 5) return false;
        var parsed = new List<ushort>();
        foreach (var part in parts)
        {
            if (!ushort.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out ushort vk) ||
                vk is 0 or > 0xFE) return false;
            parsed.Add(vk);
        }
        keys = [.. parsed];
        return true;
    }

    internal const ushort Ctrl = 0x11, Alt = 0x12, Shift = 0x10, Win = 0x5B;
    private const uint KeyUp = 0x0002, ExtendedKey = 0x0001;
    private const uint InputKeyboard = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    public static void Execute(string action)
    {
        // Only keystrokes are sent: no shell invocation and no arbitrary code execution.
        ushort[] seq = action switch
        {
            "copy" => [Ctrl, 0x43],
            "paste" => [Ctrl, 0x56],
            "cut" => [Ctrl, 0x58],
            "undo" => [Ctrl, 0x5A],
            "redo" => [Ctrl, 0x59],
            "tab" => [Alt, 0x09],
            "taskview" => [Win, 0x09],
            "desktop" => [Win, 0x44],
            "explorer" => [Win, 0x45],
            "desktop-left" => [Win, Ctrl, 0x25],
            "desktop-right" => [Win, Ctrl, 0x27],
            "reopen" => [Ctrl, Shift, 0x54],
            "close" => [Ctrl, 0x57],
            "back" => [0xA6],
            "forward" => [0xA7],
            "mute" => [0xAD],
            "play" => [0xB3],
            "escape" => [0x1B],
            "enter" => [0x0D],
            "f13" => [0x7C],
            "f14" => [0x7D],
            _ => TryParseCustom(action, out var custom) ? custom : []
        };
        Send(seq);
    }

    // Keys that need KEYEVENTF_EXTENDEDKEY so arrows/navigation are not read
    // as numpad keys and Win/media keys reach the shell correctly.
    private static bool IsExtended(ushort vk) =>
        vk is (>= 0x21 and <= 0x28) or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90
            or 0xA3 or 0xA5 or (>= 0xA6 and <= 0xB7);

    public static void Send(ushort[] seq)
    {
        if (seq.Length == 0) return;
        var input = new NativeInput[seq.Length * 2];
        for (int i = 0; i < seq.Length; i++)
        {
            input[i] = KeyInput(seq[i], false);
            input[seq.Length + i] = KeyInput(seq[seq.Length - 1 - i], true);
        }
        uint written = SendInput((uint)input.Length, input, Marshal.SizeOf<NativeInput>());
        if (written != input.Length)
            throw new InvalidOperationException("SendInput bazı tuşları gönderemedi (kurum politikası / UIPI olabilir)");
    }

    private static NativeInput KeyInput(ushort vk, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                Key = vk,
                Scan = (ushort)MapVirtualKey(vk, 0),
                Flags = (up ? KeyUp : 0) | (IsExtended(vk) ? ExtendedKey : 0)
            }
        }
    };
}

/// <summary>Thumb wheel assignments (HID++ 0x2150).</summary>
internal static class ThumbActions
{
    public const string Key = "THUMB";
    public static readonly ActionOption[] All =
    [
        new("native", "Varsayılan · yatay kaydırma"),
        new("hscroll-invert", "Yatay kaydırma · ters yön"),
        new("desktops", "Sanal masaüstü değiştir · Win+Ctrl+←/→"),
        new("tabs", "Sekme değiştir · Ctrl+Tab / Ctrl+Shift+Tab"),
        new("volume", "Ses seviyesi"),
        new("zoom", "Yakınlaştır / uzaklaştır · Ctrl + / Ctrl -")
    ];

    /// <summary>Actions that need the wheel diverted to this application.</summary>
    public static bool NeedsDivert(string action) => action is "desktops" or "tabs" or "volume" or "zoom";

    /// <summary>Desktop switching fires once per gesture so one flick moves one desktop.</summary>
    public static bool OncePerGesture(string action) => action == "desktops";

    public static ushort[] Keys(string action, bool forward) => action switch
    {
        "desktops" => forward
            ? [ShortcutActions.Win, ShortcutActions.Ctrl, 0x27]
            : [ShortcutActions.Win, ShortcutActions.Ctrl, 0x25],
        "tabs" => forward ? [ShortcutActions.Ctrl, 0x09] : [ShortcutActions.Ctrl, ShortcutActions.Shift, 0x09],
        "volume" => forward ? [0xAF] : [0xAE],
        "zoom" => forward ? [ShortcutActions.Ctrl, 0xBB] : [ShortcutActions.Ctrl, 0xBD],
        _ => []
    };
}
