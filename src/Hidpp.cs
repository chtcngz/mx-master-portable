using System.ComponentModel;
using System.Text;
using System.Security.Cryptography;

namespace MX4Portable;

internal sealed record MouseControl(ushort Cid, ushort TaskId, byte Flags, bool RawXy)
{
    public bool Divertable => (Flags & 0x20) != 0;
    public bool Reprogrammable => (Flags & 0x10) != 0;
    public bool Virtual => (Flags & 0x80) != 0;
    public bool Editable => !Virtual && Divertable && Reprogrammable;
    public string Key => $"CID:{Cid:X4}";
    public string Description => Cid switch
    {
        0x0052 => "Orta tuş",
        0x0053 => "Geri",
        0x0056 => "İleri",
        0x00C3 => "Gesture tuşu",
        0x00C4 => "SmartShift / Tekerlek modu",
        0x01A0 => "Haptic Sense panel (MX Master 4)",
        _ => $"Kontrol 0x{Cid:X4}"
    };
}

internal sealed record ControlReporting(ushort Cid, byte Flags)
{
    public bool TemporarilyDiverted => (Flags & 0x01) != 0;
}

internal sealed class HidppConnection : IAsyncDisposable
{
    private readonly FileStream _stream;
    private readonly HidInterface _device;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly object _gate = new();
    private readonly Task _readLoop;
    private TaskCompletionSource<byte[]>? _pending;
    private byte _target, _feature, _functionAndSoftware;
    private int _counter;
    private bool _closed;
    private int _sessionCount;

    public event Action<byte[]>? Notification;
    public HidInterface Interface => _device;

    public HidppConnection(HidInterface device)
    {
        _device = device;
        var handle = HidNative.OpenReadWrite(device.DevicePath);
        _stream = new FileStream(handle, FileAccess.ReadWrite, Math.Max(device.InputLength, device.OutputLength), true);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var frame = new byte[_device.InputLength];
                int read = await _stream.ReadAsync(frame, _cts.Token).ConfigureAwait(false);
                if (read < 7) continue;
                // Only HID++ short and long reports.
                if (frame[0] != 0x11 && frame[0] != 0x10) continue;
                bool handled = false;
                lock (_gate)
                {
                    if (_pending is not null && frame[1] == _target)
                    {
                        bool match = frame[2] == _feature && frame[3] == _functionAndSoftware;
                        bool error = (frame[2] == 0xFF || frame[2] == 0x8F) &&
                                     frame[3] == _feature && frame[4] == _functionAndSoftware;
                        if (match || error)
                        {
                            _pending.TrySetResult(frame);
                            handled = true;
                        }
                    }
                }
                if (!handled && (frame[3] & 0x0F) == 0)
                {
                    try { Notification?.Invoke(frame); } catch { /* user callback isolation */ }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            lock (_gate) _pending?.TrySetException(new IOException("HID++ okuma hatası", ex));
        }
    }

    public async Task<byte[]> CallAsync(byte deviceIndex, byte featureIndex,
        byte function, params byte[] parameters)
    {
        if (function > 15 || parameters.Length > 16) throw new ArgumentOutOfRangeException(nameof(function));
        await _requests.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_closed) throw new ObjectDisposedException(nameof(HidppConnection));
            byte sw = (byte)((Interlocked.Increment(ref _counter) % 15) + 1);
            byte combined = (byte)((function << 4) | sw);
            var outgoing = new byte[_device.OutputLength];
            outgoing[0] = 0x11;
            outgoing[1] = deviceIndex;
            outgoing[2] = featureIndex;
            outgoing[3] = combined;
            Array.Copy(parameters, 0, outgoing, 4, parameters.Length);
            var waiter = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _target = deviceIndex;
                _feature = featureIndex;
                _functionAndSoftware = combined;
                _pending = waiter;
            }
            try
            {
                await _stream.WriteAsync(outgoing, _cts.Token).ConfigureAwait(false);
                var completed = await Task.WhenAny(waiter.Task, Task.Delay(700, _cts.Token)).ConfigureAwait(false);
                if (completed != waiter.Task)
                    throw new TimeoutException($"HID++ yanıt zaman aşımı (slot {deviceIndex:X2}, feature {featureIndex:X2})");
                byte[] result = await waiter.Task.ConfigureAwait(false);
                if (result[2] == 0xFF || result[2] == 0x8F)
                    throw new InvalidOperationException($"HID++ protokol hatası 0x{result[5]:X2}");
                return result;
            }
            finally
            {
                lock (_gate) _pending = null;
            }
        }
        finally { _requests.Release(); }
    }

    public async Task<byte> FeatureIndexAsync(byte slot, ushort featureId)
    {
        byte[] answer = await CallAsync(slot, 0, 0,
            (byte)(featureId >> 8), (byte)featureId).ConfigureAwait(false);
        return answer[4];
    }

    // One receiver interface can host several MX Master mice in slots 1..6.
    // Each session shares this HID reader and releases it only when the last
    // session closes, avoiding double reads and premature device disposal.
    public void AddSession() => Interlocked.Increment(ref _sessionCount);

    public async ValueTask ReleaseSessionAsync()
    {
        if (Interlocked.Decrement(ref _sessionCount) == 0)
            await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed) return;
        _closed = true;
        _cts.Cancel();
        lock (_gate) _pending?.TrySetCanceled();
        _stream.Dispose();
        try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
        catch { /* read may already be stopped */ }
        _cts.Dispose();
        _requests.Dispose();
    }
}

internal sealed class MxMasterSession : IAsyncDisposable
{
    private readonly HidppConnection _link;
    public HidInterface Interface => _link.Interface;
    public byte Slot { get; }
    public string Name { get; }
    public SupportedMouseModel Model { get; }
    public string UnitId { get; }
    public string ModelPids { get; }
    public byte ControlsFeature { get; }
    public IReadOnlyList<MouseControl> Controls { get; }
    public event Action<ushort>? ButtonPressed;
    private HashSet<ushort> _pressed = new();

