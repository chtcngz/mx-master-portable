using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MX4Portable;

// Records a key combination for a mouse button. While recording, a low-level
// keyboard hook swallows the keys so combinations such as Win+Tab or Alt+Tab
// are captured instead of being executed. Modifier checkboxes and a key list
// are always available as a manual fallback (e.g. for Win+L, which Windows
// never lets an application intercept).
internal sealed class ShortcutCaptureForm : Form
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardLowLevelHook
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr HookCallback(int code, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookType, HookCallback callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wparam, IntPtr lparam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private readonly HookCallback _callback;
    private IntPtr _hook;
    private readonly HashSet<ushort> _held = [];
    private readonly HashSet<ushort> _gesture = [];

    private readonly Label _display = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold), BackColor = Color.White
    };
    private readonly Label _hint = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(104, 115, 128) };
    private readonly CheckBox _ctrl = new() { Text = "Ctrl", AutoSize = true };
    private readonly CheckBox _shift = new() { Text = "Shift", AutoSize = true };
    private readonly CheckBox _alt = new() { Text = "Alt", AutoSize = true };
    private readonly CheckBox _win = new() { Text = "Win", AutoSize = true };
    private readonly ComboBox _key = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly Button _record = new() { Text = "Tekrar kaydet", AutoSize = true };
    private readonly Button _ok = new() { Text = "Kaydet", AutoSize = true, DialogResult = DialogResult.OK };
    private readonly Button _cancel = new() { Text = "İptal", AutoSize = true, DialogResult = DialogResult.Cancel };
    private bool _syncing;
    private bool _captured;

    public string? Result { get; private set; }

    private sealed record KeyItem(ushort Vk, string Name)
    {
        public override string ToString() => Name;
    }

    public ShortcutCaptureForm(string current)
    {
        _callback = Dispatch;
        Text = "Özel kısayol kaydet";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 260);
        Font = new Font("Segoe UI", 9.5f);
        AcceptButton = _ok;
        CancelButton = _cancel;

        _key.Items.Add(new KeyItem(0, "(yalnız değiştirici tuşlar)"));
        foreach (ushort vk in SelectableKeys()) _key.Items.Add(new KeyItem(vk, ShortcutActions.KeyName(vk)));

        var modifiers = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        modifiers.Controls.AddRange([_ctrl, _shift, _alt, _win, new Label { Text = "+", AutoSize = true,
            Margin = new Padding(6, 6, 6, 0) }, _key]);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([_cancel, _ok, _record]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_display, 0, 0);
        layout.Controls.Add(_hint, 0, 1);
        layout.Controls.Add(modifiers, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);

        foreach (var box in new[] { _ctrl, _shift, _alt, _win }) box.CheckedChanged += (_, _) => FromControls();
        _key.SelectedIndexChanged += (_, _) => FromControls();
        _record.Click += (_, _) => StartRecording();
        _ok.Click += (_, _) => { if (Result is null) DialogResult = DialogResult.None; };

        if (ShortcutActions.TryParseCustom(current, out var keys)) ToControls(keys);
        else ToControls([]);
        Shown += (_, _) => StartRecording();
        FormClosed += (_, _) => StopRecording();
    }

    private static IEnumerable<ushort> SelectableKeys()
    {
        for (ushort vk = 0x41; vk <= 0x5A; vk++) yield return vk;           // A-Z
        for (ushort vk = 0x30; vk <= 0x39; vk++) yield return vk;           // 0-9
        for (ushort vk = 0x70; vk <= 0x87; vk++) yield return vk;           // F1-F24
        foreach (ushort vk in new ushort[]
        {
            0x09, 0x0D, 0x1B, 0x20, 0x08, 0x2E, 0x2D, 0x24, 0x23, 0x21, 0x22,
            0x25, 0x26, 0x27, 0x28, 0x2C, 0xBB, 0xBD, 0xBC, 0xBE,
            0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB3, 0xA6, 0xA7
        }) yield return vk;
    }

    private void StartRecording()
    {
        if (_hook != IntPtr.Zero) return;
        _captured = false;
        _held.Clear();
        _gesture.Clear();
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            _hint.Text = "Klavye kaydı başlatılamadı (" + new Win32Exception(Marshal.GetLastWin32Error()).Message +
                         "). Kısayolu aşağıdan elle seçin.";
            return;
        }
        _display.Text = "Kısayola basın…";
        _display.ForeColor = Color.FromArgb(22, 128, 112);
        _hint.Text = "Kayıt sırasında tuşlar çalışmaz (Win+Tab gibi). Esc tek başına kaydı durdurur. " +
                     "Fn tuşu Windows'a ulaşmaz; Fn kombinasyonu klavyenin ürettiği tuş olarak kaydedilir.";
        _record.Enabled = false;
    }

    private void StopRecording()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _record.Enabled = true;
        GC.KeepAlive(_callback);
    }

    private IntPtr Dispatch(int code, IntPtr wparam, IntPtr lparam)
    {
        if (code < 0 || _hook == IntPtr.Zero) return CallNextHookEx(_hook, code, wparam, lparam);
        var info = Marshal.PtrToStructure<KeyboardLowLevelHook>(lparam);
        if ((info.Flags & LlkhfInjected) != 0) return CallNextHookEx(_hook, code, wparam, lparam);
        int kind = wparam.ToInt32();
        ushort vk = ShortcutActions.Normalize((ushort)info.VkCode);
        bool down = kind is WmKeyDown or WmSysKeyDown;
        bool up = kind is WmKeyUp or WmSysKeyUp;
        if (_captured)
        {
            // Keep swallowing until every key is released so a key-up (e.g. Win)
            // cannot reach the shell and open the Start menu.
            if (down) _held.Add(vk);
            else if (up) _held.Remove(vk);
            if (_held.Count == 0) StopRecording();
            return new IntPtr(1);
        }
        if (down)
        {
            _held.Add(vk);
            if (ShortcutActions.IsModifier(vk)) _gesture.Add(vk);
            else if (vk == 0x1B && _gesture.Count == 0)
            {
                StopRecording();
                _display.Text = Result is null ? "Kayıt durduruldu" : ShortcutActions.Describe(Result);
            }
            else
            {
                _gesture.Add(vk);
                Finish();
            }
        }
        else if (up)
        {
            _held.Remove(vk);
            // Modifier-only shortcut (e.g. Win alone): record on full release.
            if (_held.Count == 0 && _gesture.Count > 0) Finish();
        }
        // Swallow every key while recording so nothing reaches the system.
        return new IntPtr(1);
    }

    private void Finish()
    {
        var keys = _gesture.ToList();
        _gesture.Clear();
        if (keys.Count == 0) return;
        _captured = true;
        if (_held.Count == 0) StopRecording();
        ToControls(keys);
        _display.Text = ShortcutActions.Describe(Result ?? "");
        _display.ForeColor = Color.FromArgb(36, 42, 51);
    }

    private void ToControls(IReadOnlyCollection<ushort> keys)
    {
        _syncing = true;
        _ctrl.Checked = keys.Contains(ShortcutActions.Ctrl);
        _shift.Checked = keys.Contains(ShortcutActions.Shift);
        _alt.Checked = keys.Contains(ShortcutActions.Alt);
        _win.Checked = keys.Contains(ShortcutActions.Win);
        ushort main = keys.FirstOrDefault(k => !ShortcutActions.IsModifier(k));
        var item = _key.Items.Cast<KeyItem>().FirstOrDefault(i => i.Vk == main);
        if (item is null)
        {
            item = new KeyItem(main, ShortcutActions.KeyName(main));
            _key.Items.Add(item);
        }
        _key.SelectedItem = item;
        _syncing = false;
        FromControls();
    }

    private void FromControls()
    {
        if (_syncing) return;
        var keys = new List<ushort>();
        if (_ctrl.Checked) keys.Add(ShortcutActions.Ctrl);
        if (_shift.Checked) keys.Add(ShortcutActions.Shift);
        if (_alt.Checked) keys.Add(ShortcutActions.Alt);
        if (_win.Checked) keys.Add(ShortcutActions.Win);
        if (_key.SelectedItem is KeyItem { Vk: not 0 } item) keys.Add(item.Vk);
        Result = keys.Count == 0 ? null : ShortcutActions.BuildCustom(keys);
        _ok.Enabled = Result is not null;
        if (_hook == IntPtr.Zero)
        {
            _display.Text = Result is null ? "Kısayol seçilmedi" : ShortcutActions.Describe(Result);
            _display.ForeColor = Color.FromArgb(36, 42, 51);
        }
    }
}
