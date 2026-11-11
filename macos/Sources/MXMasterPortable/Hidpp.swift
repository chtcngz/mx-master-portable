import Foundation
import CryptoKit

/// Serialises async work on the main actor (one HID++ request at a time).
@MainActor
final class AsyncLock {
    private var locked = false
    private var waiters: [CheckedContinuation<Void, Never>] = []

    func run<T>(_ body: () async throws -> T) async rethrows -> T {
        if locked {
            await withCheckedContinuation { waiters.append($0) }
        } else {
            locked = true
        }
        defer {
            if waiters.isEmpty { locked = false } else { waiters.removeFirst().resume() }
        }
        return try await body()
    }
}

@MainActor
final class HidppConnection {
    let device: HidDevice
    private(set) var lastError: String?
    private var listeners: [UUID: ([UInt8]) -> Void] = [:]
    private var pending: CheckedContinuation<[UInt8], Error>?
    private var pendingToken = UUID()
    private var target: UInt8 = 0
    private var feature: UInt8 = 0
    private var functionAndSoftware: UInt8 = 0
    private var counter = 0
    private var sessionCount = 0
    private var closed = false
    private let lock = AsyncLock()

    var hasSessions: Bool { sessionCount > 0 }

    init(device: HidDevice) throws {
        self.device = device
        try device.open()
        device.onFrame = { [weak self] frame in self?.handle(frame) }
    }

    private func handle(_ frame: [UInt8]) {
        // Only HID++ short (0x10) and long (0x11) reports.
        guard frame.count >= 7, frame[0] == 0x10 || frame[0] == 0x11 else { return }
        // Bluetooth requests use 0xFF, but some devices reply with 0x00.
        let deviceMatches = frame[1] == target ||
            (device.isBluetooth && target == 0xFF && frame[1] == 0x00)
        if pending != nil && deviceMatches {
            let match = frame[2] == feature && frame[3] == functionAndSoftware
            let error = (frame[2] == 0xFF || frame[2] == 0x8F) &&
                frame[3] == feature && frame[4] == functionAndSoftware
            if match || error {
                finish(.success(frame))
                return
            }
        }
        if frame[3] & 0x0F == 0 {
            for listener in listeners.values { listener(frame) }
        }
    }

    private func finish(_ result: Result<[UInt8], Error>) {
        let continuation = pending
        pending = nil
        continuation?.resume(with: result)
    }

    private func timeout(_ token: UUID, _ index: UInt8, _ featureIndex: UInt8) {
        guard pending != nil, token == pendingToken else { return }
        let message = String(format: "HID++ yanıt zaman aşımı (slot %02X, feature %02X)", index, featureIndex)
        lastError = message
        finish(.failure(HidppError.timeout(message)))
    }

    func call(_ index: UInt8, _ featureIndex: UInt8, _ function: UInt8,
              _ params: [UInt8] = []) async throws -> [UInt8] {
        precondition(function <= 15 && params.count <= 16)
        return try await lock.run {
            if closed { throw HidppError.io("HID++ bağlantısı kapalı") }
            lastError = nil
            counter = counter % 15 + 1
            let combined = (function << 4) | UInt8(counter)
            var out = [UInt8](repeating: 0, count: 20)
            out[0] = 0x11
            out[1] = index
            out[2] = featureIndex
            out[3] = combined
            for (i, b) in params.enumerated() { out[4 + i] = b }
            target = index
            feature = featureIndex
            functionAndSoftware = combined
            let token = UUID()
            pendingToken = token
            let frame: [UInt8] = try await withCheckedThrowingContinuation { continuation in
                pending = continuation
                let result = device.send(out)
                if result != kIOReturnSuccess {
                    let message = String(format: "HID++ yazma hatası 0x%08X (slot %02X, feature %02X)",
                                         UInt32(bitPattern: result), index, featureIndex)
                    lastError = message
                    finish(.failure(HidppError.io(message)))
                    return
                }
                Task { @MainActor [weak self] in
                    try? await Task.sleep(nanoseconds: 700_000_000)
                    self?.timeout(token, index, featureIndex)
                }
            }
            if frame[2] == 0xFF || frame[2] == 0x8F {
                throw HidppError.protocolError(frame.count > 5 ? frame[5] : 0)
            }
            return frame
        }
    }

    func featureIndex(_ slot: UInt8, _ featureId: UInt16) async throws -> UInt8 {
        let answer = try await call(slot, 0, 0, [UInt8(featureId >> 8), UInt8(featureId & 0xFF)])
        return answer[4]
    }

    func addListener(_ listener: @escaping ([UInt8]) -> Void) -> UUID {
        let id = UUID()
        listeners[id] = listener
        return id
    }

    func removeListener(_ id: UUID) { listeners[id] = nil }

    // One Bolt receiver can host several mice; the link closes with the last session.
    func addSession() { sessionCount += 1 }

    func releaseSession() {
        sessionCount -= 1
        if sessionCount <= 0 { close() }
    }

    func close() {
        guard !closed else { return }
        closed = true
        finish(.failure(HidppError.io("HID++ bağlantısı kapandı")))
        device.close()
    }
}

