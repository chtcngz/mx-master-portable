import SwiftUI

@main
struct MXMasterPortableApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate

    var body: some Scene {
        Window("MX Master Portable", id: "main") {
            MainView()
                .environmentObject(AppModel.shared)
                .frame(minWidth: 860, minHeight: 700)
        }
        .defaultSize(width: 980, height: 840)

        MenuBarExtra {
            MenuContent().environmentObject(AppModel.shared)
        } label: {
            MenuLabel().environmentObject(AppModel.shared)
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        Task { await AppModel.shared.scan() }
    }

    // Closing the window keeps the assignments running from the menu bar.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        let model = AppModel.shared
        guard model.needsCleanup else { return .terminateNow }
        Task {
            await model.shutdown()
            sender.reply(toApplicationShouldTerminate: true)
        }
        return .terminateLater
    }
}
