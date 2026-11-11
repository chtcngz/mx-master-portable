import Foundation

// Physical device PIDs from HID++ 0x0003, NOT the Bolt receiver's USB PID (0xC548).
struct SupportedMouseModel: Equatable {
    let key: String
    let displayName: String
    let devicePids: [UInt16]
}

enum SupportedModels {
    static let master3S = SupportedMouseModel(key: "mx-master-3s", displayName: "MX Master 3S", devicePids: [0xB034, 0xB043])
    static let master3SBusiness = SupportedMouseModel(key: "mx-master-3s-business", displayName: "MX Master 3S for Business", devicePids: [0xB035, 0xB044])
    static let master4 = SupportedMouseModel(key: "mx-master-4", displayName: "MX Master 4", devicePids: [0xB042])
    static let master4Business = SupportedMouseModel(key: "mx-master-4-business", displayName: "MX Master 4 for Business", devicePids: [0xB048])

    static let all = [master3S, master3SBusiness, master4, master4Business]

    static func isKnownDirectBluetoothPid(_ pid: UInt16) -> Bool {
        [0xB034, 0xB035, 0xB042, 0xB048].contains(pid)
    }

    struct Match {
        let model: SupportedMouseModel
        let unit: String
        let pids: String
    }

    /// HID++ long report: header 0..3, x0003 fn0 payload:
    /// 0 entity_count, 1..4 unit_id, 5 reserved, 6 transports, 7..12 model PIDs.
    static func match(deviceInfo p: [UInt8], interfacePid: UInt16) -> Match? {
        guard p.count >= 17, p[0] == 0x11 else { return nil }
        let unit = p[5...8].map { String(format: "%02X", $0) }.joined()
        let pids: [UInt16] = (0..<3).map { i in
            let o = 11 + i * 2
            return UInt16(p[o]) << 8 | UInt16(p[o + 1])
        }
        let ids = pids.map { String(format: "%04X", $0) }.joined(separator: ",")
        var model = all.first { m in pids.contains { $0 != 0 && m.devicePids.contains($0) } }
        if model == nil && pids.allSatisfy({ $0 == 0 }) && isKnownDirectBluetoothPid(interfacePid) {
            model = all.first { $0.devicePids.contains(interfacePid) }
        }
        guard let model else { return nil }
        return Match(model: model, unit: unit, pids: ids)
    }
}

struct MouseControl: Identifiable {
    let cid: UInt16
    let taskId: UInt16
    let flags: UInt8
    let rawXY: Bool

    var id: UInt16 { cid }
    var divertable: Bool { flags & 0x20 != 0 }
    var reprogrammable: Bool { flags & 0x10 != 0 }
    var isVirtual: Bool { flags & 0x80 != 0 }
    var editable: Bool { !isVirtual && divertable && reprogrammable }
    var key: String { String(format: "CID:%04X", cid) }

    var title: String {
        switch cid {
        case 0x0050: return "Sol tık"
        case 0x0051: return "Sağ tık"
        case 0x0052: return "Orta tuş"
        case 0x0053: return "Geri"
        case 0x0056: return "İleri"
        case 0x00C3: return "Gesture tuşu"
        case 0x00C4: return "SmartShift / Tekerlek modu"
        case 0x01A0: return "Haptic Sense panel (MX Master 4)"
        default: return String(format: "Kontrol 0x%04X", cid)
        }
    }
}

struct ControlReporting {
    let cid: UInt16
    let flags: UInt8
    var temporarilyDiverted: Bool { flags & 0x01 != 0 }
}
