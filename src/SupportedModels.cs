namespace MX4Portable;

// Physical device PIDs from HID++ 0x0003, NOT the Bolt receiver's USB PID (0xC548).
// A single physical mouse can report different PIDs on Bluetooth and Bolt.
internal sealed record SupportedMouseModel(string Key, string DisplayName, ushort[] DevicePids)
{
    public override string ToString() => DisplayName;
}

internal static class SupportedModels
{
    public static readonly SupportedMouseModel Master3S = new(
        "mx-master-3s", "MX Master 3S", [0xB034, 0xB043]);
    public static readonly SupportedMouseModel Master3SBusiness = new(
        "mx-master-3s-business", "MX Master 3S for Business", [0xB035, 0xB044]);
    public static readonly SupportedMouseModel Master4 = new(
        "mx-master-4", "MX Master 4", [0xB042]);
    public static readonly SupportedMouseModel Master4Business = new(
        "mx-master-4-business", "MX Master 4 for Business", [0xB048]);

    public static readonly SupportedMouseModel[] All =
        [Master3S, Master3SBusiness, Master4, Master4Business];

    public static bool IsKnownDirectBluetoothPid(ushort pid) =>
        pid is 0xB034 or 0xB035 or 0xB042 or 0xB048;

    public static bool TryMatchDeviceInfo(byte[] packet, ushort interfacePid,
        out SupportedMouseModel? model, out string unit, out string modelIds)
    {
        model = null;
        unit = "";
        modelIds = "";
        // HID++ long report: header bytes 0..3, x0003 fn0 response payload:
        // 0 entity_count, 1..4 unit_id, 5 reserved, 6 transports, 7..12 model PIDs.
        if (packet.Length < 17 || packet[0] != 0x11) return false;
        unit = Convert.ToHexString(packet.AsSpan(5, 4));
        var pids = new ushort[3];
        for (int i = 0; i < 3; i++)
        {
            int offset = 11 + i * 2;
            pids[i] = (ushort)((packet[offset] << 8) | packet[offset + 1]);
        }
        modelIds = string.Join(",", pids.Select(pid => $"{pid:X4}"));
        model = All.FirstOrDefault(item => pids.Any(pid => pid != 0 && item.DevicePids.Contains(pid)));
        // Some older firmware variants may provide no usable model PID. Only
        // fall back to a known, *direct* Bluetooth HID PID (never receiver C548).
        if (model is null && pids.All(pid => pid == 0) && IsKnownDirectBluetoothPid(interfacePid))
            model = All.FirstOrDefault(item => item.DevicePids.Contains(interfacePid));
        return model is not null;
    }
}