    private MxMasterSession(HidppConnection link, byte slot, string name,
        SupportedMouseModel model, string unitId, string modelPids,
        byte controlsFeature, IReadOnlyList<MouseControl> controls)
    {
        _link = link;
        Slot = slot;
        Name = name;
        Model = model;
        UnitId = unitId;
        ModelPids = modelPids;
        ControlsFeature = controlsFeature;
        Controls = controls;
        _link.AddSession();
        _link.Notification += OnNotification;
    }

    public override string ToString() => $"{Name} · {Interface.TransportName} · slot 0x{Slot:X2} · {Controls.Count} kontrol";

    public static async Task<MxMasterSession?> ProbeAsync(HidppConnection link, byte slot)
    {
        // All of these discovery calls are read-only. Never write to an unverified device.
        byte deviceInfoIdx;
        try { deviceInfoIdx = await link.FeatureIndexAsync(slot, 0x0003); }
        catch (TimeoutException) { return null; }
        catch (InvalidOperationException) { return null; }
        if (deviceInfoIdx == 0) return null;
        byte[] data;
        try { data = await link.CallAsync(slot, deviceInfoIdx, 0); }
        catch { return null; }
        if (!SupportedModels.TryMatchDeviceInfo(data, link.Interface.ProductId,
            out var model, out string unit, out string pids) || model is null) return null;
        // Keep profiles isolated between *physical* mice and across models,
        // but allow the same unit to use the same profile via Bolt or Bluetooth.
        string identity = unit != "00000000"
            ? $"{model.Key}:unit:{unit}"
            : $"{model.Key}:path:{Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(link.Interface.DevicePath.ToUpperInvariant())).AsSpan(0, 8))}:{slot:X2}";
        string name = model.DisplayName;
        try
        {
            byte nameIndex = await link.FeatureIndexAsync(slot, 0x0005);
            if (nameIndex != 0)
            {
                byte[] len = await link.CallAsync(slot, nameIndex, 0);
                int count = Math.Clamp(len[4], 0, 64);
                var sb = new StringBuilder();
                for (int offset = 0; offset < count; offset += 16)
                {
                    var part = await link.CallAsync(slot, nameIndex, 1, (byte)offset);
                    int n = Math.Min(16, count - offset);
                    sb.Append(Encoding.UTF8.GetString(part, 4, n));
                }
                string actual = sb.ToString().TrimEnd('\0', ' ');
                if (!string.IsNullOrWhiteSpace(actual)) name = actual;
            }
        }
        catch { /* Friendly name is optional, x0003 PID confirmed the model */ }
        byte controlsIndex;
        try { controlsIndex = await link.FeatureIndexAsync(slot, 0x1B04); }
        catch { return new MxMasterSession(link, slot, name, model, identity, pids, 0, []); }
        var controls = new List<MouseControl>();
        if (controlsIndex != 0)
        {
            try
            {
                byte[] countFrame = await link.CallAsync(slot, controlsIndex, 0);
                int count = Math.Min(countFrame[4], (byte)64);
                for (int i = 0; i < count; i++)
                {
                    var row = await link.CallAsync(slot, controlsIndex, 1, (byte)i);
                    ushort cid = (ushort)((row[4] << 8) | row[5]);
                    ushort tid = (ushort)((row[6] << 8) | row[7]);
                    byte flags = row[8];
                    bool rawXy = (row[12] & 1) != 0;
                    controls.Add(new MouseControl(cid, tid, flags, rawXy));
                }
            }
            catch { /* Partial enumeration is still useful for diagnostic view */ }
        }
        return new MxMasterSession(link, slot, name, model, identity, pids, controlsIndex, controls);
    }

    private void OnNotification(byte[] packet)
    {
        if (packet.Length < 12 || packet[1] != Slot || packet[2] != ControlsFeature ||
            ControlsFeature == 0 || packet[3] != 0) return;
        var current = new HashSet<ushort>();
        for (int i = 4; i + 1 < Math.Min(packet.Length, 12); i += 2)
        {
            ushort cid = (ushort)((packet[i] << 8) | packet[i + 1]);
            if (cid != 0) current.Add(cid);
        }
        foreach (ushort cid in current.Except(_pressed))
            ButtonPressed?.Invoke(cid);
        _pressed = current;
    }

    public async Task<ControlReporting> GetReportingAsync(ushort cid)
    {
        if (ControlsFeature == 0) throw new NotSupportedException("0x1B04 mevcut değil");
        var bytes = await _link.CallAsync(Slot, ControlsFeature, 2, (byte)(cid >> 8), (byte)cid);
        return new ControlReporting(cid, bytes[6]);
    }

    public async Task SetTemporaryDivertAsync(ushort cid, bool divert)
    {
        if (ControlsFeature == 0) throw new NotSupportedException("0x1B04 mevcut değil");
        // Only dvalid (bit1) and divert (bit0). No persistent remapping,
        // no raw-XY changes, no alteration to the existing native CID mapping.
        byte flags = divert ? (byte)0x03 : (byte)0x02;
        await _link.CallAsync(Slot, ControlsFeature, 3,
            (byte)(cid >> 8), (byte)cid, flags, 0, 0);
        var verify = await GetReportingAsync(cid);
        if (verify.TemporarilyDiverted != divert)
            throw new IOException($"0x{cid:X4} için yönlendirme doğrulanamadı");
    }

    public async ValueTask DisposeAsync()
    {
        _link.Notification -= OnNotification;
        await _link.ReleaseSessionAsync();
    }
}
