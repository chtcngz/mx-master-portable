using System.Runtime.InteropServices;

namespace MX4Portable;

internal static class Program
{
    internal const string ShowEventName = @"Local\MXMasterPortable.Show";
    private const string MutexName = @"Local\MXMasterPortable.SingleInstance";

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out bool firstInstance);
        if (!firstInstance)
        {
            // Two instances would fight over the same HID++ diversions; bring
            // the running (possibly tray-hidden) window forward instead.
            AllowSetForegroundWindow(-1);
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        GC.KeepAlive(mutex);
    }
}
