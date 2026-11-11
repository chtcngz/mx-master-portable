import SwiftUI
import ApplicationServices
import IOKit.hid

struct LogLine: Identifiable {
    let id = UUID()
    let text: String
}

struct AssignmentRow: Identifiable {
    let id: String // profile key, e.g. CID:00C3 or SYS:BACK
    let source: String
    let name: String
    let editable: Bool
}

@MainActor
final class AppModel: ObservableObject {
    static let shared = AppModel()

    @Published private(set) var sessions: [MxMasterSession] = []
    @Published var selectedId: UUID? {
        didSet { if oldValue != selectedId { selectionChanged() } }
    }
    @Published private(set) var profile = AppProfile()
    @Published private(set) var log: [LogLine] = []
    @Published private(set) var busy = false
    @Published private(set) var active = false
    @Published private(set) var state = "Hazır"
    @Published private(set) var journalPending = false
    @Published var allowGlobal = false {
        didSet {
            guard !loading, oldValue != allowGlobal else { return }
            profile.allowGlobalMouseHook = allowGlobal
            saveProfile()
        }
    }
    @Published var reuseDiverted = false

    let store = ProfileStore()
    private var mouseTap: MouseTap?
    private var loading = false
    private static let systemButtons = ["SYS:BACK", "SYS:FORWARD", "SYS:MIDDLE"]

    private init() {
        line("Ayarlar dizini: \(store.folder.path)")
        refreshJournal()
        if journalPending {
            line("UYARI: Önceki çalışmadan recovery.json mevcut; aynı mouse'u seçip 'Önceki ayarı kurtar' deyin.")
        }
    }

    var current: MxMasterSession? { sessions.first { $0.id == selectedId } }
    var canEdit: Bool { !busy && !active }
    var canScan: Bool { !busy && !active }
    var canApply: Bool { !busy && !active && current != nil && !journalPending }
    var canStop: Bool { !busy && active }
    var canRecover: Bool {
        guard !busy, !active, let current, let journal = store.loadJournal() else { return false }
        return journal.unitId == current.unitId
    }

    var rows: [AssignmentRow] {
        var result: [AssignmentRow] = []
        if let current {
            for c in current.controls where !c.isVirtual {
                result.append(AssignmentRow(
                    id: c.key,
                    source: c.editable ? String(format: "HID++ 0x%04X", c.cid) : "Değiştirilemez",
                    name: c.title,
                    editable: c.editable))
            }
        }
        result += [
            AssignmentRow(id: "SYS:BACK", source: "macOS · genel", name: "Fare düğmesi 4 / Geri", editable: true),
            AssignmentRow(id: "SYS:FORWARD", source: "macOS · genel", name: "Fare düğmesi 5 / İleri", editable: true),
            AssignmentRow(id: "SYS:MIDDLE", source: "macOS · genel", name: "Fare orta tuşu", editable: true)
        ]
        return result
    }

    func action(for key: String) -> String { profile.get(key) }

    func setAction(_ action: String, for key: String) {
        profile.actions[key] = action
        saveProfile()
    }

    func line(_ text: String) {
        let stamp = DateFormatter.localizedString(from: Date(), dateStyle: .none, timeStyle: .medium)
        log.append(LogLine(text: "[\(stamp)] \(text)"))
        if log.count > 500 { log.removeFirst(log.count - 400) }
    }

    private func setBusy(_ value: Bool, _ title: String = "Hazır") {
        busy = value
        state = title
    }

    private func refreshJournal() { journalPending = store.loadJournal() != nil }

    private func selectionChanged() {
        loading = true
        profile = current.map { store.loadProfile($0.unitId) } ?? AppProfile()
        allowGlobal = profile.allowGlobalMouseHook
        loading = false
    }

    private func saveProfile() {
        guard let current else { return }
        do { try store.saveProfile(current.unitId, profile) }
        catch { line("Profil kaydedilemedi: \(error.localizedDescription)") }
    }

    // MARK: - Scan

    func scan() async {
        guard canScan else { return }
        setBusy(true, "HID taranıyor…")
        defer { setBusy(false) }
        for s in sessions { s.close() }
        sessions = []
        selectedId = nil

        let candidates = HidDevice.enumerate()
        line("Logitech HID++ aday arayüz sayısı: \(candidates.count)")
        var found: [MxMasterSession] = []
        for device in candidates {
            line("Arayüz: \(device.transportName), PID \(String(format: "%04X", device.productId)), \(device.name)")
            let link: HidppConnection
            do {
                link = try HidppConnection(device: device)
            } catch {
                line("Arayüze erişilemedi: \(error.localizedDescription)")
                if case HidppError.notPermitted = error { requestInputMonitoring() }
                continue
            }
            let slots: [UInt8] = device.isBluetooth ? [0xFF] : [1, 2, 3, 4, 5, 6]
            for slot in slots {
                line("Slot \(String(format: "%02X", slot)) sorgulanıyor…")
                guard let session = await MxMasterSession.probe(link, slot: slot) else {
                    if let error = link.lastError { line(error) }
                    continue
                }
                session.onButton = { [weak self, weak session] cid in
                    guard let self, let session else { return }
                    self.onHidButton(session, cid)
                }
                found.append(session)
                line("Bulundu: \(session.model.displayName) · \(session.name) · \(device.transportName), " +
                     "slot \(String(format: "%02X", slot)), PID [\(session.modelPids)], " +
                     "\(session.controls.count) kontrol · 0x1B04 index \(String(format: "%02X", session.controlsFeature))")
            }
            if !link.hasSessions { link.close() }
        }
        sessions = found
        if let first = found.first {
            selectedId = first.id
            line("Hazır. Mouse'u seçin, tuş atamalarını belirleyin ve 'Atamaları uygula' deyin.")
        } else {
            line("MX Master 3S / 4 doğrulanamadı. Mouse'u uyandırın, Bluetooth/Bolt bağlantısını ve izinleri kontrol edin.")
        }
    }

