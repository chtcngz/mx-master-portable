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
    public static readonly ActionOption[] All =
    [
        new("native", "Varsayılan (fare işlevi)"),
        new("copy", "Kopyala · Ctrl+C"),
        new("paste", "Yapıştır · Ctrl+V"),
        new("cut", "Kes · Ctrl+X"),
        new("undo", "Geri al · Ctrl+Z"),
        new("redo", "Yinele · Ctrl+Y"),
        new("tab", "Pencere değiştir · Alt+Tab"),
        new("desktop", "Masaüstü · Win+D"),
        new("explorer", "Dosya Gezgini · Win+E"),
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

    private const ushort Ctrl = 0x11, Alt = 0x12, Shift = 0x10, Win = 0x5B;
    private const uint KeyUp = 0x0002;
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

    public static void Execute(string action)
    {
        // Static allowlist; no shell invocation and no arbitrary code execution.
        ushort[] seq = action switch
        {
            "copy" => [Ctrl, 0x43],
            "paste" => [Ctrl, 0x56],
            "cut" => [Ctrl, 0x58],
            "undo" => [Ctrl, 0x5A],
            "redo" => [Ctrl, 0x59],
            "tab" => [Alt, 0x09],
            "desktop" => [Win, 0x44],
            "explorer" => [Win, 0x45],
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
            _ => []
        };
        if (seq.Length == 0) return;
        var input = new NativeInput[seq.Length * 2];
        for (int i = 0; i < seq.Length; i++)
        {
            input[i] = new NativeInput { Type = InputKeyboard,
                Data = new InputUnion { Keyboard = new KeyboardInput { Key = seq[i] } } };
            input[seq.Length + i] = new NativeInput { Type = InputKeyboard,
                Data = new InputUnion { Keyboard = new KeyboardInput { Key = seq[seq.Length - 1 - i], Flags = KeyUp } } };
        }
        uint written = SendInput((uint)input.Length, input, Marshal.SizeOf<NativeInput>());
        if (written != input.Length)
            throw new InvalidOperationException("SendInput bazı tuşları gönderemedi (kurum politikası / UIPI olabilir)");
    }
}
