using MX4Portable;

// Hardware-independent checks. These verify parsing but NOT Windows HID access.
static byte[] Packet(ushort pid1, ushort pid2 = 0, ushort pid3 = 0,
    byte[]? unit = null)
{
    var b = new byte[20];
    b[0] = 0x11; // HID++ long report
    b[4] = 1;    // x0003 entity_count
    (unit ?? new byte[] { 0x11, 0x22, 0x33, 0x44 }).CopyTo(b, 5);
    ushort[] pids = [pid1, pid2, pid3];
    for (int i = 0; i < 3; i++)
    {
        b[11 + i * 2] = (byte)(pids[i] >> 8);
        b[12 + i * 2] = (byte)pids[i];
    }
    return b;
}
static void Expect(string label, byte[] packet, ushort interfacePid,
    string? expectedModel, string? expectedUnit = "11223344")
{
    bool found = SupportedModels.TryMatchDeviceInfo(packet, interfacePid,
        out var model, out string unit, out _);
    if (found != (expectedModel is not null) || model?.Key != expectedModel ||
        (expectedUnit is not null && unit != expectedUnit))
    {
        throw new Exception($"FAIL {label}: found={found}, model={model?.Key}, unit={unit}");
    }
    Console.WriteLine($"PASS {label}");
}

Expect("MX Master 3S Bluetooth", Packet(0xB034), 0xB034, "mx-master-3s");
Expect("MX Master 3S Bolt", Packet(0xB043), 0xC548, "mx-master-3s");
Expect("MX Master 3S multi-PID", Packet(0xB034, 0xB043), 0xC548, "mx-master-3s");
Expect("MX Master 3S Business Bluetooth", Packet(0xB035), 0xB035, "mx-master-3s-business");
Expect("MX Master 3S Business Bolt", Packet(0xB044), 0xC548, "mx-master-3s-business");
Expect("MX Master 4", Packet(0xB042), 0xC548, "mx-master-4");
Expect("MX Master 4 Business", Packet(0xB048), 0xB048, "mx-master-4-business");
Expect("Unknown mouse rejected", Packet(0xB777), 0xC548, null);
Expect("Receiver PID alone rejected", Packet(0), 0xC548, null);
Expect("Fallback direct bluetooth", Packet(0), 0xB034, "mx-master-3s");
Expect("Too short rejected", new byte[5], 0xB034, null, null);
Console.WriteLine("All model identification checks passed.");