@MainActor
final class MxMasterSession: Identifiable {
    let id = UUID()
    let link: HidppConnection
    let slot: UInt8
    let name: String
    let model: SupportedMouseModel
    let unitId: String
    let modelPids: String
    let controlsFeature: UInt8
    let controls: [MouseControl]
    var onButton: ((UInt16) -> Void)?
    private var pressed = Set<UInt16>()
    private var listenerId: UUID?

    var title: String {
        "\(name) · \(link.device.transportName) · slot 0x\(String(format: "%02X", slot)) · \(controls.count) kontrol"
    }

    private init(link: HidppConnection, slot: UInt8, name: String, model: SupportedMouseModel,
                 unitId: String, modelPids: String, controlsFeature: UInt8, controls: [MouseControl]) {
        self.link = link
        self.slot = slot
        self.name = name
        self.model = model
        self.unitId = unitId
        self.modelPids = modelPids
        self.controlsFeature = controlsFeature
        self.controls = controls
        link.addSession()
        listenerId = link.addListener { [weak self] packet in self?.onNotification(packet) }
    }

    /// All discovery calls are read-only. Never write to an unverified device.
    static func probe(_ link: HidppConnection, slot: UInt8) async -> MxMasterSession? {
        guard let infoIndex = try? await link.featureIndex(slot, 0x0003), infoIndex != 0,
              let data = try? await link.call(slot, infoIndex, 0),
              let match = SupportedModels.match(deviceInfo: data, interfacePid: link.device.productId)
        else { return nil }

        // Same physical unit keeps one profile across Bolt and Bluetooth.
        let identity: String
        if match.unit != "00000000" {
            identity = "\(match.model.key):unit:\(match.unit)"
        } else {
            let digest = SHA256.hash(data: Data("\(link.device.locationId)".utf8))
            let short = digest.prefix(8).map { String(format: "%02X", $0) }.joined()
            identity = "\(match.model.key):loc:\(short):\(String(format: "%02X", slot))"
        }

        var name = match.model.displayName
        if let nameIndex = try? await link.featureIndex(slot, 0x0005), nameIndex != 0,
           let length = try? await link.call(slot, nameIndex, 0) {
            let count = min(Int(length[4]), 64)
            var bytes: [UInt8] = []
            var offset = 0
            while offset < count {
                guard let part = try? await link.call(slot, nameIndex, 1, [UInt8(offset)]) else { break }
                let n = min(16, count - offset)
                bytes += part[4..<min(part.count, 4 + n)]
                offset += 16
            }
            let actual = String(decoding: bytes, as: UTF8.self)
                .trimmingCharacters(in: CharacterSet(charactersIn: "\u{0} "))
            if !actual.isEmpty { name = actual }
        }

        let controlsIndex = (try? await link.featureIndex(slot, 0x1B04)) ?? 0
        var controls: [MouseControl] = []
        if controlsIndex != 0, let countFrame = try? await link.call(slot, controlsIndex, 0) {
            for i in 0..<min(Int(countFrame[4]), 64) {
                guard let row = try? await link.call(slot, controlsIndex, 1, [UInt8(i)]) else { break }
                controls.append(MouseControl(
                    cid: UInt16(row[4]) << 8 | UInt16(row[5]),
                    taskId: UInt16(row[6]) << 8 | UInt16(row[7]),
                    flags: row[8],
                    rawXY: row[12] & 1 != 0))
            }
        }
        return MxMasterSession(link: link, slot: slot, name: name, model: match.model,
                               unitId: identity, modelPids: match.pids,
                               controlsFeature: controlsIndex, controls: controls)
    }

    private func onNotification(_ p: [UInt8]) {
        guard p.count >= 12 else { return }
        let deviceMatches = p[1] == slot || (link.device.isBluetooth && slot == 0xFF && p[1] == 0x00)
        guard deviceMatches, controlsFeature != 0, p[2] == controlsFeature, p[3] == 0 else { return }
        var current = Set<UInt16>()
        var i = 4
        while i + 1 < min(p.count, 12) {
            let cid = UInt16(p[i]) << 8 | UInt16(p[i + 1])
            if cid != 0 { current.insert(cid) }
            i += 2
        }
        for cid in current.subtracting(pressed) { onButton?(cid) }
        pressed = current
    }

    func getReporting(_ cid: UInt16) async throws -> ControlReporting {
        guard controlsFeature != 0 else { throw HidppError.io("0x1B04 mevcut değil") }
        let bytes = try await link.call(slot, controlsFeature, 2, [UInt8(cid >> 8), UInt8(cid & 0xFF)])
        return ControlReporting(cid: cid, flags: bytes[6])
    }

    /// Only dvalid (bit1) and divert (bit0); no persistent remapping.
    func setTemporaryDivert(_ cid: UInt16, _ divert: Bool) async throws {
        guard controlsFeature != 0 else { throw HidppError.io("0x1B04 mevcut değil") }
        let flags: UInt8 = divert ? 0x03 : 0x02
        _ = try await link.call(slot, controlsFeature, 3, [UInt8(cid >> 8), UInt8(cid & 0xFF), flags, 0, 0])
        let verify = try await getReporting(cid)
        if verify.temporarilyDiverted != divert {
            throw HidppError.io(String(format: "0x%04X için yönlendirme doğrulanamadı", cid))
        }
    }

    func close() {
        if let listenerId { link.removeListener(listenerId) }
        listenerId = nil
        onButton = nil
        link.releaseSession()
    }
}
