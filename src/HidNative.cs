using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MX4Portable;

internal record HidInterface(string DevicePath, ushort VendorId, ushort ProductId,
    ushort UsagePage, ushort Usage, ushort InputLength, ushort OutputLength, string ProductName)
{
    // The receiver's USB PID is C548; the mouse's own HID++ model ID is B043.
    public string TransportName => SupportedModels.IsKnownDirectBluetoothPid(ProductId) ||
        DevicePath.Contains("BTH", StringComparison.OrdinalIgnoreCase) ? "Bluetooth" : "USB / Bolt";

    public override string ToString() => $"{TransportName} · PID {ProductId:X4} · page {UsagePage:X4} · {ProductName}";
}

internal static class HidNative
{
    private const uint DigcfPresent = 0x02, DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    private const uint ShareRead = 0x00000001, ShareWrite = 0x00000002;
    private const uint OpenExisting = 3, Overlapped = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator,
        IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set,
        ref SpDeviceInterfaceData data, IntPtr detail, uint detailSize,
        out uint requiredSize, IntPtr deviceInfoData);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDevice, ref HiddAttributes attrs);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDevice, out IntPtr data);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetProductString(SafeFileHandle hidDevice, IntPtr buffer, int bufferLength);

    public static SafeFileHandle OpenReadWrite(string path)
    {
        var h = CreateFile(path, GenericRead | GenericWrite, ShareRead | ShareWrite,
            IntPtr.Zero, OpenExisting, Overlapped, IntPtr.Zero);
        if (h.IsInvalid)
        {
            int code = Marshal.GetLastWin32Error();
            h.Dispose();
            throw new Win32Exception(code, "HID erişimi reddedildi veya arayüz yazmaya kapalı");
        }
        return h;
    }

    public static IReadOnlyList<HidInterface> Enumerate()
    {
        HidD_GetHidGuid(out Guid guid);
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var found = new List<HidInterface>();
        try
        {
            for (uint i = 0; i < 512; i++)
            {
                var data = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
                if (required < 8) continue;
                var detail = Marshal.AllocHGlobal((int)required);
                string? path = null;
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W has cbSize 8 on x64, but the
                    // WCHAR DevicePath buffer begins at byte offset 4 in memory.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required,
                        out _, IntPtr.Zero)) path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                }
                finally { Marshal.FreeHGlobal(detail); }
                if (string.IsNullOrEmpty(path)) continue;
                // Opening with access 0 is enough for descriptors; mouse HID collections
                // used by Windows often block direct GENERIC_READ handles.
                using var descriptorHandle = CreateFile(path, 0, ShareRead | ShareWrite,
                    IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (descriptorHandle.IsInvalid) continue;
                var attrs = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
                if (!HidD_GetAttributes(descriptorHandle, ref attrs) || attrs.VendorID != 0x046D) continue;
                if (!HidD_GetPreparsedData(descriptorHandle, out IntPtr parsed)) continue;
                HidpCaps caps;
                try
                {
                    if (HidP_GetCaps(parsed, out caps) < 0) continue;
                }
                finally { HidD_FreePreparsedData(parsed); }
                // HID++ is normally the vendor-defined collection, not the ordinary
                // system pointer / mouse collection on UsagePage 0x01.
                if (caps.UsagePage < 0xFF00 || caps.InputReportByteLength < 20 ||
                    caps.OutputReportByteLength < 20) continue;
                var nameBuffer = Marshal.AllocHGlobal(256);
                string name = "Logitech HID++";
                try
                {
                    if (HidD_GetProductString(descriptorHandle, nameBuffer, 256))
                        name = Marshal.PtrToStringUni(nameBuffer) ?? name;
                }
                finally { Marshal.FreeHGlobal(nameBuffer); }
                found.Add(new HidInterface(path, attrs.VendorID, attrs.ProductID,
                    caps.UsagePage, caps.Usage, caps.InputReportByteLength,
                    caps.OutputReportByteLength, name));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return found;
    }
}
