using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MX4Portable;

// Explicit opt-in fallback. WH_MOUSE_LL sees all mice in the current desktop;
// it CANNOT reliably distinguish MX Master 4 from another mouse.
internal sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmXButtonDown = 0x020B, WmXButtonUp = 0x020C, WmXButtonDblClk = 0x020D;
    private const int WmMButtonDown = 0x0207, WmMButtonUp = 0x0208, WmMButtonDblClk = 0x0209;
    private const uint LlmhfInjected = 1;
    private readonly HookCallback _callback;
    private readonly Func<string, string> _mapping;
    private readonly Action<string> _action;
    private IntPtr _hook;

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLowLevelHook
    {
        public Point Point;
        public uint MouseData;
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

    public MouseHook(Func<string, string> mapping, Action<string> action)
    {
        _mapping = mapping;
        _action = action;
        _callback = Dispatch;
        _hook = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Mouse hook başlatılamadı");
    }

    private IntPtr Dispatch(int code, IntPtr wparam, IntPtr lparam)
    {
        if (code >= 0)
        {
            int kind = wparam.ToInt32();
            bool down = kind is WmXButtonDown or WmXButtonDblClk or WmMButtonDown or WmMButtonDblClk;
            bool up = kind is WmXButtonUp or WmMButtonUp;
            if (down || up)
            {
                var info = Marshal.PtrToStructure<MouseLowLevelHook>(lparam);
                if ((info.Flags & LlmhfInjected) == 0)
                {
                    string id = kind is WmMButtonDown or WmMButtonUp or WmMButtonDblClk
                        ? "SYS:MIDDLE" : ((info.MouseData >> 16) == 1 ? "SYS:BACK" : "SYS:FORWARD");
                    string mapped = _mapping(id);
                    if (mapped != "native")
                    {
                        if (down) _action(mapped);
                        return new IntPtr(1); // swallow native event; key action is sent separately
                    }
                }
            }
        }
        return CallNextHookEx(_hook, code, wparam, lparam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.KeepAlive(_callback);
    }
}