    private func requestInputMonitoring() {
        if IOHIDCheckAccess(kIOHIDRequestTypeListenEvent) != kIOHIDAccessTypeGranted {
            _ = IOHIDRequestAccess(kIOHIDRequestTypeListenEvent)
        }
    }

    // MARK: - Button events

    private func onHidButton(_ session: MxMasterSession, _ cid: UInt16) {
        guard active, session === current else { return }
        let key = String(format: "CID:%04X", cid)
        let action = profile.get(key)
        guard action != "native" else { return }
        ShortcutActions.execute(action)
        line("HID++ tuş \(key) → \(action)")
    }

    private func ensureAccessibility() -> Bool {
        if AXIsProcessTrusted() { return true }
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        line("Kısayol gönderebilmek için Sistem Ayarları → Gizlilik ve Güvenlik → Erişilebilirlik'te " +
             "MX Master Portable'a izin verin, sonra tekrar 'Atamaları uygula' deyin.")
        return false
    }

    // MARK: - Apply / restore

    func apply() async {
        guard canApply, let current else { return }
        let desired = current.controls.filter { $0.editable && profile.get($0.key) != "native" }
        let wantsGlobal = allowGlobal && Self.systemButtons.contains { profile.get($0) != "native" }
        if (!desired.isEmpty || wantsGlobal) && !ensureAccessibility() { return }

        setBusy(true, "Uygulanıyor…")
        do {
            if store.loadJournal() != nil {
                throw HidppError.io("Önce 'Önceki ayarı kurtar' ile kurtarma yapılmalı.")
            }
            var originals: [String: Bool] = [:]
            var reused = Set<UInt16>()
            for c in desired {
                let existing = try await current.getReporting(c.cid)
                if existing.temporarilyDiverted {
                    guard reuseDiverted else {
                        throw HidppError.io(String(format: "0x%04X önceden yönlendirilmiş. Logi Options+ gibi çakışan bir uygulama çalışıyor olabilir.", c.cid))
                    }
                    reused.insert(c.cid)
                    line(String(format: "Mevcut yönlendirme dinleniyor: 0x%04X. Diğer uygulama da bu tuşu işleyebilir.", c.cid))
                    continue
                }
                originals[c.key] = false
            }
            if !originals.isEmpty {
                // Recovery journal must exist BEFORE touching the physical mouse.
                try store.saveJournal(RecoveryJournal(unitId: current.unitId, originalTemporaryDiversions: originals))
                refreshJournal()
                for c in desired where !reused.contains(c.cid) {
                    try await current.setTemporaryDivert(c.cid, true)
                    line("Yönlendirildi: \(c.title) · \(String(format: "0x%04X", c.cid)) → \(profile.get(c.key))")
                }
            }
            if wantsGlobal {
                mouseTap = MouseTap(
                    resolve: { [weak self] id in self?.profile.get(id) ?? "native" },
                    fire: { [weak self] action in
                        ShortcutActions.execute(action)
                        self?.line("macOS fare tuşu → \(action)")
                    })
                line(mouseTap == nil
                     ? "Genel fare kancası kurulamadı (Erişilebilirlik izni gerekli)."
                     : "Genel fare kancası ETKİN: bütün farelerdeki seçili tuşları etkiler.")
            }
            active = true
            line("Atamalar aktif. Program açık kaldığı sürece geçerli.")
        } catch {
            line("UYGULAMA HATASI: \(error.localizedDescription)")
            do { try await restoreCore() } catch { line("Geri alma hatası: \(error.localizedDescription)") }
        }
        refreshJournal()
        setBusy(false, active ? "Aktif" : "Hazır")
    }

    private func restoreCore() async throws {
        mouseTap?.invalidate()
        mouseTap = nil
        active = false
        guard let journal = store.loadJournal() else { return }
        guard let current, journal.unitId == current.unitId else {
            throw HidppError.io("Kurtarma için aynı fiziksel MX Master mouse'u seçin.")
        }
        var errors: [String] = []
        for (key, value) in journal.originalTemporaryDiversions {
            guard key.uppercased().hasPrefix("CID:"), let cid = UInt16(key.dropFirst(4), radix: 16) else { continue }
            do {
                try await current.setTemporaryDivert(cid, value)
                line("Eski durum geri alındı: \(key)")
            } catch {
                errors.append("\(key): \(error.localizedDescription)")
            }
        }
        if !errors.isEmpty {
            throw HidppError.io("Bazı tuşlar geri alınamadı: " + errors.joined(separator: "; "))
        }
        store.clearJournal()
        refreshJournal()
        line("Tüm geçici yönlendirmeler geri alındı.")
    }

    func stop() async {
        guard canStop else { return }
        setBusy(true, "Geri alınıyor…")
        do { try await restoreCore() } catch { line("GERİ ALMA HATASI: \(error.localizedDescription)") }
        setBusy(false)
    }

    func recover() async {
        guard canRecover else { return }
        setBusy(true, "Kurtarılıyor…")
        do { try await restoreCore() } catch { line("KURTARMA HATASI: \(error.localizedDescription)") }
        setBusy(false)
    }

    var needsCleanup: Bool { active }

    /// Called on quit: undo diversions so the mouse behaves normally again.
    func shutdown() async {
        if active {
            do { try await restoreCore() } catch { line("Kapatma uyarısı: \(error.localizedDescription)") }
        }
        for s in sessions { s.close() }
        sessions = []
    }
}
