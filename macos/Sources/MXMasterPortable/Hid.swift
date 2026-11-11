import Foundation
import IOKit
import IOKit.hid

enum HidppError: LocalizedError {
    case timeout(String)
    case protocolError(UInt8)
    case io(String)
    case notPermitted(String)

    var errorDescription: String? {
        switch self {
        case .timeout(let m), .io(let m), .notPermitted(let m): return m
        case .protocolError(let code): return String(format: "HID++ protokol hatası 0x%02X", code)
        }
    }
}

// kIOReturnNotPermitted is a function-like C macro and is not imported into Swift.
private let ioReturnNotPermitted = IOReturn(bitPattern: 0xE000_02E2)

// Input reports arrive on the main run loop. The context is the HidDevice.
private let hidInputCallback: IOHIDReportCallback = { context, _, _, _, reportID, report, length in
    guard let context, length > 0 else { return }
    let device = Unmanaged<HidDevice>.fromOpaque(context).takeUnretainedValue()
    var bytes = Array(UnsafeBufferPointer(start: report, count: length))
    // Keep the Windows frame layout: byte 0 is always the report ID.
    let id = UInt8(truncatingIfNeeded: reportID)
    if reportID != 0 && bytes.first != id { bytes.insert(id, at: 0) }
    device.onFrame?(bytes)
}

/// One Logitech HID interface that exposes the vendor (HID++) collection.
final class HidDevice {
    let ref: IOHIDDevice
    let productId: UInt16
    let transport: String
    let name: String
    let locationId: Int
    let maxInput: Int
    var onFrame: (([UInt8]) -> Void)?
    private var buffer: UnsafeMutablePointer<UInt8>?
    private var bufferSize = 0
    private var isOpen = false

    var isBluetooth: Bool { transport.localizedCaseInsensitiveContains("bluetooth") }
    var transportName: String { isBluetooth ? "Bluetooth" : (productId == 0xC548 ? "Bolt" : "USB") }

    private init(ref: IOHIDDevice) {
        self.ref = ref
        productId = UInt16(truncatingIfNeeded: HidDevice.int(ref, kIOHIDProductIDKey) ?? 0)
        transport = HidDevice.string(ref, kIOHIDTransportKey) ?? ""
        name = HidDevice.string(ref, kIOHIDProductKey) ?? "Logitech HID++"
        locationId = HidDevice.int(ref, kIOHIDLocationIDKey) ?? 0
        maxInput = HidDevice.int(ref, kIOHIDMaxInputReportSizeKey) ?? 64
    }

    private static func int(_ device: IOHIDDevice, _ key: String) -> Int? {
        (IOHIDDeviceGetProperty(device, key as CFString) as? NSNumber)?.intValue
    }

    private static func string(_ device: IOHIDDevice, _ key: String) -> String? {
        IOHIDDeviceGetProperty(device, key as CFString) as? String
    }

    private static let manager: IOHIDManager = {
        let m = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
        IOHIDManagerSetDeviceMatching(m, [kIOHIDVendorIDKey: 0x046D] as CFDictionary)
        return m
    }()

    static func enumerate() -> [HidDevice] {
        guard let set = IOHIDManagerCopyDevices(manager) as? Set<IOHIDDevice> else { return [] }
        let found = set.compactMap { ref -> HidDevice? in
            // C53F is a Lightspeed gaming receiver, not one of the supported mice.
            if int(ref, kIOHIDProductIDKey) == 0xC53F { return nil }
            // macOS exposes one IOHIDDevice per physical device / USB interface,
            // so the vendor collection may not be the primary usage page.
            let pairs = IOHIDDeviceGetProperty(ref, kIOHIDDeviceUsagePairsKey as CFString) as? [[String: Any]] ?? []
            let hasVendorPage = pairs.contains { (($0[kIOHIDDeviceUsagePageKey] as? NSNumber)?.intValue ?? 0) >= 0xFF00 }
                || (int(ref, kIOHIDPrimaryUsagePageKey) ?? 0) >= 0xFF00
            guard hasVendorPage,
                  (int(ref, kIOHIDMaxInputReportSizeKey) ?? 0) >= 20,
                  (int(ref, kIOHIDMaxOutputReportSizeKey) ?? 0) >= 20 else { return nil }
            return HidDevice(ref: ref)
        }
        return found.sorted { $0.locationId < $1.locationId }
    }

    func open() throws {
        guard !isOpen else { return }
        let result = IOHIDDeviceOpen(ref, IOOptionBits(kIOHIDOptionsTypeNone))
        if result == ioReturnNotPermitted {
            throw HidppError.notPermitted("HID erişimi reddedildi. Sistem Ayarları → Gizlilik ve Güvenlik → Giriş İzleme'de MX Master Portable'a izin verin.")
        }
        guard result == kIOReturnSuccess else {
            throw HidppError.io(String(format: "HID aygıtı açılamadı (0x%08X)", UInt32(bitPattern: result)))
        }
        isOpen = true
        bufferSize = max(maxInput, 64)
        let buf = UnsafeMutablePointer<UInt8>.allocate(capacity: bufferSize)
        buffer = buf
        IOHIDDeviceRegisterInputReportCallback(ref, buf, bufferSize, hidInputCallback,
                                               Unmanaged.passUnretained(self).toOpaque())
        IOHIDDeviceScheduleWithRunLoop(ref, CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue)
    }

    /// `data[0]` is the report ID (0x11 for HID++ long reports).
    func send(_ data: [UInt8]) -> IOReturn {
        data.withUnsafeBufferPointer { p in
            IOHIDDeviceSetReport(ref, kIOHIDReportTypeOutput, CFIndex(data[0]), p.baseAddress!, p.count)
        }
    }

    func close() {
        guard isOpen else { return }
        isOpen = false
        onFrame = nil
        IOHIDDeviceUnscheduleFromRunLoop(ref, CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue)
        if let buffer {
            IOHIDDeviceRegisterInputReportCallback(ref, buffer, bufferSize, nil, nil)
        }
        IOHIDDeviceClose(ref, IOOptionBits(kIOHIDOptionsTypeNone))
        buffer?.deallocate()
        buffer = nil
    }
}
