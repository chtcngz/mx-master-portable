import Foundation
import CryptoKit

struct AppProfile: Codable {
    var actions: [String: String] = [:]
    var allowGlobalMouseHook = false

    enum CodingKeys: String, CodingKey {
        case actions = "Actions"
        case allowGlobalMouseHook = "AllowGlobalMouseHook"
    }

    func get(_ id: String) -> String { actions[id] ?? "native" }
}

struct RecoveryJournal: Codable {
    var unitId: String
    var originalTemporaryDiversions: [String: Bool]

    enum CodingKeys: String, CodingKey {
        case unitId = "UnitId"
        case originalTemporaryDiversions = "OriginalTemporaryDiversions"
    }
}

/// Settings live in ~/Library/Application Support/MXMasterPortable.
final class ProfileStore {
    let folder: URL
    private var profilesFolder: URL { folder.appendingPathComponent("profiles", isDirectory: true) }
    private var journalURL: URL { folder.appendingPathComponent("recovery.json") }

    init() {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory())
        folder = base.appendingPathComponent("MXMasterPortable", isDirectory: true)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    // Never put a raw device path or mouse name into the filename.
    private func profileURL(_ identity: String) -> URL {
        let hex = SHA256.hash(data: Data(identity.utf8)).map { String(format: "%02X", $0) }.joined()
        return profilesFolder.appendingPathComponent(hex + ".json")
    }

    private func encoder() -> JSONEncoder {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        return e
    }

    func loadProfile(_ identity: String) -> AppProfile {
        guard let data = try? Data(contentsOf: profileURL(identity)),
              let profile = try? JSONDecoder().decode(AppProfile.self, from: data) else { return AppProfile() }
        return profile
    }

    func saveProfile(_ identity: String, _ profile: AppProfile) throws {
        try FileManager.default.createDirectory(at: profilesFolder, withIntermediateDirectories: true)
        try encoder().encode(profile).write(to: profileURL(identity), options: .atomic)
    }

    func loadJournal() -> RecoveryJournal? {
        guard let data = try? Data(contentsOf: journalURL) else { return nil }
        return try? JSONDecoder().decode(RecoveryJournal.self, from: data)
    }

    func saveJournal(_ journal: RecoveryJournal) throws {
        try encoder().encode(journal).write(to: journalURL, options: .atomic)
    }

    func clearJournal() { try? FileManager.default.removeItem(at: journalURL) }
}
