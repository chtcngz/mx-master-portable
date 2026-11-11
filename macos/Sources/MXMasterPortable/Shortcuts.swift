import AppKit
import CoreGraphics

struct ActionOption: Identifiable, Hashable {
    let id: String
    let title: String
}

/// Static allowlist; no shell invocation and no arbitrary code execution.
enum ShortcutActions {
    static let all: [ActionOption] = [
        ActionOption(id: "native", title: "Varsayılan (fare işlevi)"),
        ActionOption(id: "copy", title: "Kopyala · ⌘C"),
        ActionOption(id: "paste", title: "Yapıştır · ⌘V"),
        ActionOption(id: "cut", title: "Kes · ⌘X"),
        ActionOption(id: "undo", title: "Geri al · ⌘Z"),
        ActionOption(id: "redo", title: "Yinele · ⇧⌘Z"),
        ActionOption(id: "tab", title: "Uygulama değiştir · ⌘Tab"),
        ActionOption(id: "missioncontrol", title: "Mission Control · ⌃↑"),
        ActionOption(id: "appwindows", title: "Uygulama pencereleri · ⌃↓"),
        ActionOption(id: "desktop", title: "Masaüstünü göster · F11"),
        ActionOption(id: "explorer", title: "Finder penceresi aç"),
        ActionOption(id: "spotlight", title: "Spotlight · ⌘Boşluk"),
        ActionOption(id: "screenshot", title: "Ekran görüntüsü (alan) · ⇧⌘4"),
        ActionOption(id: "reopen", title: "Sekmeyi yeniden aç · ⇧⌘T"),
        ActionOption(id: "close", title: "Sekmeyi kapat · ⌘W"),
        ActionOption(id: "back", title: "Tarayıcı geri · ⌘["),
        ActionOption(id: "forward", title: "Tarayıcı ileri · ⌘]"),
        ActionOption(id: "mute", title: "Sesi kapat/aç"),
        ActionOption(id: "play", title: "Oynat/duraklat"),
        ActionOption(id: "escape", title: "Esc"),
        ActionOption(id: "enter", title: "Enter"),
        ActionOption(id: "f13", title: "F13"),
        ActionOption(id: "f14", title: "F14")
    ]

    // Virtual key codes (Carbon kVK_*).
    private static let command: CGKeyCode = 0x37
    private static let shift: CGKeyCode = 0x38
    private static let control: CGKeyCode = 0x3B

    static func execute(_ action: String) {
        switch action {
        case "copy": combo([command], 0x08)
        case "paste": combo([command], 0x09)
        case "cut": combo([command], 0x07)
        case "undo": combo([command], 0x06)
        case "redo": combo([command, shift], 0x06)
        case "tab": combo([command], 0x30)
        case "missioncontrol": combo([control], 0x7E)
        case "appwindows": combo([control], 0x7D)
        case "desktop": combo([], 0x67)
        case "explorer": NSWorkspace.shared.open(URL(fileURLWithPath: NSHomeDirectory(), isDirectory: true))
        case "spotlight": combo([command], 0x31)
        case "screenshot": combo([command, shift], 0x15)
        case "reopen": combo([command, shift], 0x11)
        case "close": combo([command], 0x0D)
        case "back": combo([command], 0x21)
        case "forward": combo([command], 0x1E)
        case "mute": media(7)
        case "play": media(16)
        case "escape": combo([], 0x35)
        case "enter": combo([], 0x24)
        case "f13": combo([], 0x69)
        case "f14": combo([], 0x6B)
        default: break
        }
    }

    private static func flag(for key: CGKeyCode) -> CGEventFlags {
        switch key {
        case command: return .maskCommand
        case shift: return .maskShift
        case control: return .maskControl
        default: return []
        }
    }

    /// Presses modifier keys explicitly (needed for ⌘Tab), then the key.
    private static func combo(_ modifiers: [CGKeyCode], _ key: CGKeyCode) {
        let source = CGEventSource(stateID: .hidSystemState)
        var flags: CGEventFlags = []
        func post(_ code: CGKeyCode, _ down: Bool) {
            let event = CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: down)
            event?.flags = flags
            event?.post(tap: .cghidEventTap)
        }
        for m in modifiers {
            flags.insert(flag(for: m))
            post(m, true)
        }
        post(key, true)
        post(key, false)
        for m in modifiers.reversed() {
            flags.remove(flag(for: m))
            post(m, false)
        }
    }

    /// Media keys are NX system-defined events, not ordinary key codes.
    private static func media(_ key: Int) {
        for down in [true, false] {
            let state = down ? 0xA : 0xB
            let event = NSEvent.otherEvent(
                with: .systemDefined, location: .zero,
                modifierFlags: NSEvent.ModifierFlags(rawValue: UInt(state << 8)),
                timestamp: 0, windowNumber: 0, context: nil, subtype: 8,
                data1: (key << 16) | (state << 8), data2: -1)
            event?.cgEvent?.post(tap: .cghidEventTap)
        }
    }
}
